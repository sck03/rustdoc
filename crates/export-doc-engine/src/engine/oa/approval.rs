//! Resolves organisation/account references; Domain owns ordered plan semantics.
use super::*;
use export_doc_domain::approval::{ApprovalMode, ApprovalPlan, ApprovalStep};
use std::collections::HashSet;

pub(in crate::engine) fn resource(kind: &str) -> Option<&'static str> {
    match kind.trim_start_matches("oa-") {
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
fn reviewer(tx: &Connection, actor: &Actor, id: i64, row: &Value) -> Result<Actor> {
    let reviewer = auth::current_actor_in(tx, id, actor.edition)?;
    let resource = resource(&text(row, "kind")).ok_or_else(|| invalid("未知申请类型。"))?;
    if !viewable(&reviewer, resource, row) || !auth::visible(&reviewer, resource, "approve", row) {
        return Err(error(403, "审批人缺少本申请的查看或审批范围。"));
    }
    Ok(reviewer)
}
pub(super) fn queue_approvers(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
) -> Result<Vec<export_doc_storage::RecordApprover>> {
    use export_doc_storage::RecordApprover;
    let mut approvers = vec![RecordApprover {
        user_id: actor.id,
        department: None,
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
                department: (rank == 2).then_some(principal.department),
            });
        }
    }
    Ok(approvers)
}
pub(super) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    let settings = super::settings::load(tx, &actor.company)?;
    let rule = settings["rules"]
        .as_array()
        .and_then(|r| r.iter().find(|r| r["kind"] == row["kind"]))
        .ok_or_else(|| super::super::error::unavailable("缺少申请审批规则。"))?;
    let mode: ApprovalMode = serde_json::from_value(rule["mode"].clone())?;
    let employee = store::get(
        tx,
        "people",
        records::positive(row, "employeeId", "申请人员")?,
    )?;
    let employee_user = employee["account"]["id"].as_i64();
    row["applicantAccountId"] = json!(employee_user);
    let mut ids = vec![];
    if mode == ApprovalMode::Named {
        ids = serde_json::from_value(rule["approverUserIds"].clone())?;
    } else if mode == ApprovalMode::DepartmentChain {
        let departments = store::all(tx, "departments")?;
        let mut code = text(row, "departmentId");
        let mut visited = HashSet::new();
        while !code.is_empty() {
            if visited.len() >= 10 || !visited.insert(code.clone()) {
                return Err(conflict("部门审批链循环或超过十级，请先修正组织目录。"));
            }
            let department = departments
                .iter()
                .find(|d| {
                    d["code"] == code && d["companyCode"] == actor.company && d["isActive"] == true
                })
                .ok_or_else(|| conflict("申请部门或上级部门已停用/缺失。"))?;
            let person = store::get(
                tx,
                "people",
                records::positive(department, "managerEmployeeId", "部门负责人")?,
            )?;
            if person["companyScope"] != actor.company
                || !matches!(person["status"].as_str(), Some("Active" | "Probation"))
            {
                return Err(conflict("部门负责人须为本公司在职人员。"));
            }
            let id = records::positive(&person["account"], "id", "部门负责人关联账号")?;
            if id != row["ownerUserId"].as_i64().unwrap_or(0)
                && Some(id) != employee_user
                && !ids.contains(&id)
            {
                ids.push(id);
            }
            code = text(department, "parentCode");
        }
    }
    let mut snapshot = ApprovalPlan {
        mode,
        policy_version: settings["versionNumber"].as_i64().unwrap_or(0),
        steps: vec![],
    };
    for id in ids {
        if Some(id) == employee_user {
            return Err(invalid("指定审批人不能是申请人员本人。"));
        }
        let reviewer = reviewer(tx, actor, id, row)?;
        snapshot.steps.push(ApprovalStep {
            approver_user_id: id,
            approver_name: reviewer.name,
            status: "Waiting".into(),
            acted_by_user_id: None,
            acted_by_name: String::new(),
            acted_at: String::new(),
            note: String::new(),
            delegation_key: String::new(),
        });
    }
    snapshot
        .validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["currentApproverId"] = json!(
        snapshot
            .steps
            .first()
            .map(|s| s.approver_user_id)
            .unwrap_or(0)
    );
    row["approvalPlan"] = serde_json::to_value(snapshot)?;
    row["lastRemindedAt"] = json!("");
    Ok(())
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
        || !viewable(actor, resource, row)
        || !auth::visible(actor, resource, "approve", row)
    {
        return Ok(None);
    }
    let plan = plan(row)?;
    if plan.as_ref().is_none_or(|p| p.mode == ApprovalMode::Single) {
        return Ok(
            (tx.provider() == "SQLite" || row["ownerUserId"] != actor.id)
                .then_some((actor.id, String::new())),
        );
    }
    let plan = plan.unwrap();
    let Some(index) = plan.current() else {
        return Ok(None);
    };
    if row["ownerUserId"] == actor.id
        || row["applicantAccountId"] == actor.id
        || plan
            .steps
            .iter()
            .any(|s| s.status == "Approved" && s.acted_by_user_id == Some(actor.id))
    {
        return Ok(None);
    }
    let principal = plan.steps[index].approver_user_id;
    if principal == actor.id {
        return Ok(Some((principal, String::new())));
    }
    let delegation = super::settings::principals(tx, actor)?
        .into_iter()
        .find(|(id, _)| *id == principal);
    if delegation.is_some() {
        match reviewer(tx, actor, principal, row) {
            Ok(_) => {}
            Err(e) if e.status == Some(403) => return Ok(None),
            Err(e) => return Err(e),
        }
    }
    Ok(delegation)
}
pub(super) fn review(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    action: &str,
    note: &str,
) -> Result<&'static str> {
    let (_, delegation) = authority(tx, actor, row)?.ok_or_else(|| {
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
                .current()
                .ok_or_else(|| conflict("当前没有待审批步骤。"))?;
            let step = &mut plan.steps[index];
            step.status = next.into();
            step.acted_by_user_id = Some(actor.id);
            step.acted_by_name = actor.name.clone();
            step.acted_at = store::timestamp();
            step.note = note.into();
            step.delegation_key = delegation;
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
