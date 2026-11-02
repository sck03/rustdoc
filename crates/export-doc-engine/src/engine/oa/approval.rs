//! Assignment-scoped authorization and graph task decisions.
use super::*;
use export_doc_domain::approval::ApprovalPlan;
mod snapshot;
pub(in crate::engine) use snapshot::ReviewSnapshot;
pub(in crate::engine) fn resource(kind: &str) -> Option<&'static str> {
    match business_kind(kind) {
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
pub(in crate::engine) fn business_kind(kind: &str) -> &str {
    match kind {
        "bookings" => "room",
        "supply-requests" => "supply",
        kind => kind.trim_start_matches("oa-"),
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
    reviewer_for(tx, actor, id, row, "approve", true)
}
pub(in crate::engine) fn reviewer_for(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    row: &Value,
    action: &str,
    forbid_subject: bool,
) -> Result<Actor> {
    let resource = resource(&text(row, "kind")).ok_or_else(|| invalid("未知申请类型。"))?;
    let reviewers = super::super::units::actors_for_operation(
        tx,
        id,
        actor.edition,
        &actor.company,
        resource,
        action,
    )?;
    for reviewer in reviewers {
        match qualified_person_for(tx, &reviewer, row, action, forbid_subject) {
            Ok(_) => return Ok(reviewer),
            Err(cause) if cause.status == Some(403) => {}
            Err(cause) => return Err(cause),
        }
    }
    Err(error(
        403,
        "责任人在此单位没有符合申请范围的有效人员审批身份。",
    ))
}
pub(in crate::engine) fn qualified_person(
    tx: &Connection,
    candidate: &Actor,
    row: &Value,
) -> Result<i64> {
    qualified_person_for(tx, candidate, row, "approve", true)
}
pub(in crate::engine) fn qualified_person_for(
    tx: &Connection,
    candidate: &Actor,
    row: &Value,
    action: &str,
    forbid_subject: bool,
) -> Result<i64> {
    let resource = resource(&text(row, "kind")).ok_or_else(|| invalid("未知申请类型。"))?;
    auth::authorize(candidate, resource, "view")?;
    auth::authorize(candidate, resource, action)?;
    if row["companyScope"].is_string()
        && (row["companyScope"] != candidate.company
            || !auth::restriction_allows(candidate, resource, "view", row, true)
            || !auth::restriction_allows(candidate, resource, action, row, true))
    {
        return Err(error(403, "审批事项超出此身份的显式数据范围限制。"));
    }
    let person = super::qualification::person_id(tx, candidate)?;
    if forbid_subject
        && (row["ownerUserId"] == candidate.id
            || row["applicantAccountId"] == candidate.id
            || row["employeeId"] == person)
    {
        return Err(error(403, "申请人或代填人不能承担本事项审批。"));
    }
    Ok(person)
}
pub(in crate::engine) fn queue_approvers(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
) -> Result<Vec<export_doc_storage::RecordApprover>> {
    Ok(super::queue_authority::QueueAuthority::new(tx, actor)?.approvers(resource))
}
pub(in crate::engine) fn object_approvers(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    row: &Value,
) -> Result<Vec<export_doc_storage::RecordApprover>> {
    Ok(super::queue_authority::QueueAuthority::new(tx, actor)?.approvers_for(resource, row))
}
pub(in crate::engine) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    super::approval_routing::submit(tx, actor, row)
}
pub(in crate::engine) fn authority(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
) -> Result<Option<(i64, String)>> {
    Ok(ReviewSnapshot::read(tx, actor, row)?.authority(None))
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
    let mut review = ReviewSnapshot::read(tx, actor, row)?;
    let (principal, delegation) = review
        .authority(Some(task))
        .ok_or_else(|| error(403, "此任务不由您审批，或代理已失效；不能审批本人申请。"))?;
    let next = match action {
        "reject" => "Rejected",
        "return" => "Returned",
        "approve" => "Approved",
        _ => return Err(invalid("未知审批决定。")),
    };
    let mut plan = review
        .plan
        .take()
        .ok_or_else(|| conflict("申请缺少流程实例。"))?;
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
    node.material_policy
        .verify_decision(
            &values,
            &points,
            node.review_points.len(),
            action == "approve",
        )
        .map_err(invalid)?;
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
    let stage = step.stage;
    let person = step.acted_by_person_id;
    let principal_person = step.approver_person_id;
    plan.record_participant(stage, actor.id, person, principal_person, &actor.context)
        .map_err(invalid)?;
    plan.settle(index);
    plan.refresh_subflows().map_err(invalid)?;
    plan.activate(chrono::Utc::now()).map_err(invalid)?;
    super::live_assignments::check(tx, actor, row, &mut plan)?;
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
