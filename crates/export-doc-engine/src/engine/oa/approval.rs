//! Resolves organisation/account references; Domain owns ordered plan semantics.
use super::*;
use export_doc_domain::approval::{ApprovalMode, ApprovalPlan};

pub(in crate::engine) fn resource(kind: &str) -> Option<&'static str> {
    match kind.trim_start_matches("oa-") {
        "personnel" => Some("office.personnel-changes"),
        "leave" => Some("office.leave"),
        "overtime" => Some("office.overtime"),
        "expense" => Some("office.expenses"),
        "travel" => Some("office.travel"),
        "purchase" => Some("office.purchase"),
        "general" => Some("office.general"),
        _ => None,
    }
}
pub(super) fn plan(row: &Value) -> Result<Option<ApprovalPlan>> {
    row.get("approvalPlan")
        .filter(|v| !v.is_null())
        .map(|v| {
            let plan: ApprovalPlan = serde_json::from_value(v.clone())
                .map_err(|e| super::super::error::unavailable(format!("审批步骤损坏：{e}")))?;
            plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
                .map_err(super::super::error::unavailable)?;
            Ok(plan)
        })
        .transpose()
}
pub(super) fn reviewer(tx: &Connection, actor: &Actor, id: i64, row: &Value) -> Result<Actor> {
    let reviewer = auth::current_actor_in(tx, id, actor.edition)?;
    let resource = resource(&text(row, "kind")).ok_or_else(|| invalid("未知申请类型。"))?;
    if !scope_viewable(tx, &reviewer, resource, row)?
        || !auth::visible(&reviewer, resource, "approve", row)
    {
        return Err(error(403, "审批人缺少本申请的查看或审批范围。"));
    }
    Ok(reviewer)
}
pub(in crate::engine) fn queue_approvers(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
) -> Result<Vec<export_doc_storage::RecordApprover>> {
    use export_doc_storage::RecordApprover;
    let mut approvers = vec![RecordApprover {
        user_id: actor.id,
        departments: None,
    }];
    for (id, _) in super::settings::principals(tx, actor)? {
        let principal = match auth::current_actor_in(tx, id, actor.edition) {
            Ok(principal) => principal,
            Err(e) if e.status == Some(403) => continue,
            Err(e) => return Err(e),
        };
        let rank = auth::scope_rank(&principal, resource, "view")
            .min(auth::scope_rank(&principal, resource, "approve"));
        // A named approver cannot own the request; own-only grants cannot review it.
        if principal.company == actor.company && rank >= 2 {
            approvers.push(RecordApprover {
                user_id: id,
                departments: (rank == 2).then_some(principal.departments),
            });
        }
    }
    Ok(approvers)
}
pub(super) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    super::approval_routing::submit(tx, actor, row)
}
pub(in crate::engine) fn authority(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
) -> Result<Option<(i64, String)>> {
    let Some(resource) = resource(&text(row, "kind")) else {
        return Ok(None);
    };
    if row["status"] != "Pending"
        || !viewable(tx, actor, resource, row)?
        || !auth::visible(actor, resource, "approve", row)
    {
        return Ok(None);
    }
    let plan = plan(row)?;
    if row["kind"] == "personnel"
        && (row["ownerUserId"] == actor.id || row["applicantAccountId"] == actor.id)
    {
        return Ok(None);
    }
    if plan.as_ref().is_none_or(|p| p.mode == ApprovalMode::Single) {
        return Ok(
            (tx.provider() == "SQLite" || row["ownerUserId"] != actor.id)
                .then_some((actor.id, String::new())),
        );
    }
    let plan = plan.unwrap();
    let indices = plan.current_indices();
    if indices.is_empty() {
        return Ok(None);
    }
    if row["ownerUserId"] == actor.id
        || row["applicantAccountId"] == actor.id
        || plan
            .steps
            .iter()
            .any(|s| s.status == "Approved" && s.acted_by_user_id == Some(actor.id))
    {
        return Ok(None);
    }
    for index in &indices {
        let principal = plan.steps[*index].approver_user_id;
        if principal == actor.id {
            return Ok(Some((principal, String::new())));
        }
    }
    for (principal, key) in super::settings::principals(tx, actor)? {
        if !indices
            .iter()
            .any(|index| plan.steps[*index].approver_user_id == principal)
        {
            continue;
        }
        match reviewer(tx, actor, principal, row) {
            Ok(_) => return Ok(Some((principal, key))),
            Err(error) if matches!(error.status, Some(400 | 403 | 404)) => continue,
            Err(error) => return Err(error),
        }
    }
    Ok(None)
}
pub(super) fn review(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    action: &str,
    note: &str,
) -> Result<&'static str> {
    let (principal, delegation) = authority(tx, actor, row)?.ok_or_else(|| {
        error(
            403,
            "当前步骤不由您审批，或代理已失效；不能自批或重复代批多级。",
        )
    })?;
    let next = if action == "reject" {
        "Rejected"
    } else {
        "Approved"
    };
    if let Some(mut plan) = plan(row)? {
        if plan.mode != ApprovalMode::Single {
            let index = plan
                .current_indices()
                .into_iter()
                .find(|index| plan.steps[*index].approver_user_id == principal)
                .ok_or_else(|| conflict("当前没有待审批步骤。"))?;
            let step = &mut plan.steps[index];
            step.status = next.into();
            step.acted_by_user_id = Some(actor.id);
            step.acted_by_name = actor.name.clone();
            step.acted_at = store::timestamp();
            step.note = note.into();
            step.delegation_key = delegation;
            plan.close_any_stage(index);
            let current = if action == "reject" {
                None
            } else {
                plan.current()
            };
            row["currentApproverId"] =
                json!(current.map(|i| plan.steps[i].approver_user_id).unwrap_or(0));
            row["approvalPlan"] = serde_json::to_value(plan)?;
            return Ok(if current.is_some() { "Pending" } else { next });
        }
    }
    Ok(next)
}
pub(super) fn remind(actor: &Actor, row: &mut Value) -> Result<()> {
    if row["ownerUserId"] != actor.id || row["status"] != "Pending" {
        return Err(error(403, "只有申请人可催办待审批申请。"));
    }
    let previous = text(row, "lastRemindedAt");
    if !previous.is_empty() {
        let time = chrono::DateTime::parse_from_rfc3339(&previous)
            .map_err(|_| super::super::error::unavailable("催办时间记录损坏。"))?;
        if chrono::Utc::now() - time.with_timezone(&chrono::Utc) < chrono::Duration::hours(1) {
            return Err(error(429, "同一申请每小时最多催办一次。"));
        }
    }
    row["lastRemindedAt"] = json!(store::timestamp());
    Ok(())
}
