//! Select once by stable appointment, then reuse it in routing and archives.
use super::*;
use export_doc_domain::personnel_organization::Assignment;

pub(super) fn appointment(
    tx: &Connection,
    actor: &Actor,
    person: &Value,
    row: &Value,
) -> Result<Assignment> {
    let mut selected = text(row, "applicantAppointmentId");
    let requested = text(row, "appointmentId");
    if !selected.is_empty() && !requested.is_empty() && requested != selected {
        return Err(conflict("申请任职不能更换，请取消后使用新的任职身份发起。"));
    }
    if selected.is_empty() {
        if person["account"]["id"] == actor.id {
            if let Some(id) = super::super::units::selected_appointment_id(tx, actor)? {
                if !requested.is_empty() && requested != id {
                    return Err(error(403, "申请任职须与当前业务身份一致。"));
                }
                selected = id;
            }
        }
        if selected.is_empty() {
            selected = requested;
        }
    }
    let today = super::super::units::today(tx)?;
    let date = if row["kind"] == "personnel"
        && matches!(
            row["personnelChange"]["action"].as_str(),
            Some("Hire" | "Rehire")
        ) {
        today.max(super::personnel_changes::effective_date(row)?)
    } else {
        today
    };
    let department = text(row, "departmentId");
    let mut candidates = vec![];
    let assignments = super::super::personnel_organization::assignments(person)?;
    if !assignments.iter().any(|a| a.company_code == actor.company) {
        return Err(error(403, "只能为当前经营单位的员工办理申请。"));
    }
    for assignment in assignments {
        if assignment.company_code == actor.company
            && (department.is_empty() || assignment.department_id == department)
            && assignment
                .active_on(date)
                .map_err(super::super::error::unavailable)?
        {
            candidates.push(assignment);
        }
    }
    let selected = if selected.is_empty() {
        let primary =
            super::super::personnel_organization::organization(person)?.primary_assignment_id;
        candidates
            .iter()
            .position(|a| a.id == primary)
            .or_else(|| (candidates.len() == 1).then_some(0))
    } else {
        candidates.iter().position(|a| a.id == selected)
    };
    let appointment = selected
        .map(|index| candidates.swap_remove(index))
        .ok_or_else(|| {
            conflict("所选任职已失效或不属于申请单位；同部门多岗位须选择明确的任职身份。")
        })?;
    super::super::organization::validate_assignment(
        tx,
        &actor.company,
        &appointment.department_id,
    )?;
    Ok(appointment)
}
