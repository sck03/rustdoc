//! Exceptional operations share the domain graph and ordinary transaction save.
use super::*;
use export_doc_domain::approval::{ApprovalStep, RevisionKind};

pub(super) fn actions(tx: &Connection, actor: &Actor, row: &Value) -> Result<Vec<String>> {
    let mut actions = vec![];
    let manager =
        super::super::units::demand(actor, &actor.company, "organization.workflows").is_ok();
    let Some(plan) = approval::plan(row)? else {
        return Ok(actions);
    };
    if row["status"] == "Suspended" {
        if manager {
            actions.extend(["Resume", "Terminate"].map(str::to_owned));
        }
        return Ok(actions);
    }
    if row["status"] != "Pending" {
        return Ok(actions);
    }
    if manager {
        actions.extend(["Suspend", "Terminate"].map(str::to_owned));
    }
    let controlled = controlled_nodes(tx, actor, row, &plan, manager)?;
    for &i in &controlled {
        let node = &plan.nodes[i];
        if node.revision_policy.allow_add_signers {
            actions.extend(["AddBefore", "AddAfter", "AddParallel"].map(str::to_owned));
        }
        if node.revision_policy.allow_remove_signers && !node.independent_review {
            actions.push("RemoveSigner".into());
        }
    }
    if ancestors(&plan, &controlled)
        .iter()
        .any(|i| plan.nodes[*i].revision_policy.allow_return_to_node)
    {
        actions.push("ReturnToNode".into());
    }
    actions.sort();
    actions.dedup();
    Ok(actions)
}
fn controlled_nodes(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    plan: &export_doc_domain::approval::ApprovalPlan,
    manager: bool,
) -> Result<Vec<usize>> {
    let tasks = approval::review_tasks(tx, actor, row)?;
    Ok(plan
        .current_indices()
        .into_iter()
        .filter(|i| manager || tasks.contains(&plan.steps[*i].task_key))
        .map(|i| plan.steps[i].stage)
        .collect())
}
fn ancestors(plan: &export_doc_domain::approval::ApprovalPlan, current: &[usize]) -> Vec<usize> {
    let mut result = current.to_vec();
    let mut cursor = 0;
    while cursor < result.len() {
        for dependency in &plan.nodes[result[cursor]].depends_on {
            if let Some(i) = plan.nodes.iter().position(|n| n.key == *dependency) {
                if !result.contains(&i) {
                    result.push(i);
                }
            }
        }
        cursor += 1;
    }
    result
}
pub(super) fn result(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    parameters: &[(&str, String)],
) -> Result<Value> {
    let key = parameters
        .iter()
        .find(|(k, _)| *k == "commandKey")
        .map(|(_, v)| v.as_str())
        .ok_or_else(|| invalid("操作编号缺失。"))?;
    let receipt = tx
        .command_receipt(actor.id, &actor.context, key)?
        .filter(|r| r["targetId"] == row["id"])
        .ok_or_else(|| error(404, "未找到当前身份的操作回执。"))?;
    Ok(
        json!({"requestId":row["id"],"commandKey":key,"command":receipt["command"],"committedAt":receipt["createdAt"],"committedVersion":receipt["result"]["versionNumber"],"currentVersion":row["versionNumber"]}),
    )
}
pub(super) fn candidates(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    query: &[(&str, String)],
) -> Result<Value> {
    if !actions(tx, actor, row)?
        .iter()
        .any(|a| a.starts_with("Add"))
    {
        return Err(error(403, "当前没有可加签的审批任务。"));
    }
    let today = super::super::units::today(tx)?.to_string();
    let before = query
        .iter()
        .find(|(key, _)| *key == "beforeId")
        .map(|(_, value)| {
            value
                .parse::<i64>()
                .ok()
                .filter(|id| *id > 0)
                .ok_or_else(|| invalid("候选人游标无效。"))
        })
        .transpose()?;
    let accounts = tx.unit_account_page(&actor.company, &today, false, before, 100)?;
    let next = if accounts.len() == 100 {
        accounts.last().map(|a| a["id"].clone())
    } else {
        None
    };
    let mut users = vec![];
    for account in accounts {
        crate::operation::check()?;
        let id = records::positive(&account, "id", "候选人")?;
        if row["ownerUserId"] == id || row["applicantAccountId"] == id {
            continue;
        }
        match approval::reviewer(tx, actor, id, row) {
            Ok(reviewer) => users.push(json!({"id":id,"fullName":reviewer.name})),
            Err(cause) if matches!(cause.status, Some(400 | 401 | 403 | 404)) => {}
            Err(cause) => return Err(cause),
        }
    }
    Ok(json!({"users":users,"nextCursor":next}))
}
pub(super) fn execute(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    mut row: Value,
    body: &Value,
) -> Result<Value> {
    required(body, "command", "流程操作", 40)?;
    required(body, "note", "操作原因", 500)?;
    let command = text(body, "command");
    let note = text(body, "note");
    if !actions(tx, actor, &row)?.iter().any(|a| a == &command) {
        return Err(error(403, "当前身份或发布的节点政策没有开放此操作。"));
    }
    let mut plan = approval::plan(&row)?.ok_or_else(|| conflict("申请没有流程实例。"))?;
    let now = chrono::Utc::now();
    match command.as_str() {
        "Suspend" => {
            plan.suspended_at = now.to_rfc3339();
            row["status"] = json!("Suspended");
        }
        "Resume" => {
            let since = chrono::DateTime::parse_from_rfc3339(&plan.suspended_at)
                .map_err(|_| super::super::error::unavailable("暂停时间损坏。"))?;
            let pause = now.signed_duration_since(since);
            if pause < chrono::Duration::zero() {
                return Err(super::super::error::unavailable("恢复时间早于暂停时间。"));
            }
            for (i, node) in plan.nodes.iter_mut().enumerate() {
                if node.revision_policy.pause_stops_clock
                    && !node.due_at.is_empty()
                    && plan
                        .steps
                        .iter()
                        .any(|s| s.stage == i && s.status == "Waiting")
                {
                    let due = chrono::DateTime::parse_from_rfc3339(&node.due_at)
                        .map_err(|_| super::super::error::unavailable("节点期限损坏。"))?;
                    node.due_at = if node.deadline_mode
                        == export_doc_domain::business_calendar::DeadlineMode::Working
                        && due > since
                    {
                        let calendar = plan
                            .calendars
                            .get(&node.calendar_key)
                            .ok_or_else(|| super::super::error::unavailable("节点日历缺失。"))?;
                        let remaining = calendar
                            .elapsed(
                                since.with_timezone(&chrono::Utc),
                                due.with_timezone(&chrono::Utc),
                            )
                            .map_err(invalid)?;
                        if remaining > 0 {
                            calendar
                                .deadline(now, remaining)
                                .map_err(invalid)?
                                .to_rfc3339()
                        } else {
                            due.to_rfc3339()
                        }
                    } else {
                        (due + pause).to_rfc3339()
                    };
                }
            }
            plan.paused_seconds = plan
                .paused_seconds
                .checked_add(pause.num_seconds() as u64)
                .ok_or_else(|| invalid("暂停时间累计溢出。"))?;
            plan.suspended_at.clear();
            row["status"] = json!("Pending");
        }
        "Terminate" => {
            for task in &mut plan.steps {
                if task.status == "Waiting" {
                    task.status = "Cancelled".into();
                }
            }
            row["status"] = json!("Terminated");
        }
        _ => {
            let revision: RevisionKind =
                serde_json::from_value(json!(command)).map_err(|_| invalid("未知流程修订。"))?;
            required(body, "targetNodeKey", "目标节点", 180)?;
            let target = text(body, "targetNodeKey");
            let manager =
                super::super::units::demand(actor, &actor.company, "organization.workflows")
                    .is_ok();
            let controlled = controlled_nodes(tx, actor, &row, &plan, manager)?;
            let permitted = if revision == RevisionKind::ReturnToNode {
                ancestors(&plan, &controlled)
            } else {
                controlled
            };
            if !permitted.iter().any(|i| plan.nodes[*i].key == target) {
                return Err(error(
                    403,
                    "只能调整当前负责的审批节点，或退回其已执行的前置节点。",
                ));
            }
            let ids = body["userIds"]
                .as_array()
                .map(|ids| {
                    ids.iter()
                        .map(|id| {
                            id.as_i64()
                                .filter(|id| *id > 0)
                                .ok_or_else(|| invalid("责任人编号无效。"))
                        })
                        .collect::<Result<Vec<_>>>()
                })
                .transpose()?
                .unwrap_or_default();
            if ids.len() > 50 {
                return Err(invalid("每次最多选择 50 位责任人。"));
            }
            let mut candidates = vec![];
            if matches!(
                revision,
                RevisionKind::AddBefore | RevisionKind::AddAfter | RevisionKind::AddParallel
            ) {
                for id in &ids {
                    let reviewer = approval::reviewer(tx, actor, *id, &row)?;
                    if row["ownerUserId"] == *id || row["applicantAccountId"] == *id {
                        return Err(error(403, "申请人与代填人不能被加入审批。"));
                    }
                    candidates.push(ApprovalStep {
                        approver_person_id: Some(super::qualification::person_id(tx, &reviewer)?),
                        acted_by_person_id: None,
                        verification_values: Default::default(),
                        confirmed_review_points: vec![],
                        task_key: String::new(),
                        stage: 0,
                        decision: export_doc_domain::approval::ApprovalDecision::All,
                        approver_user_id: *id,
                        approver_name: reviewer.name,
                        approver_context: reviewer.context,
                        status: "Waiting".into(),
                        acted_by_user_id: None,
                        acted_by_name: String::new(),
                        acted_by_context: String::new(),
                        acted_at: String::new(),
                        note: String::new(),
                        delegation_key: String::new(),
                        source_generation: None,
                    });
                }
            }
            plan.revise(
                revision,
                &target,
                &candidates,
                &ids,
                row["ownerUserId"].as_i64().unwrap_or(0),
            )
            .map_err(invalid)?;
            plan.activate(now).map_err(invalid)?;
        }
    }
    row["approvalPlan"] = serde_json::to_value(&plan)?;
    tx.append_workflow_operation(&json!({"requestId":row["id"],"submissionNumber":row["submissionNumber"],"generation":plan.generation,"actorId":actor.id,"contextId":actor.context,"command":command,"reason":note,"occurredAt":store::timestamp(),"details":{"targetNodeKey":body["targetNodeKey"],"userIds":body["userIds"]}}))?;
    save(tx, actor, meta, row, "workflow-command", &note)
}
