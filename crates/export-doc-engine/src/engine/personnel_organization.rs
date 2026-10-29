//! Personnel-owned appointments; account scope and workflow routing derive from these records.
use super::{
    error::{Result, invalid, unavailable},
    records::text,
    store::{self, Actor},
};
use crate::engine::store::Connection;
use export_doc_domain::personnel_organization::PersonnelOrganization;
use export_doc_storage::{DataScope, GenericQuery};
use serde_json::{Value, json};
use std::collections::HashMap;

pub fn reporting_options(
    store: &store::Store,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    if super::auth::authorize(actor, "office.people", "create").is_err() {
        super::auth::authorize(actor, "office.people", "transition")?;
    }
    if !actor.admin
        && query
            .iter()
            .any(|(key, value)| *key == "companyCode" && *value != actor.company)
    {
        return Err(super::error::error(
            403,
            "汇报负责人只能从本公司在职人员选择。",
        ));
    }
    super::organization::managers(store, query)
}

pub fn organization(person: &Value) -> Result<PersonnelOrganization> {
    match person.get("organization").filter(|value| !value.is_null()) {
        Some(value) => serde_json::from_value(value.clone())
            .map_err(|_| unavailable("人员任职与汇报关系损坏。")),
        None => Ok(PersonnelOrganization::default()),
    }
}
pub fn departments(person: &Value) -> Result<Vec<String>> {
    Ok(assignments(person)?
        .into_iter()
        .filter(|a| a.company_code == text(person, "companyScope"))
        .map(|a| a.department_id)
        .collect())
}
pub fn assignments(
    person: &Value,
) -> Result<Vec<export_doc_domain::personnel_organization::Assignment>> {
    let org = organization(person)?;
    let mut result = vec![export_doc_domain::personnel_organization::Assignment {
        id: org.primary_assignment_id,
        manager_assignment_id: org.manager_assignment_id,
        functional_manager_assignment_ids: org.functional_manager_assignment_ids,
        company_code: text(person, "companyScope"),
        department_id: text(person, "departmentId"),
        job_title: text(person, "jobTitle"),
        manager_employee_id: org.manager_employee_id,
        functional_manager_employee_ids: org.functional_manager_employee_ids,
        starts_on: if org.primary_starts_on.is_empty() {
            text(person, "hireDate")
        } else {
            org.primary_starts_on
        },
        ends_on: String::new(),
    }];
    result.extend(org.additional_assignments);
    Ok(result)
}
pub fn departments_in(tx: &Connection, person: &Value, company: &str) -> Result<Vec<String>> {
    let today = super::units::today(tx)?;
    if !matches!(person["status"].as_str(), Some("Active" | "Probation")) {
        return Ok(vec![]);
    }
    assignments(person)?
        .into_iter()
        .filter(|a| a.company_code == company)
        .filter_map(|a| match a.active_on(today) {
            Ok(true) => Some(Ok(a.department_id)),
            Ok(false) => None,
            Err(e) => Some(Err(unavailable(e))),
        })
        .collect()
}
pub fn appointment_in(
    tx: &Connection,
    person: &Value,
    company: &str,
    department: &str,
) -> Result<export_doc_domain::personnel_organization::Assignment> {
    if !departments_in(tx, person, company)?
        .iter()
        .any(|d| d == department)
    {
        return Err(invalid("所选单位和部门不在人员的有效任职范围内。"));
    }
    assignments(person)?
        .into_iter()
        .find(|a| a.company_code == company && a.department_id == department)
        .ok_or_else(|| invalid("任职不存在。"))
}
pub fn appointment_by_id(
    tx: &Connection,
    person: &Value,
    company: &str,
    id: &str,
) -> Result<export_doc_domain::personnel_organization::Assignment> {
    if !matches!(person["status"].as_str(), Some("Active" | "Probation")) {
        return Err(invalid("任职人员已不在职。"));
    }
    let today = super::units::today(tx)?;
    for assignment in assignments(person)? {
        if assignment.id == id
            && assignment.company_code == company
            && assignment.active_on(today).map_err(unavailable)?
        {
            super::organization::validate_assignment(tx, company, &assignment.department_id)?;
            return Ok(assignment);
        }
    }
    Err(invalid("所选任职已失效、已结束或不属于目标单位。"))
}
pub fn active_person(tx: &Connection, company: &str, id: i64) -> Result<Value> {
    if id <= 0 {
        return Err(invalid("负责人编号必须大于零。"));
    }
    let person = store::get(tx, "people", id)?;
    if departments_in(tx, &person, company)?.is_empty()
        || !matches!(person["status"].as_str(), Some("Active" | "Probation"))
    {
        return Err(invalid("汇报负责人须为本公司的在职人员。"));
    }
    Ok(person)
}
pub fn directory(tx: &Connection, company: &str) -> Result<Vec<Value>> {
    let mut result = vec![];
    loop {
        crate::operation::check()?;
        let (total, rows) = tx.query_generic(&GenericQuery {
            kind: "people",
            scope: DataScope {
                company_wide: true,
                company,
                personnel_memberships: true,
                personnel_date: Some(&super::units::today(tx)?.to_string()),
                ..Default::default()
            },
            filters: &[],
            keyword: "",
            search_root: None,
            fields: &[
                ("id", "id"),
                ("status", "status"),
                ("departmentId", "departmentId"),
                ("organization", "organization"),
                ("companyScope", "companyScope"),
                ("hireDate", "hireDate"),
                ("jobTitle", "jobTitle"),
            ],
            offset: result.len() as i64,
            limit: 200,
        })?;
        result.extend(rows);
        if result.len() as i64 >= total {
            break;
        }
    }
    Ok(result)
}
pub fn validate(
    tx: &Connection,
    actor: &Actor,
    person: &mut Value,
    submitted: &Value,
    action: &str,
) -> Result<()> {
    validate_with_primary(tx, actor, person, submitted, action, None)
}

pub(super) fn validate_with_primary(
    tx: &Connection,
    actor: &Actor,
    person: &mut Value,
    submitted: &Value,
    action: &str,
    primary: Option<&str>,
) -> Result<()> {
    let mut organization: PersonnelOrganization = serde_json::from_value(submitted.clone())
        .map_err(|_| invalid("任职与汇报关系格式无效。"))?;
    let company = text(person, "companyScope");
    let previous = if person["id"].as_i64().unwrap_or(0) > 0 {
        store::get(tx, "people", person["id"].as_i64().unwrap())?
    } else {
        Value::Null
    };
    let previous_assignments = assignments(&previous)?;
    let previous_org = self::organization(&previous)?;
    organization.primary_assignment_id = if let Some(primary) = primary {
        let selected = previous_assignments
            .iter()
            .find(|a| a.id == primary && a.company_code == company)
            .ok_or_else(|| invalid("新的主任职必须来自此人员的已有任职。"))?;
        organization.primary_starts_on = selected.starts_on.clone();
        selected.id.clone()
    } else if previous_org.primary_assignment_id.is_empty() {
        organization.primary_starts_on = text(person, "hireDate");
        crate::paths::nonce().map_err(unavailable)?
    } else {
        organization.primary_starts_on =
            if previous["status"] == "Departed" && person["status"] != "Departed" {
                text(person, "hireDate")
            } else {
                previous_assignments[0].starts_on.clone()
            };
        previous_org.primary_assignment_id.clone()
    };
    for assignment in &mut organization.additional_assignments {
        if assignment.company_code.is_empty() {
            assignment.company_code = company.clone();
        }
        if assignment.id.is_empty() {
            assignment.id = previous_org
                .additional_assignments
                .iter()
                .find(|a| {
                    a.company_code == assignment.company_code
                        && a.department_id == assignment.department_id
                })
                .map(|a| a.id.clone())
                .filter(|id| !id.is_empty())
                .unwrap_or(crate::paths::nonce().map_err(unavailable)?);
        } else if !previous_assignments
            .iter()
            .any(|a| a.id == assignment.id && a.company_code == assignment.company_code)
        {
            return Err(invalid("任职编号不属于此人员或任职单位已改变。"));
        }
    }
    let mut identifiers =
        std::collections::HashSet::from([organization.primary_assignment_id.as_str()]);
    if organization
        .additional_assignments
        .iter()
        .any(|a| !identifiers.insert(a.id.as_str()))
    {
        return Err(invalid("任职编号不能重复。"));
    }
    let id = person["id"].as_i64().unwrap_or(0);
    organization
        .validate(id, &text(person, "departmentId"), &company)
        .map_err(invalid)?;
    super::personnel_reporting::normalize(tx, &company, &mut organization)?;
    for (unit, department) in std::iter::once((company.as_str(), text(person, "departmentId")))
        .chain(
            organization
                .additional_assignments
                .iter()
                .map(|a| (a.company_code.as_str(), a.department_id.clone())),
        )
    {
        super::organization::validate_assignment(tx, unit, &department)?;
        let scope = json!({"companyScope":unit,"departmentId":department,"ownerUserId":person["ownerUserId"]});
        let managed = super::units::demand(actor, unit, "organization.appointments").is_ok();
        let unchanged = if unit == company && department == text(person, "departmentId") {
            previous["companyScope"] == person["companyScope"]
                && previous["departmentId"] == person["departmentId"]
                && previous["jobTitle"] == person["jobTitle"]
                && self::organization(&previous)?.manager_employee_id
                    == organization.manager_employee_id
                && self::organization(&previous)?.functional_manager_employee_ids
                    == organization.functional_manager_employee_ids
                && self::organization(&previous)?.manager_assignment_id
                    == organization.manager_assignment_id
                && self::organization(&previous)?.functional_manager_assignment_ids
                    == organization.functional_manager_assignment_ids
        } else {
            organization
                .additional_assignments
                .iter()
                .find(|a| a.company_code == unit && a.department_id == department)
                .is_some_and(|a| previous_assignments.contains(a))
        };
        if !unchanged && !managed && !super::auth::visible(actor, "office.people", action, &scope) {
            return Err(super::error::error(403, "任职部门超出人事办理范围。"));
        }
    }
    // A full-personnel form may preserve another unit's assignments but cannot remove them.
    for old in self::organization(&previous)?.additional_assignments {
        if !organization.additional_assignments.contains(&old) {
            let scope = json!({"companyScope":old.company_code,"departmentId":old.department_id,"ownerUserId":person["ownerUserId"]});
            if super::units::demand(actor, &old.company_code, "organization.appointments").is_err()
                && !super::auth::visible(actor, "office.people", action, &scope)
            {
                return Err(super::error::error(403, "不能结束或改写其他单位的任职。"));
            }
        }
    }
    for (unit, managers) in std::iter::once((
        company.as_str(),
        organization
            .manager_employee_id
            .into_iter()
            .chain(organization.functional_manager_employee_ids.iter().copied())
            .collect::<Vec<_>>(),
    ))
    .chain(organization.additional_assignments.iter().map(|a| {
        (
            a.company_code.as_str(),
            a.manager_employee_id
                .into_iter()
                .chain(a.functional_manager_employee_ids.iter().copied())
                .collect(),
        )
    })) {
        for manager in managers {
            active_person(tx, unit, manager)?;
        }
    }
    person["organization"] = serde_json::to_value(organization)?;
    super::personnel_reporting::validate(tx, person)?;
    super::organization::validate_personnel_duties(tx, person)?;
    Ok(())
}
pub fn references(
    tx: &Connection,
    company: &str,
    employee: i64,
    active_only: bool,
) -> Result<usize> {
    let mut count = 0;
    let today = super::units::today(tx)?;
    for person in store::matching(
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
    )? {
        if active_only && !matches!(person["status"].as_str(), Some("Active" | "Probation")) {
            continue;
        }
        for assignment in assignments(&person)?
            .into_iter()
            .filter(|a| a.company_code == company)
        {
            if active_only && !assignment.active_on(today).map_err(unavailable)? {
                continue;
            }
            if assignment.manager_employee_id == Some(employee)
                || assignment
                    .functional_manager_employee_ids
                    .contains(&employee)
            {
                count += 1;
                break;
            }
        }
    }
    Ok(count)
}
pub fn appointment_managers(
    tx: &Connection,
    person: &Value,
    company: &str,
    department: &str,
    functional: bool,
) -> Result<Vec<i64>> {
    let assignment = appointment_in(tx, person, company, department)?;
    Ok(if functional {
        assignment.functional_manager_employee_ids.clone()
    } else {
        assignment.manager_employee_id.into_iter().collect()
    })
}

pub fn project(tx: &Connection, person: &Value) -> Result<Value> {
    let organization = organization(person)?;
    let mut value = serde_json::to_value(&organization)?;
    let mut names = HashMap::new();
    for id in organization.manager_ids() {
        let manager = store::get(tx, "people", id)?;
        names.insert(
            id,
            format!(
                "{} · {}",
                text(&manager, "profile.fullName"),
                text(&manager, "employeeNumber")
            ),
        );
    }
    fn enrich(value: &mut Value, names: &HashMap<i64, String>) {
        value["managerName"] = json!(
            value["managerEmployeeId"]
                .as_i64()
                .and_then(|id| names.get(&id))
                .cloned()
                .unwrap_or_default()
        );
        value["functionalManagerNames"] = json!(
            value["functionalManagerEmployeeIds"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(|id| id.as_i64().and_then(|id| names.get(&id)))
                .collect::<Vec<_>>()
        );
    }
    enrich(&mut value, &names);
    let mut departments = super::organization::DepartmentNames::default();
    for assignment in value["additionalAssignments"]
        .as_array_mut()
        .into_iter()
        .flatten()
    {
        enrich(assignment, &names);
        assignment["departmentName"] = json!(departments.get(
            tx,
            &text(assignment, "companyCode"),
            &text(assignment, "departmentId")
        )?);
    }
    Ok(value)
}
