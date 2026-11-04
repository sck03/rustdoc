//! Exceptional operations share the domain graph and ordinary transaction save.
use super::*;
use export_doc_domain::approval::{ApprovalStep, RevisionKind};
use serde::Deserialize;

pub(super) fn actions(tx: &Connection, actor: &Actor, row: &Value) -> Result<Vec<String>> {
    let review = approval::ReviewSnapshot::read(tx, actor, row)?;
    Ok(actions_for(
        actor,
        row,
        review.plan.as_ref(),
        &review.task_keys(),
    ))
}
pub(super) fn actions_for(
    actor: &Actor,
    row: &Value,
    plan: Option<&export_doc_domain::approval::ApprovalPlan>,
    tasks: &[String],
) -> Vec<String> {
    let mut actions = vec![];
    let manager =
        super::super::units::demand(actor, &actor.company, "organization.workflows").is_ok();
    let Some(plan) = plan else {
        return actions;
    };
    if !plan.suspended_at.is_empty() {
        if manager {
            actions.extend(["Resume", "Terminate"].map(str::to_owned));
        }
        return actions;
    }
    if manager {
        if plan.nodes.iter().any(|n| {
            n.state == export_doc_domain::approval_graph::NodeState::Blocked
                && n.kind == export_doc_domain::approval_graph::NodeKind::System
        }) {
            actions.push("RetryNode".into());
        }
        if plan.subflows.iter().any(|s| {
            plan.subflow_progress
                .get(&s.key)
                .is_some_and(|p| !matches!(p.outcome.as_str(), "Pending" | "Approved" | "Ignored"))
        }) {
            actions.extend(["RetrySubflow", "CompensateSubflow"].map(str::to_owned));
        }
        if plan.subflows.iter().any(|s| {
            plan.subflow_progress.get(&s.key).is_none_or(|p| {
                matches!(
                    p.state,
                    export_doc_domain::approval_graph::NodeState::Pending
                        | export_doc_domain::approval_graph::NodeState::Active
                        | export_doc_domain::approval_graph::NodeState::Blocked
                )
            })
        }) {
            actions.push("CancelSubflow".into());
        }
    }
    if row["status"] != "Pending"
        && (plan.outcome() != "Approved" || super::fulfillment::business_complete(plan))
    {
        return actions;
    }
    if manager {
        actions.extend(["Suspend", "Terminate"].map(str::to_owned));
    }
    let controlled = controlled_nodes(plan, tasks, manager);
    for &i in &controlled {
        let node = &plan.nodes[i];
        if node.revision_policy.allow_add_signers {
            actions.extend(["AddBefore", "AddAfter", "AddParallel"].map(str::to_owned));
        }
        if node.revision_policy.allow_remove_signers && !node.independent_review {
            actions.push("RemoveSigner".into());
        }
    }
    if ancestors(&plan, &controlled).iter().any(|i| {
        plan.nodes[*i].revision_policy.allow_return_to_node
            && plan.nodes[*i].phase
                == if plan.outcome() == "Approved" {
                    export_doc_domain::approval_graph::Phase::Fulfillment
                } else {
                    export_doc_domain::approval_graph::Phase::Decision
                }
    }) {
        actions.push("ReturnToNode".into());
    }
    actions.sort();
    actions.dedup();
    actions
}
fn controlled_nodes(
    plan: &export_doc_domain::approval::ApprovalPlan,
    tasks: &[String],
    manager: bool,
) -> Vec<usize> {
    plan.current_indices()
        .into_iter()
        .filter(|i| manager || tasks.contains(&plan.steps[*i].task_key))
        .map(|i| plan.steps[i].stage)
        .fold(vec![], |mut nodes, node| {
            if !nodes.contains(&node) {
                nodes.push(node);
            }
            nodes
        })
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
    let key = crate::engine::request_values::find(parameters, "commandKey")
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
    let input = crate::generated_api::OaWorkflowCommandSave::deserialize(body)
        .map_err(|cause| invalid(format!("流程操作字段无效：{cause}")))?;
    let command = input.command.trim();
    let note = input.note.trim();
    records::required_text(command, "流程操作", 40)?;
    records::required_text(note, "操作原因", 500)?;
    let mut review = approval::ReviewSnapshot::read(tx, actor, &row)?;
    let review_tasks = review.task_keys();
    if !actions_for(actor, &row, review.plan.as_ref(), &review_tasks)
        .iter()
        .any(|a| a == command)
    {
        return Err(error(403, "当前身份或发布的节点政策没有开放此操作。"));
    }
    let mut plan = review
        .plan
        .take()
        .ok_or_else(|| conflict("申请没有流程实例。"))?;
    let now = chrono::Utc::now();
    match command {
        "Suspend" => {
            plan.suspended_at = now.to_rfc3339();
            if plan.outcome() == "Pending" {
                row["status"] = json!("Suspended");
            }
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
                    && (matches!(
                        node.state,
                        export_doc_domain::approval_graph::NodeState::Active
                            | export_doc_domain::approval_graph::NodeState::Blocked
                    ) || plan
                        .steps
                        .iter()
                        .any(|s| s.stage == i && s.status == "Waiting"))
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
            if row["status"] == "Suspended" {
                row["status"] = json!(plan.outcome());
            }
            plan.refresh_escalation_deadlines().map_err(invalid)?;
        }
        "Terminate" => {
            if matches!(
                text(&row, "kind").as_str(),
                "room" | "supply" | "bookings" | "supply-requests"
            ) {
                if row["status"] == "Suspended" {
                    row["status"] = json!(plan.outcome());
                }
                if matches!(row["status"].as_str(), Some("Pending" | "Approved")) {
                    super::super::office_workflows::compensate(tx, actor, &mut row, body, true)?;
                } else if matches!(row["status"].as_str(), Some("InUse" | "Issued"))
                    && (row["kind"] == "room" || row["isReturnable"] == true)
                {
                    return Err(conflict(
                        "资源已经实际交付，请先登记真实归还，再终止后续办理。",
                    ));
                }
            }
            if row["kind"] == "personnel" {
                tx.personnel_order_state(
                    records::positive(&row, "id", "人员申请")?,
                    "Cancelled",
                    "",
                )?;
            }
            for task in &mut plan.steps {
                if task.status == "Waiting" {
                    task.status = "Cancelled".into();
                }
            }
            for node in &mut plan.nodes {
                if matches!(
                    node.state,
                    export_doc_domain::approval_graph::NodeState::Active
                        | export_doc_domain::approval_graph::NodeState::Pending
                        | export_doc_domain::approval_graph::NodeState::Blocked
                ) {
                    node.state = export_doc_domain::approval_graph::NodeState::Cancelled;
                }
            }
            plan.suspended_at.clear();
            if !matches!(
                row["status"].as_str(),
                Some("Completed" | "Returned" | "Issued")
            ) {
                row["status"] = json!("Terminated");
            }
        }
        "RetryNode" => {
            let target = input.target_node_key.as_deref().unwrap_or("").trim();
            let node = plan
                .nodes
                .iter_mut()
                .find(|n| {
                    n.key == target
                        && n.kind == export_doc_domain::approval_graph::NodeKind::System
                        && n.state == export_doc_domain::approval_graph::NodeState::Blocked
                })
                .ok_or_else(|| conflict("只有失败的受控系统节点可以重试。"))?;
            node.state = export_doc_domain::approval_graph::NodeState::Active;
            node.error_code.clear();
            if node.system_action == export_doc_domain::approval_graph::SystemAction::ApplyPersonnel
            {
                tx.personnel_order_state(
                    records::positive(&row, "id", "人员申请")?,
                    "Pending",
                    "",
                )?;
            }
            plan.refresh_subflows().map_err(invalid)?;
        }
        "CancelSubflow" | "RetrySubflow" | "CompensateSubflow" => {
            let key = input.subflow_key.as_deref().unwrap_or("").trim();
            records::required_text(key, "子流程", 180)?;
            let child = plan
                .subflows
                .iter()
                .find(|s| s.key == key)
                .ok_or_else(|| invalid("子流程不存在。"))?
                .clone();
            match command {
                "CancelSubflow" => plan.cancel_child(&key).map_err(invalid)?,
                "RetrySubflow" => {
                    plan.retry_child(
                        &key,
                        row["ownerUserId"].as_i64().unwrap_or(0),
                        chrono::Utc::now(),
                    )
                    .map_err(invalid)?;
                    if matches!(
                        row["status"].as_str(),
                        Some("Returned" | "Rejected" | "Cancelled")
                    ) {
                        row["status"] = json!(plan.outcome());
                    }
                    if child.policy.compensation
                        == export_doc_domain::approval_graph::Compensation::ReturnResource
                        && plan.outcome() == "Approved"
                    {
                        row["status"] = json!("Approved");
                        if matches!(text(&row, "kind").as_str(), "supply" | "supply-requests") {
                            row["reservationState"] = json!("Unreserved");
                            row["returnedQuantity"] = json!(0);
                        }
                    }
                }
                _ => {
                    match child.policy.compensation {
                        export_doc_domain::approval_graph::Compensation::None => {
                            return Err(conflict("发布合同未声明补偿。"));
                        }
                        export_doc_domain::approval_graph::Compensation::ManualReview => {}
                        export_doc_domain::approval_graph::Compensation::ReturnResource => {
                            super::super::office_workflows::compensate(
                                tx, actor, &mut row, body, false,
                            )?
                        }
                        export_doc_domain::approval_graph::Compensation::CancelPendingPersonnel => {
                            let id = records::positive(&row, "id", "人员申请")?;
                            if row["kind"] != "personnel"
                                || tx
                                    .personnel_order(id)?
                                    .is_none_or(|o| o["state"] == "Applied")
                            {
                                return Err(conflict("只能补偿尚未生效的人员变动。"));
                            }
                            tx.personnel_order_state(id, "Cancelled", "")?;
                        }
                    }
                    plan.mark_child_compensated(&key).map_err(invalid)?;
                }
            }
            if child.phase == export_doc_domain::approval_graph::Phase::Decision
                && command == "CancelSubflow"
            {
                row["status"] = json!(plan.outcome());
            }
        }
        _ => {
            let revision: RevisionKind =
                serde_json::from_value(json!(command)).map_err(|_| invalid("未知流程修订。"))?;
            let target = input.target_node_key.as_deref().unwrap_or("").trim();
            records::required_text(target, "目标节点", 180)?;
            let manager =
                super::super::units::demand(actor, &actor.company, "organization.workflows")
                    .is_ok();
            let controlled = controlled_nodes(&plan, &review_tasks, manager);
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
            let ids = input.user_ids.as_deref().unwrap_or_default();
            if ids.iter().any(|id| *id <= 0) {
                return Err(invalid("责任人编号无效。"));
            }
            if ids.len() > 50 {
                return Err(invalid("每次最多选择 50 位责任人。"));
            }
            let mut candidates = vec![];
            if matches!(
                revision,
                RevisionKind::AddBefore | RevisionKind::AddAfter | RevisionKind::AddParallel
            ) {
                for id in ids {
                    let target_node = plan
                        .nodes
                        .iter()
                        .find(|n| n.key == target)
                        .ok_or_else(|| invalid("目标节点不存在。"))?;
                    let action = super::actor_binding::node_action(
                        target_node.phase,
                        target_node.kind,
                        target_node.handling_action,
                    );
                    let reviewer = approval::reviewer_for(tx, actor, *id, &row, action, true)?;
                    if row["ownerUserId"] == *id || row["applicantAccountId"] == *id {
                        return Err(error(403, "申请人与代填人不能被加入审批。"));
                    }
                    candidates.push(ApprovalStep {
                        mandatory: false,
                        origin: Default::default(),
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
    super::live_assignments::check(tx, actor, &row, &mut plan)?;
    row["approvalPlan"] = serde_json::to_value(&plan)?;
    tx.append_workflow_operation(&json!({"requestId":row["id"],"submissionNumber":row["submissionNumber"],"generation":plan.generation,"actorId":actor.id,"contextId":actor.context,"command":command,"reason":note,"occurredAt":store::timestamp(),"details":{"targetNodeKey":input.target_node_key,"userIds":input.user_ids}}))?;
    if matches!(command, "RetryNode" | "RetrySubflow") && plan.outcome() == "Approved" {
        return super::fulfillment::advance(
            tx,
            actor,
            meta,
            row,
            super::super::units::today(tx)?,
            "workflow-command",
            &note,
        );
    }
    save(tx, actor, meta, row, "workflow-command", &note)
}
