//! Recheck the current duty behind a frozen actor without rewriting history.
use super::*;

pub(in crate::engine::oa) fn origin_available(
    tx: &Connection,
    row: &Value,
    person: i64,
    origin: &AssignmentOrigin,
) -> Result<bool> {
    let Some(source) = origin.source else {
        return Ok(true);
    };
    if !origin.appointment_id.is_empty() {
        let employee = store::get(tx, "people", person)?;
        match super::super::super::personnel_organization::appointment_by_id(
            tx,
            &employee,
            &origin.unit_code,
            &origin.appointment_id,
        ) {
            Ok(_) => {}
            Err(cause) if matches!(cause.status, Some(400 | 403 | 404 | 409)) => return Ok(false),
            Err(cause) => return Err(cause),
        }
    }
    if matches!(
        source,
        ApproverSource::Position | ApproverSource::Responsibility | ApproverSource::ProjectLeader
    ) {
        let unit = super::super::super::units::company(tx, &origin.unit_code)?;
        let unit_id = records::positive(&unit, "id", "来源单位")?;
        let id = origin
            .source_id
            .as_ref()
            .ok_or_else(|| super::super::super::error::unavailable("冻结职责来源编号缺失。"))?
            .number();
        let today = super::super::super::units::today(tx)?.to_string();
        let values = if source == ApproverSource::Responsibility {
            tx.responsibility_appointments(
                unit_id,
                id,
                row["projectId"].as_str().and_then(|id| id.parse().ok()),
                &text(row, "kind"),
                &today,
            )?
        } else {
            tx.placement_appointments(
                unit_id,
                if source == ApproverSource::Position {
                    "Position"
                } else {
                    "ProjectLeader"
                },
                id,
                &today,
            )?
        };
        return Ok(values
            .iter()
            .any(|v| v["personId"] == person && v["appointmentId"] == origin.appointment_id));
    }
    use ApproverSource::*;
    let authorized_pair =
        |row: &Value, main_person: &str, main_appointment: &str, leaders: bool| -> Result<bool> {
            if row[main_person] == person && row[main_appointment] == origin.appointment_id {
                return Ok(true);
            }
            Ok(leaders
                && super::super::super::organization::leaders(row)?
                    .iter()
                    .any(|l| {
                        l.employee_id == person
                            && l.appointment_id == origin.appointment_id
                            && l.workflow_kinds.contains(&text(row, "kind"))
                    }))
        };
    match source {
        DepartmentManager | DepartmentLeaders => {
            let Some(id) = origin.department_id.as_ref() else {
                return Err(super::super::super::error::unavailable(
                    "冻结部门职责编号缺失。",
                ));
            };
            let Some(department) = tx.get("departments", id.number())? else {
                return Ok(false);
            };
            return Ok(department["isActive"] == true
                && department["companyCode"] == origin.unit_code
                && authorized_pair(
                    &department,
                    "managerEmployeeId",
                    "managerAppointmentId",
                    source == DepartmentLeaders,
                )?);
        }
        CompanyExecutive | CompanyLeaders => {
            let unit = super::super::super::units::company(tx, &origin.unit_code)?;
            return Ok(unit["isActive"] == true
                && authorized_pair(
                    &unit,
                    "executiveEmployeeId",
                    "executiveAppointmentId",
                    source == CompanyLeaders,
                )?);
        }
        LineManager | FunctionalManagers => {
            let Some(id) = origin.subject_person_id else {
                return Ok(true);
            };
            let Some(subject) = tx.get("people", id)? else {
                return Ok(false);
            };
            let appointment = match super::super::super::personnel_organization::appointment_by_id(
                tx,
                &subject,
                &origin.unit_code,
                &origin.subject_appointment_id,
            ) {
                Ok(a) => a,
                Err(cause) if matches!(cause.status, Some(400 | 403 | 404 | 409)) => {
                    return Ok(false);
                }
                Err(cause) => return Err(cause),
            };
            return Ok(if source == LineManager {
                appointment.manager_employee_id == Some(person)
                    && appointment.manager_assignment_id.as_deref() == Some(&origin.appointment_id)
            } else {
                appointment
                    .functional_manager_employee_ids
                    .iter()
                    .zip(&appointment.functional_manager_assignment_ids)
                    .any(|(id, key)| *id == person && key == &origin.appointment_id)
            });
        }
        Handlers => {
            let key = text(row, "handlingKey");
            if !key.is_empty() {
                let employee = store::get(tx, "people", person)?;
                return Ok(handling::services(tx, &text(row, "companyScope"))?
                    .iter()
                    .any(|s| {
                        s["key"] == key
                            && s["handlerUserIds"]
                                .as_array()
                                .is_some_and(|ids| ids.contains(&employee["account"]["id"]))
                    }));
            }
        }
        _ => {}
    }
    Ok(true)
}
