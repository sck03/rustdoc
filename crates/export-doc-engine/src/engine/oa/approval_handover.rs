//! Explicit administrator handover changes one unsigned task and preserves prior event snapshots.
use super::super::error::unavailable;
use super::*;
pub(in crate::engine) fn managed_tasks(
    store: &store::Store,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        super::super::units::demand(&actor, &actor.company, "organization.workflows")?;
        let (number, size) = paging(query)?;
        let assignee = query
            .iter()
            .find(|(k, _)| *k == "assigneeId")
            .map(|(_, v)| v.parse::<i64>().map_err(|_| invalid("审批人编号无效。")))
            .transpose()?;
        let (count, mut items) =
            tx.query_workflow_tasks(&export_doc_storage::WorkflowTaskQuery {
                company: &actor.company,
                assignee,
                offset: (number - 1) * size,
                limit: size,
            })?;
        for task in &mut items {
            let context = text(task, "contextId");
            let resource = approval::resource(&text(task, "requestKind"))
                .ok_or_else(|| unavailable("任务业务类型损坏。"))?;
            let eligibility = super::super::units::resolve(
                tx,
                task["assigneeId"]
                    .as_i64()
                    .ok_or_else(|| unavailable("任务人员编号损坏。"))?,
                actor.edition,
                &context,
            )
            .and_then(|candidate| {
                if candidate.company != actor.company {
                    return Err(error(403, "任务身份单位不匹配。"));
                }
                auth::authorize(&candidate, resource, "view")?;
                auth::authorize(&candidate, resource, "approve")
            });
            task["identityAvailable"] = json!(match eligibility {
                Ok(()) => true,
                Err(e) if matches!(e.status, Some(400 | 401 | 403 | 404)) => false,
                Err(e) => return Err(e),
            });
            task["authorizationEndsAt"] = if let Some(id) = context
                .strip_prefix("unit:")
                .and_then(|s| s.split_once(':'))
                .and_then(|(id, _)| id.parse::<i64>().ok())
            {
                json!(
                    tx.get(super::super::units::KIND, id)?
                        .map(|r| text(&r, "endsAt"))
                        .unwrap_or_default()
                )
            } else {
                json!("")
            };
        }
        Ok(json!({"items":items,"totalCount":count,"pageNumber":number,"pageSize":size}))
    })
}

pub(in crate::engine) fn managed_reassign(
    store: &store::Store,
    actor: &Actor,
    body: Value,
) -> Result<Value> {
    store.transaction_as(actor,|tx,actor| {
        super::super::units::demand(actor,&actor.company,"organization.workflows")?;
        let kind = text(&body,"requestKind");
        if !super::approval_references::WORKFLOWS.contains(&kind.as_str()) { return Err(invalid("审批事项类型无效。")); }
        let id = records::positive(&body,"requestId","审批事项")?;
        let mut row = store::get(tx,&kind,id)?;
        if row["companyScope"] != actor.company { return Err(error(403,"事项不属于当前管理单位。")); }
        store::check_version(&row,store::expected(&body))?;
        reassign(tx,actor,&mut row,&body)?;
        let saved = if kind.starts_with("oa-") {
            let meta = json!({"kind":text(&row,"kind"),"resource":approval::resource(&kind).ok_or_else(|| invalid("申请类型无效。"))?});
            save(tx,actor,&meta,row,"approval-reassign",&text(&body,"note"))?
        } else {
            let saved = store::save(tx,&kind,id,row,None,actor,"approval-reassign")?;
            super::super::office_events::append(tx,actor,&kind,&saved,"ApprovalReassign",0,&text(&body,"note"))?;
            saved
        };
        Ok(json!({"requestId":id,"versionNumber":saved["versionNumber"]}))
    })
}
pub(super) fn reassign(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    body: &Value,
) -> Result<()> {
    super::super::units::demand(actor, &text(row, "companyScope"), "organization.workflows")?;
    required(body, "note", "审批移交原因", 500)?;
    required(body, "taskKey", "审批任务", 180)?;
    if !matches!(row["status"].as_str(), Some("Pending" | "Suspended")) {
        return Err(conflict("只有在途申请可以移交未签署任务。"));
    }
    let from = records::positive(body, "fromUserId", "原审批人")?;
    let to = records::positive(body, "toUserId", "接任人")?;
    let mut plan = approval::plan(row)?.ok_or_else(|| conflict("缺少审批流程。"))?;
    let index = plan
        .steps
        .iter()
        .position(|s| {
            s.task_key == text(body, "taskKey")
                && s.approver_user_id == from
                && s.status == "Waiting"
        })
        .ok_or_else(|| conflict("任务已变化或已经签署。"))?;
    let node = plan.steps[index].stage;
    let resource =
        approval::resource(&text(row, "kind")).ok_or_else(|| invalid("未知审批类型。"))?;
    let reviewer = match body.get("toContextId").and_then(Value::as_str) {
        Some(context) => super::super::units::actors_for_operation(
            tx,
            to,
            actor.edition,
            &actor.company,
            resource,
            "approve",
        )?
        .into_iter()
        .find(|a| a.context == context)
        .ok_or_else(|| error(403, "接任身份已失效或不具备此事项的审批权限。"))?,
        None => approval::reviewer(tx, actor, to, row)?,
    };
    if (from == to && plan.steps[index].approver_context == reviewer.context)
        || row["ownerUserId"] == to
        || row["applicantAccountId"] == to
        || plan.independent_signer_conflict(node, to)
        || plan.steps.iter().enumerate().any(|(i, s)| {
            i != index
                && s.approver_user_id == to
                && (s.stage == node
                    || plan.nodes[node].independent_review
                    || plan.nodes[s.stage].independent_review)
        })
    {
        return Err(invalid(
            "接任人不能是申请人、同节点审批人或违反独立复核规则的人员。",
        ));
    }
    plan.steps[index].approver_user_id = to;
    plan.steps[index].approver_name = reviewer.name;
    plan.steps[index].approver_context = reviewer.context;
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["approvalPlan"] = serde_json::to_value(plan)?;
    Ok(())
}
