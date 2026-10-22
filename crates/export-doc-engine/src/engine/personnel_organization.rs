//! Personnel-owned appointments; account scope and workflow routing derive from these records.
use super::{
    error::{Result, invalid, unavailable},
    records::text,
    store::{self, Actor},
};
use export_doc_domain::personnel_organization::{PersonnelOrganization, validate_reporting_graph};
use export_doc_storage::{Connection, DataScope, GenericQuery};
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
    Ok(organization(person)?.departments(&text(person, "departmentId")))
}
pub fn active_person(tx: &Connection, company: &str, id: i64) -> Result<Value> {
    let person = store::get(tx, "people", id)?;
    if person["companyScope"] != company
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
    let organization: PersonnelOrganization = serde_json::from_value(submitted.clone())
        .map_err(|_| invalid("任职与汇报关系格式无效。"))?;
    let id = person["id"].as_i64().unwrap_or(0);
    organization
        .validate(id, &text(person, "departmentId"))
        .map_err(invalid)?;
    let company = text(person, "companyScope");
    for department in organization.departments(&text(person, "departmentId")) {
        super::organization::validate_assignment(tx, &company, &department)?;
        let scope = json!({"companyScope":company,"departmentId":department,"ownerUserId":person["ownerUserId"]});
        if !super::auth::visible(actor, "office.people", action, &scope) {
            return Err(super::error::error(403, "任职部门超出人事办理范围。"));
        }
    }
    let managers = organization.manager_ids();
    for manager in &managers {
        active_person(tx, &company, *manager)?;
    }
    // A node without outgoing reporting edges cannot introduce a cycle.
    if !managers.is_empty() {
        let mut edges = HashMap::new();
        for other in directory(tx, &company)? {
            if other["id"] == id || other["status"] == "Departed" {
                continue;
            }
            edges.insert(
                other["id"]
                    .as_i64()
                    .ok_or_else(|| unavailable("人员编号损坏。"))?,
                self::organization(&other)?.manager_ids(),
            );
        }
        edges.insert(id, managers);
        validate_reporting_graph(&edges).map_err(invalid)?;
    }
    person["organization"] = serde_json::to_value(organization)?;
    Ok(())
}
pub fn references(
    tx: &Connection,
    company: &str,
    employee: i64,
    active_only: bool,
) -> Result<usize> {
    let mut count = 0;
    for person in directory(tx, company)? {
        if (!active_only || person["status"] != "Departed")
            && organization(&person)?.manager_ids().contains(&employee)
        {
            count += 1;
        }
    }
    Ok(count)
}
pub fn appointment_managers(
    person: &Value,
    department: &str,
    functional: bool,
) -> Result<Vec<i64>> {
    let organization = organization(person)?;
    if person["departmentId"] == department {
        return Ok(if functional {
            organization.functional_manager_employee_ids
        } else {
            organization.manager_employee_id.into_iter().collect()
        });
    }
    let assignment = organization
        .additional_assignments
        .iter()
        .find(|a| a.department_id == department)
        .ok_or_else(|| invalid("申请部门不在员工当前任职范围内。"))?;
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
            &text(person, "companyScope"),
            &text(assignment, "departmentId")
        )?);
    }
    Ok(value)
}
