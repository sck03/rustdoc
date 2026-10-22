//! Resolve workflow sources once, then persist an auditable approval snapshot.
use super::*;
use export_doc_domain::{
    approval::{ApprovalMode, ApprovalPlan, ApprovalStep},
    approval_policy::{ApprovalRule, ApprovalStage, ApproverSource},
};
use std::collections::HashSet;

fn account(tx: &Connection, company: &str, employee: i64) -> Result<i64> {
    let person = super::super::personnel_organization::active_person(tx, company, employee)?;
    records::positive(&person["account"], "id", "负责人关联账号")
}
fn department_manager(tx: &Connection, company: &str, department: &str) -> Result<(i64, String)> {
    let value = tx
        .find_identity("departments", &store::normalize(department))?
        .filter(|value| value["companyCode"] == company && value["isActive"] == true)
        .ok_or_else(|| conflict("申请部门或上级部门缺失、已停用。"))?;
    Ok((
        account(
            tx,
            company,
            records::positive(&value, "managerEmployeeId", "部门负责人")?,
        )?,
        text(&value, "parentCode"),
    ))
}
fn resolve(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    employee: &Value,
    stage: &ApprovalStage,
) -> Result<Vec<i64>> {
    let employee_ids = match stage.source {
        ApproverSource::Named => return Ok(stage.approver_user_ids.clone()),
        ApproverSource::DepartmentManager => {
            return Ok(vec![
                department_manager(tx, &actor.company, &text(row, "departmentId"))?.0,
            ]);
        }
        ApproverSource::LineManager | ApproverSource::FunctionalManagers => {
            super::super::personnel_organization::appointment_managers(
                employee,
                &text(row, "departmentId"),
                stage.source == ApproverSource::FunctionalManagers,
            )?
        }
        ApproverSource::CompanyExecutive => {
            let company = tx
                .find_identity("companies", &store::normalize(&actor.company))?
                .ok_or_else(|| conflict("申请公司不存在。"))?;
            vec![records::positive(
                &company,
                "executiveEmployeeId",
                "公司执行负责人",
            )?]
        }
    };
    if employee_ids.is_empty() {
        return Err(conflict(
            "审批阶段缺少已配置的汇报负责人，请先完善人员任职关系。",
        ));
    }
    employee_ids
        .into_iter()
        .map(|id| account(tx, &actor.company, id))
        .collect()
}
pub(super) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    let settings = settings::load(tx, &actor.company)?;
    let rule = settings["rules"]
        .as_array()
        .and_then(|rules| rules.iter().find(|rule| rule["kind"] == row["kind"]))
        .ok_or_else(|| super::super::error::unavailable("缺少申请审批规则。"))?;
    let rule: ApprovalRule = serde_json::from_value(rule.clone())
        .map_err(|_| super::super::error::unavailable("审批规则损坏。"))?;
    rule.validate().map_err(super::super::error::unavailable)?;
    let hiring = row["kind"] == "personnel" && row["personnelChange"]["action"] == "Hire";
    let employee = if hiring {
        row["personnelChange"]["registration"].clone()
    } else {
        store::get(
            tx,
            "people",
            records::positive(row, "employeeId", "申请人员")?,
        )?
    };
    let employee_user = if hiring {
        None
    } else {
        employee["account"]["id"].as_i64()
    };
    row["applicantAccountId"] = json!(employee_user);
    let branch = rule
        .matching_branch(&text(row, "currency"), &text(row, "totalAmount"))
        .map_err(super::super::error::unavailable)?;
    let mut groups = vec![];
    let mode = if branch.is_some() {
        ApprovalMode::Configured
    } else {
        rule.mode.clone()
    };
    if mode == ApprovalMode::Configured {
        let stages = branch.map(|branch| &branch.stages).unwrap_or(&rule.stages);
        for stage in stages {
            groups.push(resolve(tx, actor, row, &employee, stage)?);
        }
    } else if mode == ApprovalMode::Named {
        groups = rule.approver_user_ids.iter().map(|id| vec![*id]).collect();
    } else if mode == ApprovalMode::DepartmentChain {
        let mut department = text(row, "departmentId");
        let mut visited = HashSet::new();
        let mut ids = HashSet::new();
        while !department.is_empty() {
            if visited.len() >= 10 || !visited.insert(department.clone()) {
                return Err(conflict("部门审批链循环或超过十级。"));
            }
            let (id, parent) = department_manager(tx, &actor.company, &department)?;
            if row["ownerUserId"] != id && Some(id) != employee_user && ids.insert(id) {
                groups.push(vec![id]);
            }
            department = parent;
        }
    }
    let mut plan = ApprovalPlan {
        mode,
        policy_version: settings["versionNumber"].as_i64().unwrap_or(0),
        steps: vec![],
        matched_branch: branch.map(|branch| branch.key.clone()).unwrap_or_default(),
    };
    for (stage, ids) in groups.into_iter().enumerate() {
        if ids.is_empty() {
            return Err(conflict("审批阶段没有有效负责人。"));
        }
        for id in ids {
            if Some(id) == employee_user {
                return Err(invalid("审批人不能是申请人员本人。"));
            }
            let reviewer = approval::reviewer(tx, actor, id, row)?;
            plan.steps.push(ApprovalStep {
                stage: Some(stage),
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
    }
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["currentApproverId"] = json!(
        plan.steps
            .first()
            .map(|step| step.approver_user_id)
            .unwrap_or(0)
    );
    row["approvalPlan"] = serde_json::to_value(plan)?;
    row["lastRemindedAt"] = json!("");
    Ok(())
}
