//! Promote an existing appointment; all identity and duty checks use one transaction.
use super::{
    auth,
    error::{Result, conflict, error, invalid},
    personnel, personnel_organization as organization,
    records::{self, text},
    store::{self, Actor, Store},
};
use serde_json::{Value, json};

pub(super) fn promote(
    store: &Store,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    store.transaction_as(actor, |tx, actor| {
        if !actor.admin {
            return Err(error(403, "跨单位主任职调动由平台管理员办理。"));
        }
        auth::authorize(actor, "office.people", "transition")?;
        let id = records::id(parameters)?;
        let mut person = personnel::person(tx, actor, id, "transition")?;
        store::check_version(&person, store::expected(body))?;
        records::required(body, "note", "主职调动说明", 500)?;
        if !matches!(person["status"].as_str(), Some("Active" | "Probation")) {
            return Err(invalid("只有在职人员可以调整主任职。"));
        }
        let selected = text(body, "appointmentId");
        let mut org = organization::organization(&person)?;
        let next = org
            .additional_assignments
            .iter()
            .find(|a| a.id == selected)
            .cloned()
            .ok_or_else(|| invalid("请选择此人员已有的兼任职。"))?;
        let today = super::units::today(tx)?;
        if !next.active_on(today).map_err(invalid)? || !next.ends_on.is_empty() {
            return Err(invalid(
                "新主任职须已生效且没有结束日期，请先维护任职有效期。",
            ));
        }
        organization::appointment_by_id(tx, &person, &next.company_code, &selected)?;
        let clearance = super::office_queries::clearance(tx, &person)?;
        if clearance["isClear"] != true {
            return Err(conflict(
                "此人员仍有未结申请、审批或办理事项，请先完成交接。",
            ));
        }
        let previous = organization::assignments(&person)?.remove(0);
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
            &mut person,
            &submitted,
            "transition",
            Some(&selected),
        )?;
        for key in ["companyScope", "departmentId", "jobTitle"] {
            person["employee"][key] = person[key].clone();
        }
        person["lastEffectiveDate"] = json!(today.to_string());
        personnel::save(
            tx,
            actor,
            id,
            person,
            "PromotePersonnelAppointment",
            &text(body, "note"),
        )
    })
}
