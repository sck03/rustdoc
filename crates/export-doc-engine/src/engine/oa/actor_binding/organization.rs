//! Organization reporting and leadership provide stable appointment sources.
use super::*;

pub(super) fn appointments(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    employee: &Value,
    stage: &ApprovalStage,
) -> Result<(String, Vec<(i64, String)>)> {
    use ApproverSource::*;
    match stage.source {
        DepartmentManager | DepartmentLeaders => {
            let route = text(row, "routingDepartment");
            let department = if route.is_empty() {
                text(row, "departmentId")
            } else {
                route
            };
            let department = tx
                .find_identity(
                    "departments",
                    &store::department_identity(&actor.company, &department)?,
                )?
                .filter(|d| d["isActive"] == true)
                .ok_or_else(|| conflict("申请部门已失效。"))?;
            let mut values = vec![(
                records::positive(&department, "managerEmployeeId", "部门负责人")?,
                text(&department, "managerAppointmentId"),
            )];
            if stage.source == DepartmentLeaders {
                values.extend(
                    super::super::super::organization::leaders(&department)?
                        .into_iter()
                        .filter(|l| l.workflow_kinds.contains(&text(row, "kind")))
                        .map(|l| (l.employee_id, l.appointment_id)),
                );
            }
            Ok((actor.company.clone(), values))
        }
        LineManager | FunctionalManagers => {
            let appointment =
                super::super::request_identity::appointment(tx, actor, employee, row)?;
            let values = if stage.source == FunctionalManagers {
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
            };
            Ok((actor.company.clone(), values))
        }
        CompanyExecutive | CompanyLeaders => {
            let unit = super::super::organization_routing::unit(tx, actor, stage)?;
            let mut values = vec![(
                records::positive(&unit, "executiveEmployeeId", "公司负责人")?,
                text(&unit, "executiveAppointmentId"),
            )];
            if stage.source == CompanyLeaders {
                values.extend(
                    super::super::super::organization::leaders(&unit)?
                        .into_iter()
                        .filter(|l| l.workflow_kinds.contains(&text(row, "kind")))
                        .map(|l| (l.employee_id, l.appointment_id)),
                );
            }
            Ok((text(&unit, "code"), values))
        }
        _ => Err(invalid("组织职责来源无效。")),
    }
}
