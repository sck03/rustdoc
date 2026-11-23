//! Public display projections; never load private personnel details or removal references.
use super::{
    error::{Result, unavailable},
    records::text,
    store::{self, Connection},
};
use serde_json::{Value, json};
use std::collections::{BTreeSet, HashMap};

pub(super) fn enrich(tx: &Connection, people: &mut [Value]) -> Result<()> {
    let mut ids = BTreeSet::new();
    let mut departments = BTreeSet::new();
    let mut companies = BTreeSet::new();
    for person in people.iter_mut() {
        let organization = super::personnel_organization::organization(person)?;
        ids.extend(organization.manager_ids());
        person["organization"] = serde_json::to_value(organization)?;
        for assignment in person["organization"]["additionalAssignments"]
            .as_array()
            .into_iter()
            .flatten()
            .chain(
                person["organization"]["assignmentHistory"]
                    .as_array()
                    .into_iter()
                    .flatten(),
            )
        {
            let company = text(assignment, "companyCode");
            departments.insert(store::company_identity(
                &company,
                &text(assignment, "departmentId"),
            )?);
            companies.insert(store::normalize(&company));
        }
        let company = text(person, "companyScope");
        if !company.is_empty() {
            departments.insert(store::company_identity(
                &company,
                &text(person, "departmentId"),
            )?);
            companies.insert(store::normalize(&company));
        }
    }
    let names: HashMap<_, _> = tx
        .records_by_ids(
            "people",
            &ids.into_iter().collect::<Vec<_>>(),
            &[
                ("id", "id"),
                ("name", "profile.fullName"),
                ("employeeNumber", "employeeNumber"),
            ],
        )?
        .into_iter()
        .map(|v| {
            (
                v["id"].as_i64().unwrap_or(0),
                format!("{} · {}", text(&v, "name"), text(&v, "employeeNumber")),
            )
        })
        .collect();
    let department_names: HashMap<_, _> = tx
        .records_by_identities(
            "departments",
            &departments.into_iter().collect::<Vec<_>>(),
            &[
                ("companyCode", "companyCode"),
                ("code", "code"),
                ("name", "name"),
            ],
        )?
        .into_iter()
        .map(|v| {
            (
                (text(&v, "companyCode"), text(&v, "code")),
                text(&v, "name"),
            )
        })
        .collect();
    let company_names: HashMap<_, _> = tx
        .records_by_identities(
            "companies",
            &companies.into_iter().collect::<Vec<_>>(),
            &[("code", "code"), ("name", "name")],
        )?
        .into_iter()
        .map(|v| (text(&v, "code"), text(&v, "name")))
        .collect();
    let reporting = |value: &mut Value| -> Result<()> {
        let name = |id: i64| {
            names
                .get(&id)
                .cloned()
                .ok_or_else(|| unavailable("汇报负责人引用无效。"))
        };
        value["managerName"] = json!(
            value["managerEmployeeId"]
                .as_i64()
                .map(name)
                .transpose()?
                .unwrap_or_default()
        );
        value["functionalManagerNames"] = json!(
            value["functionalManagerEmployeeIds"]
                .as_array()
                .into_iter()
                .flatten()
                .map(|id| name(id.as_i64().unwrap_or(0)))
                .collect::<Result<Vec<_>>>()?
        );
        Ok(())
    };
    let labels = |value: &mut Value, company: String| {
        value["companyName"] = json!(
            company_names
                .get(&company)
                .cloned()
                .unwrap_or(company.clone())
        );
        let code = text(value, "departmentId");
        value["departmentName"] = json!(
            department_names
                .get(&(company, code.clone()))
                .cloned()
                .unwrap_or(code)
        );
    };
    for person in people {
        labels(person, text(person, "companyScope"));
        let organization = &mut person["organization"];
        reporting(organization)?;
        for field in ["additionalAssignments", "assignmentHistory"] {
            for assignment in organization[field].as_array_mut().into_iter().flatten() {
                if field == "additionalAssignments" {
                    reporting(assignment)?;
                }
                labels(assignment, text(assignment, "companyCode"));
            }
        }
    }
    Ok(())
}
