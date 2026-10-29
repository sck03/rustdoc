//! Resolve the published graph and freeze explicit task assignments on submission.
use super::*;
use export_doc_domain::{
    approval::{ApprovalNode, ApprovalPlan, ApprovalStep},
    approval_policy::{ApprovalStage, ApproverSource, ConditionContext},
};
use rust_decimal::Decimal;
use std::collections::HashSet;
use std::str::FromStr;

pub(super) fn resolve(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    employee: &Value,
    stage: &ApprovalStage,
) -> Result<Vec<i64>> {
    let mut company_code = actor.company.clone();
    let appointments = match stage.source {
        ApproverSource::Named => return Ok(stage.approver_user_ids.clone()),
        ApproverSource::DepartmentManager | ApproverSource::DepartmentLeaders => {
            let department = tx
                .find_identity(
                    "departments",
                    &store::department_identity(&actor.company, &text(row, "departmentId"))?,
                )?
                .filter(|v| v["companyCode"] == actor.company && v["isActive"] == true)
                .ok_or_else(|| conflict("申请部门缺失或已停用。"))?;
            let mut ids = vec![(
                records::positive(&department, "managerEmployeeId", "部门负责人")?,
                text(&department, "managerAppointmentId"),
            )];
            if stage.source == ApproverSource::DepartmentLeaders {
                ids.extend(
                    super::super::organization::leaders(&department)?
                        .into_iter()
                        .filter(|l| l.workflow_kinds.contains(&text(row, "kind")))
                        .map(|l| (l.employee_id, l.appointment_id)),
                );
            }
            ids
        }
        ApproverSource::LineManager | ApproverSource::FunctionalManagers => {
            let appointment = super::super::personnel_organization::appointment_in(
                tx,
                employee,
                &actor.company,
                &text(row, "departmentId"),
            )?;
            if stage.source == ApproverSource::FunctionalManagers {
                appointment
                    .functional_manager_employee_ids
                    .into_iter()
                    .zip(appointment.functional_manager_assignment_ids)
                    .collect()
            } else {
                appointment
                    .manager_employee_id
                    .into_iter()
                    .zip(appointment.manager_assignment_id)
                    .collect()
            }
        }
        ApproverSource::CompanyExecutive | ApproverSource::CompanyLeaders => {
            let company = super::organization_routing::unit(tx, actor, stage)?;
            company_code = text(&company, "code");
            let mut ids = vec![(
                records::positive(&company, "executiveEmployeeId", "公司领导审批负责人")?,
                text(&company, "executiveAppointmentId"),
            )];
            if stage.source == ApproverSource::CompanyLeaders {
                ids.extend(
                    super::super::organization::leaders(&company)?
                        .into_iter()
                        .filter(|l| l.workflow_kinds.contains(&text(row, "kind")))
                        .map(|l| (l.employee_id, l.appointment_id)),
                );
            }
            ids
        }
    };
    appointments
        .into_iter()
        .map(|(id, appointment)| {
            super::organization_routing::account(tx, &company_code, id, &appointment)
        })
        .collect()
}
pub(super) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    let kind = text(row, "kind");
    let variant = if kind == "personnel" {
        text(&row["personnelChange"], "action")
    } else {
        String::new()
    };
    let (rule, version) = super::definitions::published(tx, &actor.company, &kind, &variant)?;
    let hiring = kind == "personnel" && variant == "Hire";
    let employee = if kind == "personnel" && matches!(variant.as_str(), "Hire" | "Rehire") {
        super::personnel_changes::prepare(tx, actor, row, super::super::units::today(tx)?, false)?
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
    let duration = match kind.as_str() {
        "leave" => {
            let (a, b) = export_doc_domain::oa::leave_span(&row["leave"]).map_err(invalid)?;
            Some(Decimal::from(b - a) / Decimal::from(2))
        }
        "overtime" => {
            let (a, b) = export_doc_domain::oa::overtime_span(&row["overtime"]).map_err(invalid)?;
            Some(Decimal::from((b - a).num_minutes()) / Decimal::from(60))
        }
        "travel" => {
            let a =
                export_doc_domain::oa::date(&text(&row["travel"], "startsOn")).map_err(invalid)?;
            let b =
                export_doc_domain::oa::date(&text(&row["travel"], "endsOn")).map_err(invalid)?;
            Some(Decimal::from((b - a).num_days() + 1))
        }
        _ => None,
    };
    let context = ConditionContext {
        amount: if text(row, "totalAmount").is_empty() {
            None
        } else {
            Some(
                Decimal::from_str(&text(row, "totalAmount"))
                    .map_err(|_| invalid("申请金额无效。"))?,
            )
        },
        currency: text(row, "currency"),
        duration,
        department: text(row, "departmentId"),
        category: if kind == "leave" {
            text(&row["leave"], "category")
        } else {
            text(row, "category")
        },
        personnel_action: variant,
    };
    build_plan(tx, actor, row, &employee, &rule, version, &context)
}

pub(super) fn build_plan(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    employee: &Value,
    rule: &export_doc_domain::approval_policy::ApprovalRule,
    version: i64,
    context: &ConditionContext,
) -> Result<()> {
    let employee_user = row["applicantAccountId"].as_i64();
    let mut plan = ApprovalPlan {
        definition_key: rule.key.clone(),
        policy_version: version,
        nodes: vec![],
        steps: vec![],
        notify_user_ids: rule.notify_user_ids.clone(),
    };
    let mut previous = HashSet::new();
    let mut notes = vec![];
    for (index, node) in rule.stages.iter().enumerate() {
        let included = rule.includes(index, &context).map_err(invalid)?;
        let ids = if included {
            let (ids, routed) = rule
                .route_group(
                    index,
                    resolve(tx, actor, row, &employee, node)?,
                    row["ownerUserId"].as_i64().unwrap_or(0),
                    employee_user,
                    &mut previous,
                )
                .map_err(invalid)?;
            notes.extend(routed);
            ids
        } else {
            notes.push(format!("{}：条件不满足，跳过节点。", node.name));
            vec![]
        };
        plan.nodes.push(ApprovalNode {
            key: node.key.clone(),
            name: node.name.clone(),
            review_points: node.review_points.clone(),
            depends_on: node.depends_on.clone(),
            decision: node.decision,
            due_hours: node.due_hours,
            independent_review: node.repeated_approver
                == export_doc_domain::approval_policy::RepeatedApprover::Reject,
            skipped: ids.is_empty(),
            started_at: String::new(),
            due_at: String::new(),
        });
        for id in ids {
            let reviewer = approval::reviewer(tx, actor, id, row)?;
            plan.steps.push(ApprovalStep {
                task_key: format!("{}:{id}", node.key),
                stage: index,
                decision: node.decision,
                approver_user_id: id,
                approver_name: reviewer.name,
                approver_context: reviewer.context,
                status: "Waiting".into(),
                acted_by_user_id: None,
                acted_by_name: String::new(),
                acted_by_context: String::new(),
                acted_at: String::new(),
                note: String::new(),
                delegation_key: String::new(),
            });
        }
    }
    plan.activate(chrono::Utc::now());
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["approvalStageLabels"] = json!(plan.nodes.iter().map(|n| &n.name).collect::<Vec<_>>());
    row["approvalPlan"] = serde_json::to_value(plan)?;
    row["approvalRoutingNotes"] = json!(notes);
    row["lastRemindedAt"] = json!("");
    Ok(())
}
