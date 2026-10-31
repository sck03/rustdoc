//! Assignment-scoped authorization and graph task decisions.
use super::*;
use export_doc_domain::approval::ApprovalPlan;
pub(in crate::engine) fn resource(kind: &str) -> Option<&'static str> {
    match kind.trim_start_matches("oa-") {
        "personnel" => Some("office.personnel-changes"),
        "leave" => Some("office.leave"),
        "overtime" => Some("office.overtime"),
        "expense" => Some("office.expenses"),
        "travel" => Some("office.travel"),
        "purchase" => Some("office.purchase"),
        "general" => Some("office.general"),
        "room" | "bookings" => Some("office.rooms"),
        "supply" | "supply-requests" => Some("office.supplies"),
        _ => None,
    }
}
pub(in crate::engine) fn resource_for_collaboration(resource: &str) -> bool {
    export_doc_domain::permissions::resource(resource)
        .is_some_and(|resource| resource.workflow_resource)
}
pub(in crate::engine) fn plan(row: &Value) -> Result<Option<ApprovalPlan>> {
    row.get("approvalPlan")
        .filter(|v| !v.is_null())
        .map(|v| {
            let plan: ApprovalPlan = serde_json::from_value(v.clone())
                .map_err(|e| super::super::error::unavailable(format!("审批实例损坏：{e}")))?;
            plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
                .map_err(super::super::error::unavailable)?;
            Ok(plan)
        })
        .transpose()
}
pub(in crate::engine) fn reviewer(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    row: &Value,
) -> Result<Actor> {
    let resource = resource(&text(row, "kind")).ok_or_else(|| invalid("未知申请类型。"))?;
    let reviewer = super::super::units::for_operation(
        tx,
        id,
        actor.edition,
        &actor.company,
        resource,
        "approve",
    )?;
    super::qualification::person_id(tx, &reviewer)?;
    Ok(reviewer)
}
pub(in crate::engine) fn queue_approvers(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
) -> Result<Vec<export_doc_storage::RecordApprover>> {
    Ok(super::queue_authority::QueueAuthority::new(tx, actor)?.approvers(resource))
}
pub(in crate::engine) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    super::approval_routing::submit(tx, actor, row)
}
pub(in crate::engine) fn authority(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
) -> Result<Option<(i64, String)>> {
    authority_for(tx, actor, row, None)
}
pub(in crate::engine) fn review_tasks(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
) -> Result<Vec<String>> {
    let mut tasks = vec![];
    if let Some(plan) = plan(row)? {
        for i in plan.current_indices() {
            if authority_for(tx, actor, row, Some(&plan.steps[i].task_key))?.is_some() {
                tasks.push(plan.steps[i].task_key.clone());
            }
        }
    }
    Ok(tasks)
}
fn authority_for(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    task: Option<&str>,
) -> Result<Option<(i64, String)>> {
    let Some(resource) = resource(&text(row, "kind")) else {
        return Ok(None);
    };
    if row["status"] != "Pending"
        || row["companyScope"] != actor.company
        || row["ownerUserId"] == actor.id
        || row["applicantAccountId"] == actor.id
        || auth::authorize(actor, resource, "view").is_err()
        || auth::authorize(actor, resource, "approve").is_err()
    {
        return Ok(None);
    }
    let signer = match super::qualification::person_id(tx, actor) {
        Ok(id) => id,
        Err(cause) if cause.status == Some(403) => return Ok(None),
        Err(cause) => return Err(cause),
    };
    let Some(plan) = plan(row)? else {
        return Ok(None);
    };
    let indices: Vec<_> = plan
        .current_indices()
        .into_iter()
        .filter(|i| {
            task.is_none_or(|key| plan.steps[*i].task_key == key)
                && !plan.independent_signer_conflict(plan.steps[*i].stage, actor.id)
                && !(plan.nodes[plan.steps[*i].stage].independent_review
                    && plan.steps.iter().any(|s| {
                        s.stage != plan.steps[*i].stage
                            && s.acted_by_user_id.is_some()
                            && (s.acted_by_person_id == Some(signer)
                                || s.approver_person_id == Some(signer))
                    }))
        })
        .collect();
    if indices.iter().any(|i| {
        plan.steps[*i].approver_user_id == actor.id
            && plan.steps[*i].approver_context == actor.context
            && plan.steps[*i]
                .approver_person_id
                .is_none_or(|id| id == signer)
    }) {
        return Ok(Some((actor.id, String::new())));
    }
    for (principal, key) in settings::principals(tx, actor)? {
        if !indices
            .iter()
            .any(|i| plan.steps[*i].approver_user_id == principal)
        {
            continue;
        }
        for i in indices
            .iter()
            .filter(|i| plan.steps[**i].approver_user_id == principal)
        {
            match super::super::units::resolve(
                tx,
                principal,
                actor.edition,
                &plan.steps[*i].approver_context,
            ) {
                Ok(p)
                    if p.company == actor.company
                        && auth::authorize(&p, resource, "view").is_ok()
                        && auth::authorize(&p, resource, "approve").is_ok() =>
                {
                    return Ok(Some((principal, key)));
                }
                Ok(_) => {}
                Err(e) if matches!(e.status, Some(400 | 401 | 403 | 404)) => {}
                Err(e) => return Err(e),
            }
        }
    }
    Ok(None)
}
pub(in crate::engine) fn review(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    action: &str,
    note: &str,
    task: &str,
    body: &Value,
) -> Result<&'static str> {
    if task.is_empty() {
        return Err(invalid("请选择具体审批任务，刷新后重试。"));
    }
    let (principal, delegation) = authority_for(tx, actor, row, Some(task))?
        .ok_or_else(|| error(403, "此任务不由您审批，或代理已失效；不能审批本人申请。"))?;
    let next = match action {
        "reject" => "Rejected",
        "return" => "Returned",
        "approve" => "Approved",
        _ => return Err(invalid("未知审批决定。")),
    };
    let mut plan = plan(row)?.ok_or_else(|| conflict("申请缺少流程实例。"))?;
    let index = plan
        .current_indices()
        .into_iter()
        .find(|i| plan.steps[*i].task_key == task && plan.steps[*i].approver_user_id == principal)
        .ok_or_else(|| conflict("审批任务已变化。"))?;
    let values =
        serde_json::from_value(body.get("verificationValues").cloned().unwrap_or(json!({})))
            .map_err(|_| invalid("核验值格式无效。"))?;
    let points = serde_json::from_value::<Vec<u16>>(
        body.get("confirmedReviewPoints")
            .cloned()
            .unwrap_or(json!([])),
    )
    .map_err(|_| invalid("审查要点确认格式无效。"))?;
    let node = &plan.nodes[plan.steps[index].stage];
    if action == "approve" {
        node.material_policy
            .verify(&values, &points, node.review_points.len())
            .map_err(invalid)?;
    }
    let step = &mut plan.steps[index];
    step.verification_values = values;
    step.confirmed_review_points = points;
    step.status = next.into();
    step.acted_by_user_id = Some(actor.id);
    step.acted_by_person_id = Some(super::qualification::person_id(tx, actor)?);
    step.acted_by_name = actor.name.clone();
    step.acted_by_context = actor.context.clone();
    step.acted_at = store::timestamp();
    step.note = note.into();
    step.delegation_key = delegation;
    plan.settle(index);
    plan.activate(chrono::Utc::now()).map_err(invalid)?;
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    let outcome = plan.outcome();
    row["approvalPlan"] = serde_json::to_value(plan)?;
    Ok(outcome)
}
pub(super) fn remind(actor: &Actor, row: &mut Value) -> Result<()> {
    if row["ownerUserId"] != actor.id || row["status"] != "Pending" {
        return Err(error(403, "只有申请人可催办待审批申请。"));
    }
    let previous = text(row, "lastRemindedAt");
    if !previous.is_empty() {
        let time = chrono::DateTime::parse_from_rfc3339(&previous)
            .map_err(|_| super::super::error::unavailable("催办时间损坏。"))?;
        if chrono::Utc::now() - time.with_timezone(&chrono::Utc) < chrono::Duration::hours(1) {
            return Err(error(429, "同一申请每小时最多催办一次。"));
        }
    }
    row["lastRemindedAt"] = json!(store::timestamp());
    Ok(())
}
