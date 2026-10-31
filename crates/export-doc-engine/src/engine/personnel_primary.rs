//! Prepare a primary appointment proposal; OA owns approval and atomic application.
use super::{
    error::{Result, invalid},
    personnel_organization as organization,
    store::{Actor, Connection},
};
use serde_json::{Value, json};

pub(super) fn prepare(
    tx: &Connection,
    actor: &Actor,
    person: &mut Value,
    selected: &str,
    date: chrono::NaiveDate,
) -> Result<()> {
    if !matches!(person["status"].as_str(), Some("Active" | "Probation")) {
        return Err(invalid("只有在职人员可以调整主任职。"));
    }
    let mut org = organization::organization(person)?;
    let next = org
        .additional_assignments
        .iter()
        .find(|a| a.id == selected)
        .cloned()
        .ok_or_else(|| invalid("请选择此人员已有的兼任职。"))?;
    if !next.active_on(date).map_err(invalid)? || !next.ends_on.is_empty() {
        return Err(invalid(
            "新主任职须已生效且没有结束日期，请先维护任职有效期。",
        ));
    }
    super::organization::validate_assignment(tx, &next.company_code, &next.department_id)?;
    let previous = organization::assignments(person)?.remove(0);
    org.additional_assignments.retain(|a| a.id != selected);
    org.additional_assignments.push(previous);
    org.manager_employee_id = next.manager_employee_id;
    org.functional_manager_employee_ids = next.functional_manager_employee_ids;
    org.manager_assignment_id = next.manager_assignment_id;
    org.functional_manager_assignment_ids = next.functional_manager_assignment_ids;
    person["companyScope"] = json!(next.company_code);
    person["departmentId"] = json!(next.department_id);
    person["jobTitle"] = json!(next.job_title);
    let submitted = serde_json::to_value(org)?;
    organization::validate_with_primary(
        tx,
        actor,
        person,
        &submitted,
        "transition",
        Some(selected),
    )?;
    Ok(())
}
