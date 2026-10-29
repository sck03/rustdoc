//! Reporting edges connect appointments, so reciprocal duties in different units do not create a false person-level cycle.
use super::{
    error::{Result, invalid, unavailable},
    personnel_organization::{active_person, assignments},
    records::text,
    store::{self, Connection},
};
use export_doc_domain::personnel_organization::{
    Assignment, PersonnelOrganization, validate_reporting_graph,
};
use serde_json::Value;
use std::collections::HashMap;

pub(super) fn manager_assignment(
    tx: &Connection,
    person: &Value,
    company: &str,
) -> Result<Assignment> {
    let today = super::units::today(tx)?;
    let mut options = assignments(person)?
        .into_iter()
        .filter(|a| a.company_code == company)
        .filter_map(|a| match a.active_on(today) {
            Ok(true) => Some(Ok(a)),
            Ok(false) => None,
            Err(e) => Some(Err(unavailable(e))),
        })
        .collect::<Result<Vec<_>>>()?;
    if person["companyScope"] == company {
        return options
            .into_iter()
            .find(|a| a.department_id == text(person, "departmentId"))
            .ok_or_else(|| invalid("负责人主任职已失效。"));
    }
    if options.len() != 1 {
        return Err(invalid(
            "负责人在目标单位须有唯一的有效任职；存在多个任职时须明确指定负责任职。",
        ));
    }
    Ok(options.remove(0))
}

pub(super) fn reference(
    tx: &Connection,
    company: &str,
    manager: i64,
    appointment: Option<&str>,
) -> Result<String> {
    let person = active_person(tx, company, manager)?;
    if let Some(id) = appointment.filter(|id| !id.is_empty()) {
        let today = super::units::today(tx)?;
        for assignment in assignments(&person)? {
            if assignment.id == id
                && assignment.company_code == company
                && assignment.active_on(today).map_err(unavailable)?
            {
                return Ok(id.into());
            }
        }
        return Err(invalid(
            "所选负责人任职已结束或不属于目标单位，请先交接汇报关系。",
        ));
    }
    Ok(manager_assignment(tx, &person, company)?.id)
}

pub(super) fn normalize(
    tx: &Connection,
    company: &str,
    org: &mut PersonnelOrganization,
) -> Result<()> {
    fn line(
        tx: &Connection,
        company: &str,
        person: Option<i64>,
        reference_id: &mut Option<String>,
        people: &[i64],
        references: &mut Vec<String>,
    ) -> Result<()> {
        *reference_id = person
            .map(|p| reference(tx, company, p, reference_id.as_deref()))
            .transpose()?;
        if !references.is_empty() && references.len() != people.len() {
            return Err(invalid("职能负责人及负责任职须逐项对应。"));
        }
        *references = people
            .iter()
            .enumerate()
            .map(|(i, p)| reference(tx, company, *p, references.get(i).map(String::as_str)))
            .collect::<Result<_>>()?;
        Ok(())
    }
    line(
        tx,
        company,
        org.manager_employee_id,
        &mut org.manager_assignment_id,
        &org.functional_manager_employee_ids,
        &mut org.functional_manager_assignment_ids,
    )?;
    for a in &mut org.additional_assignments {
        line(
            tx,
            &a.company_code,
            a.manager_employee_id,
            &mut a.manager_assignment_id,
            &a.functional_manager_employee_ids,
            &mut a.functional_manager_assignment_ids,
        )?;
    }
    Ok(())
}

pub(super) fn validate(tx: &Connection, proposed: &Value) -> Result<()> {
    let mut people = store::matching(
        tx,
        "people",
        &[],
        &[
            ("id", "id"),
            ("status", "status"),
            ("companyScope", "companyScope"),
            ("departmentId", "departmentId"),
            ("jobTitle", "jobTitle"),
            ("hireDate", "hireDate"),
            ("organization", "organization"),
        ],
    )?;
    people.retain(|p| p["id"] != proposed["id"]);
    people.push(proposed.clone());
    let today = super::units::today(tx)?;
    let mut graph = HashMap::new();
    for person in people.iter().filter(|p| p["status"] != "Departed") {
        for appointment in assignments(person)? {
            if !appointment.active_on(today).map_err(invalid)? {
                continue;
            }
            if appointment.id.is_empty()
                || appointment.manager_employee_id.is_some()
                    != appointment.manager_assignment_id.is_some()
                || appointment.functional_manager_employee_ids.len()
                    != appointment.functional_manager_assignment_ids.len()
            {
                return Err(unavailable("任职或汇报关系引用不完整。"));
            }
            let mut edges = vec![];
            for (manager, reference_id) in appointment
                .manager_employee_id
                .into_iter()
                .zip(appointment.manager_assignment_id.iter())
                .chain(
                    appointment
                        .functional_manager_employee_ids
                        .iter()
                        .copied()
                        .zip(appointment.functional_manager_assignment_ids.iter()),
                )
            {
                let target = if proposed["id"] == manager {
                    proposed.clone()
                } else {
                    active_person(tx, &appointment.company_code, manager)?
                };
                let mut active = false;
                for a in assignments(&target)? {
                    if a.id == *reference_id
                        && a.company_code == appointment.company_code
                        && a.active_on(today).map_err(unavailable)?
                    {
                        active = true;
                        break;
                    }
                }
                if !active {
                    return Err(invalid("任职仍被汇报关系引用，请先移交后再结束或调整。"));
                }
                edges.push(reference_id.clone());
            }
            graph.insert(appointment.id, edges);
        }
    }
    validate_reporting_graph(&graph).map_err(invalid)
}
