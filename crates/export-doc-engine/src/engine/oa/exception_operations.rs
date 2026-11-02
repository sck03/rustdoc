//! Technical operators requeue declared system nodes without request material access.
use super::*;
use export_doc_domain::approval_graph::{NodeKind, NodeState, SystemAction};
pub(super) fn retry(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    required(body, "note", "异常处理说明", 500)?;
    let kind = text(body, "requestKind");
    if !super::approval_references::WORKFLOWS.contains(&kind.as_str()) {
        return Err(invalid("申请类型无效。"));
    }
    let id = records::positive(body, "requestId", "申请")?;
    let key = text(body, "nodeKey");
    let mut row = store::get(tx, &kind, id)?;
    if row["companyScope"] != actor.company {
        return Err(error(403, "此异常不属于当前管理单位。"));
    }
    if super::super::command_receipts::prepare(tx, actor, &mut row, "RetryOaSystemNode", body)? {
        return Ok(
            json!({"requestId":id,"versionNumber":row["versionNumber"],"state":row["approvalPlan"]["nodes"].as_array().and_then(|n|n.iter().find(|n|n["key"]==key)).map(|n|n["state"].clone()).unwrap_or(Value::Null)}),
        );
    }
    store::check_version(&row, store::expected(body))?;
    let mut plan = approval::plan(&row)?.ok_or_else(|| conflict("流程实例缺失。"))?;
    if plan.outcome() != "Approved" || !plan.suspended_at.is_empty() {
        return Err(conflict("只能重试未暂停的获批系统动作。"));
    }
    let node = plan
        .nodes
        .iter_mut()
        .find(|n| n.key == key && n.kind == NodeKind::System && n.state == NodeState::Blocked)
        .ok_or_else(|| conflict("此节点没有可重试的系统异常。"))?;
    if node.system_action == SystemAction::ApplyPersonnel {
        tx.personnel_order_state(id, "Pending", "")?;
    }
    node.state = NodeState::Active;
    node.error_code.clear();
    plan.refresh_subflows().map_err(invalid)?;
    row["approvalPlan"] = serde_json::to_value(&plan)?;
    let receipt = row
        .as_object_mut()
        .and_then(|r| r.remove(super::super::command_receipts::FIELD))
        .ok_or_else(|| super::super::error::unavailable("重试回执缺失。"))?;
    let saved = store::save_in_scope(tx, &kind, id, row, None, actor, "retry-system", None)?;
    tx.put_command_receipt(&receipt, &saved)?;
    tx.append_workflow_operation(&json!({"requestId":id,"submissionNumber":saved["submissionNumber"],"generation":plan.generation,"actorId":actor.id,"contextId":actor.context,"command":"RetrySystem","reason":text(body,"note"),"occurredAt":store::timestamp(),"details":{"nodeKey":key}}))?;
    Ok(json!({"requestId":id,"versionNumber":saved["versionNumber"],"state":"Active"}))
}
