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
use std::collections::{HashMap, HashSet};

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
    if let Some(id) = appointment.filter(|id| !id.is_empty()) {
        let person = store::get(tx, "people", manager)?;
        for assignment in assignments(&person)? {
            if assignment.id == id && assignment.company_code == company {
                return Ok(id.into());
            }
        }
        return Err(invalid(
            "所选负责人任职已结束或不属于目标单位，请先交接汇报关系。",
        ));
    }
    let person = active_person(tx, company, manager)?;
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
    let people = store::matching(
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
    let today = super::units::today(tx)?;
    let previous = proposed["id"]
        .as_i64()
        .filter(|id| *id > 0)
        .map(|id| store::get(tx, "people", id))
        .transpose()?
        .unwrap_or(Value::Null);
    validate_graph(people, proposed, &previous, today)
}

fn validate_graph(
    mut people: Vec<Value>,
    proposed: &Value,
    previous: &Value,
    today: chrono::NaiveDate,
) -> Result<()> {
    people.retain(|p| p["id"] != proposed["id"]);
    people.push(proposed.clone());
    let old = if previous["status"] == "Departed" {
        vec![]
    } else {
        assignments(&previous)?
    };
    let next = if proposed["status"] == "Departed" {
        vec![]
    } else {
        assignments(proposed)?
    };
    let changed: HashSet<_> = old
        .iter()
        .chain(&next)
        .filter(|a| !old.contains(a) || !next.contains(a))
        .map(|a| a.id.as_str())
        .collect();
    let mut available = HashMap::new();
    for person in people
        .iter()
        .filter(|p| matches!(p["status"].as_str(), Some("Active" | "Probation")))
    {
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
            let owner = if person["id"].is_null() && proposed["id"].is_null() {
                0
            } else {
                person["id"]
                    .as_i64()
                    .ok_or_else(|| unavailable("任职人员编号损坏。"))?
            };
            if available
                .insert(appointment.id.clone(), (owner, appointment))
                .is_some()
            {
                return Err(unavailable("任职编号重复。"));
            }
        }
    }
    let mut graph = HashMap::new();
    for (id, (_, appointment)) in &available {
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
            let active = available.get(reference_id).is_some_and(|(owner, a)| {
                *owner == manager && a.company_code == appointment.company_code
            });
            if !active {
                if changed.contains(id.as_str()) || changed.contains(reference_id.as_str()) {
                    return Err(invalid(
                        "变更的任职仍有失效汇报关系，请先移交后再结束或调整。",
                    ));
                }
                // Optional dates can expire without a write. Unchanged stale
                // edges do not block unrelated appointments; routing still
                // requires a currently effective manager when used.
                continue;
            }
            edges.push(reference_id.clone());
        }
        graph.insert(id.clone(), edges);
    }
    validate_reporting_graph(&graph).map_err(invalid)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test]
    fn optional_expiry_does_not_block_unrelated_units_but_changes_and_cycles_remain_strict() {
        let person = |id, unit, department| json!({"id":id,"status":"Active","companyScope":unit,"departmentId":department,"jobTitle":"普通员工","hireDate":"2026-01-01","organization":{"primaryAssignmentId":format!("primary-{id}"),"managerEmployeeId":null,"functionalManagerEmployeeIds":[],"additionalAssignments":[]}});
        let mut manager = person(1, "A", "A1");
        manager["organization"]["additionalAssignments"] = json!([{"id":"temporary-b","companyCode":"B","departmentId":"B1","jobTitle":"临时兼任","startsOn":"2026-01-01","endsOn":"2026-10-29","managerEmployeeId":null,"functionalManagerEmployeeIds":[]}]);
        let mut reporter = person(2, "B", "B2");
        reporter["organization"]["managerEmployeeId"] = json!(1);
        reporter["organization"]["managerAssignmentId"] = json!("temporary-b");
        let unrelated = person(3, "C", "C1");
        let rows = vec![manager.clone(), reporter.clone(), unrelated.clone()];
        let mut changed = unrelated.clone();
        changed["jobTitle"] = json!("其他单位的岗位维护");
        let today = "2026-10-30".parse().unwrap();
        validate_graph(rows.clone(), &changed, &unrelated, today).unwrap();
        let mut changed_reporter = reporter.clone();
        changed_reporter["jobTitle"] = json!("调整汇报岗位");
        assert_eq!(
            validate_graph(rows.clone(), &changed_reporter, &reporter, today)
                .unwrap_err()
                .status,
            Some(400)
        );
        validate_graph(
            rows.clone(),
            &changed_reporter,
            &reporter,
            "2026-10-29".parse().unwrap(),
        )
        .unwrap();
        let mut previous_manager = manager.clone();
        previous_manager["organization"]["additionalAssignments"][0]["endsOn"] = json!("");
        assert_eq!(
            validate_graph(rows.clone(), &manager, &previous_manager, today)
                .unwrap_err()
                .status,
            Some(400)
        );
        let mut fixed = reporter.clone();
        fixed["organization"]["managerEmployeeId"] = Value::Null;
        fixed["organization"]["managerAssignmentId"] = Value::Null;
        validate_graph(rows, &fixed, &reporter, today).unwrap();
        let mut first = person(4, "A", "A2");
        let second = person(5, "A", "A3");
        first["organization"]["managerEmployeeId"] = json!(5);
        first["organization"]["managerAssignmentId"] = json!("primary-5");
        let mut looped = second.clone();
        looped["organization"]["managerEmployeeId"] = json!(4);
        looped["organization"]["managerAssignmentId"] = json!("primary-4");
        assert_eq!(
            validate_graph(vec![first, second.clone()], &looped, &second, today)
                .unwrap_err()
                .status,
            Some(400)
        );
    }
}
