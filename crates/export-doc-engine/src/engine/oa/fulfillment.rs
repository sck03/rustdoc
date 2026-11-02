//! Business handlers execute published fulfillment nodes, with current identity checks.
use super::*;
use export_doc_domain::{
    approval::ApprovalPlan,
    approval_graph::{
        ActorBinding, EscalationAction, HandlingAction, NodeExecution, NodeKind, NodeState, Phase,
        SystemAction,
    },
};
fn business_action(action: HandlingAction) -> &'static str {
    match action {
        HandlingAction::Issue => "issue",
        HandlingAction::Return => "return",
        _ => "complete",
    }
}
fn person(tx: &Connection, actor: &Actor) -> Result<Option<i64>> {
    if actor.admin {
        Ok(None)
    } else {
        super::qualification::person_id(tx, actor).map(Some)
    }
}
pub(in crate::engine) fn binding_available(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    binding: &ActorBinding,
) -> Result<bool> {
    if !identity_matches(tx, actor, binding)? {
        return Ok(false);
    }
    Ok(binding
        .person_id
        .is_none_or(|_| binding.origin.source.is_none())
        || super::actor_binding::origin_available(
            tx,
            row,
            binding.person_id.unwrap_or(0),
            &binding.origin,
        )?)
}
pub(super) fn identity_matches(
    tx: &Connection,
    actor: &Actor,
    binding: &ActorBinding,
) -> Result<bool> {
    Ok(binding.user_id == actor.id
        && binding.context == actor.context
        && binding.person_id == person(tx, actor)?
        && binding.administration == actor.admin)
}
pub(super) fn assigned(tx: &Connection, actor: &Actor, row: &Value) -> Result<bool> {
    let Some(plan) = approval::plan(row)? else {
        return Ok(false);
    };
    let Some(resource) = approval::resource(&text(row, "kind")) else {
        return Ok(false);
    };
    let allowed = |action| {
        auth::authorize(actor, resource, "view").is_ok()
            && auth::authorize(actor, resource, action).is_ok()
            && auth::restriction_allows(actor, resource, "view", row, true)
            && auth::restriction_allows(actor, resource, action, row, true)
    };
    for node in &plan.nodes {
        if node.started_at.is_empty() {
            continue;
        }
        let action = if node.kind == NodeKind::Handling {
            business_action(node.handling_action)
        } else {
            "view"
        };
        if allowed(action) {
            for binding in &node.actors {
                if binding_available(tx, actor, row, binding)? {
                    return Ok(true);
                }
            }
        }
        for (level, bindings) in node
            .escalation_targets
            .iter()
            .enumerate()
            .take(usize::from(node.escalation_cursor))
        {
            let escalation = node
                .escalations
                .get(level)
                .ok_or_else(|| crate::engine::error::unavailable("运行节点的升级来源不完整。"))?;
            let action = if escalation.action == EscalationAction::NotifySupervisor {
                "view"
            } else {
                action
            };
            if !allowed(action) {
                continue;
            }
            for binding in bindings {
                if binding_available(tx, actor, row, binding)? {
                    return Ok(true);
                }
            }
        }
    }
    Ok(false)
}
pub(in crate::engine) fn can_handle(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    action: &str,
) -> Result<bool> {
    let Some(plan) = approval::plan(row)? else {
        return Ok(false);
    };
    if plan.outcome() != "Approved" || !plan.suspended_at.is_empty() {
        return Ok(false);
    }
    let resource =
        approval::resource(&text(row, "kind")).ok_or_else(|| invalid("申请类型无效。"))?;
    if auth::authorize(actor, resource, "view").is_err()
        || auth::authorize(actor, resource, action).is_err()
        || !auth::restriction_allows(actor, resource, "view", row, true)
        || !auth::restriction_allows(actor, resource, action, row, true)
    {
        return Ok(false);
    }
    for node in &plan.nodes {
        let stock_retry = action == "issue"
            && row["reservationState"] == "Insufficient"
            && node.state == NodeState::Pending
            && plan.nodes.iter().any(|n| {
                n.system_action == SystemAction::ReserveSupply
                    && n.state == NodeState::Blocked
                    && n.error_code == "StockInsufficient"
            });
        if node.phase == Phase::Fulfillment
            && node.kind == NodeKind::Handling
            && (node.state == NodeState::Active || stock_retry)
            && business_action(node.handling_action) == action
        {
            for binding in &node.actors {
                if binding_available(tx, actor, row, binding)? {
                    return Ok(true);
                }
            }
        }
    }
    let review = approval::ReviewSnapshot::read(tx, actor, row)?;
    if plan.current_indices().iter().any(|i| {
        plan.nodes[plan.steps[*i].stage].phase == Phase::Fulfillment
            && business_action(plan.nodes[plan.steps[*i].stage].handling_action) == action
            && review.task_keys().contains(&plan.steps[*i].task_key)
    }) {
        return Ok(true);
    }
    Ok(false)
}
pub(super) fn tasks(tx: &Connection, actor: &Actor, row: &Value) -> Result<Value> {
    let Some(plan) = approval::plan(row)? else {
        return Ok(json!([]));
    };
    let review = approval::ReviewSnapshot::read(tx, actor, row)?;
    let keys = review.task_keys();
    let mut tasks = vec![];
    for node in &plan.nodes {
        if node.phase != Phase::Fulfillment || node.skipped {
            continue;
        }
        let action = business_action(node.handling_action);
        let task = plan
            .steps
            .iter()
            .find(|s| plan.nodes[s.stage].key == node.key && keys.contains(&s.task_key));
        let available = if node.kind == NodeKind::Handling {
            (node.state == NodeState::Active
                || node.state == NodeState::Pending
                    && node.handling_action == HandlingAction::Issue
                    && row["reservationState"] == "Insufficient")
                && can_handle(tx, actor, row, action)?
                && node.actors.iter().try_fold(false, |found, binding| {
                    Ok::<_, crate::api::ApiError>(
                        found || binding_available(tx, actor, row, binding)?,
                    )
                })?
        } else {
            task.is_some()
        };
        tasks.push(json!({"nodeKey":node.key,"taskKey":task.map(|t|&t.task_key),"name":node.name,"kind":node.kind,"state":node.state,"action":action,"canExecute":available,"dueAt":node.due_at,"errorCode":node.error_code,"executedBy":node.execution.as_ref().map(|e|e.user_id),"executedAt":node.execution.as_ref().map(|e|&e.occurred_at),"materialPolicy":node.material_policy,"reviewPoints":node.review_points}));
    }
    Ok(json!(tasks))
}
pub(in crate::engine) fn progress(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    body: &Value,
    action: &str,
    finished: bool,
) -> Result<bool> {
    let mut plan = approval::plan(row)?.ok_or_else(|| conflict("申请没有已发布的办理图。"))?;
    if plan.outcome() != "Approved" || !plan.suspended_at.is_empty() {
        return Err(conflict("批准尚未成立或办理已暂停。"));
    }
    let target = text(body, "nodeKey");
    let task = text(body, "taskKey");
    if !task.is_empty() {
        let index = plan
            .current_indices()
            .into_iter()
            .find(|i| plan.steps[*i].task_key == task)
            .ok_or_else(|| conflict("办理核验任务已变化。"))?;
        let node = &plan.nodes[plan.steps[index].stage];
        if node.phase != Phase::Fulfillment || business_action(node.handling_action) != action {
            return Err(error(403, "此任务不属于当前办理动作。"));
        }
        let decision = match body["decision"].as_str().unwrap_or("Approve") {
            "Approve" => "approve",
            "Reject" => "reject",
            "Return" => "return",
            _ => return Err(invalid("办理核验决定无效。")),
        };
        approval::review(tx, actor, row, decision, &text(body, "note"), &task, body)?;
        return Ok(business_complete(
            &approval::plan(row)?.ok_or_else(|| conflict("办理图缺失。"))?,
        ));
    }
    let indices = plan
        .nodes
        .iter()
        .enumerate()
        .filter_map(|(i, n)| {
            (n.phase == Phase::Fulfillment
                && n.kind == NodeKind::Handling
                && n.state == NodeState::Active
                && business_action(n.handling_action) == action
                && (target.is_empty() || n.key == target))
                .then_some(i)
        })
        .collect::<Vec<_>>();
    if indices.len() != 1 {
        return Err(conflict("请选择唯一的当前办理节点，或先完成前置办理。"));
    }
    let index = indices[0];
    let node = &plan.nodes[index];
    let available = node.actors.iter().try_fold(false, |found, binding| {
        Ok::<_, crate::api::ApiError>(found || binding_available(tx, actor, row, binding)?)
    })?;
    if !can_handle(tx, actor, row, action)? || !available {
        return Err(error(403, "此节点未分派给当前有效身份。"));
    }
    let identity = person(tx, actor)?;
    if node.independent_review
        && (identity.is_none() || plan.signer_conflict(index, actor.id, identity))
    {
        return Err(error(
            403,
            "实际验收须由独立自然人办理，不能复用前序审批或办理人。",
        ));
    }
    let values =
        serde_json::from_value(body.get("verificationValues").cloned().unwrap_or(json!({})))
            .map_err(|_| invalid("办理核验格式无效。"))?;
    let points: Vec<u16> = serde_json::from_value(
        body.get("confirmedReviewPoints")
            .cloned()
            .unwrap_or(json!([])),
    )
    .map_err(|_| invalid("办理要点确认格式无效。"))?;
    node.material_policy
        .verify(&values, &points, node.review_points.len())
        .map_err(invalid)?;
    let node = &mut plan.nodes[index];
    node.state = if finished {
        NodeState::Completed
    } else {
        NodeState::Active
    };
    node.execution = Some(NodeExecution {
        user_id: actor.id,
        person_id: identity,
        context: actor.context.clone(),
        occurred_at: store::timestamp(),
        note: text(body, "note"),
        verification_values: values,
    });
    plan.record_participant(index, actor.id, identity, None, &actor.context)
        .map_err(invalid)?;
    plan.refresh_subflows().map_err(invalid)?;
    plan.activate(chrono::Utc::now()).map_err(invalid)?;
    super::live_assignments::check(tx, actor, row, &mut plan)?;
    let completed = business_complete(&plan);
    row["approvalPlan"] = serde_json::to_value(plan)?;
    Ok(completed)
}
pub(super) fn business_complete(plan: &ApprovalPlan) -> bool {
    plan.nodes
        .iter()
        .enumerate()
        .filter(|(_, n)| {
            n.phase == Phase::Fulfillment
                && n.system_action != SystemAction::GenerateApprovalDocument
        })
        .all(|(i, n)| n.skipped || plan.completed(i))
}
pub(super) fn state(plan: &ApprovalPlan) -> &'static str {
    if !plan.suspended_at.is_empty() {
        return "Suspended";
    }
    if plan
        .nodes
        .iter()
        .any(|n| n.phase == Phase::Fulfillment && n.state == NodeState::Blocked)
    {
        return "Blocked";
    }
    if plan
        .nodes
        .iter()
        .any(|n| n.phase == Phase::Fulfillment && n.state == NodeState::Cancelled)
    {
        return "Cancelled";
    }
    if business_complete(plan) {
        return "Completed";
    }
    if plan
        .nodes
        .iter()
        .any(|n| n.phase == Phase::Fulfillment && n.state == NodeState::Active)
    {
        "Active"
    } else {
        "Pending"
    }
}
pub(in crate::engine) fn active_system(row: &Value, action: SystemAction) -> Result<bool> {
    Ok(approval::plan(row)?.is_some_and(|p| {
        p.outcome() == "Approved"
            && p.suspended_at.is_empty()
            && p.nodes
                .iter()
                .any(|n| n.system_action == action && !n.skipped && n.state == NodeState::Active)
    }))
}
pub(super) fn execute(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    mut row: Value,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    required(body, "note", "办理说明", 500)?;
    super::validation::completion(&row, today)?;
    progress(tx, actor, &mut row, body, "complete", true)?;
    advance(
        tx,
        actor,
        meta,
        row,
        today,
        "handling-step",
        &text(body, "note"),
    )
}
pub(super) fn advance(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    mut row: Value,
    today: chrono::NaiveDate,
    mut event: &str,
    note: &str,
) -> Result<Value> {
    if active_system(&row, SystemAction::ReserveSupply)? {
        super::super::office_workflows::reserve(tx, actor, &mut row, today)?;
        let insufficient = row["reservationState"] == "Insufficient";
        system_result(
            &mut row,
            SystemAction::ReserveSupply,
            if insufficient {
                NodeState::Blocked
            } else {
                NodeState::Completed
            },
            if insufficient {
                "StockInsufficient"
            } else {
                ""
            },
        )?;
    }
    if active_system(&row, SystemAction::ApplyPersonnel)? {
        if super::personnel_changes::effective_date(&row)? > today {
            super::runtime::schedule(tx, actor, &row)?;
        } else {
            return super::personnel_changes::commit(tx, actor, meta, row, today, event, note);
        }
    }
    if let Some(plan) = approval::plan(&row)? {
        if business_complete(&plan)
            && !matches!(
                text(&row, "kind").as_str(),
                "supply" | "supply-requests" | "room" | "bookings"
            )
        {
            row["status"] = json!(if row["kind"] == "expense" {
                "HandedOff"
            } else {
                "Completed"
            });
            row["completedBy"] = json!(actor.name);
            row["completedAt"] = json!(store::timestamp());
            row["completedNote"] = json!(note);
            event = "complete";
        }
    }
    save(tx, actor, meta, row, event, note)
}
pub(in crate::engine) fn system_result(
    row: &mut Value,
    action: SystemAction,
    state: NodeState,
    error_code: &str,
) -> Result<()> {
    let Some(mut plan) = approval::plan(row)? else {
        return Ok(());
    };
    for node in plan.nodes.iter_mut().filter(|n| {
        n.kind == NodeKind::System
            && n.system_action == action
            && !n.skipped
            && matches!(n.state, NodeState::Active | NodeState::Blocked)
    }) {
        node.state = state;
        node.error_code = error_code.into();
    }
    plan.refresh_subflows().map_err(invalid)?;
    plan.activate(chrono::Utc::now()).map_err(invalid)?;
    row["approvalPlan"] = serde_json::to_value(plan)?;
    Ok(())
}
