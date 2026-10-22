//! Resolve live department scope without persisting derived hierarchy on accounts.
use super::{
    error::{Result, unavailable},
    records::text,
};
use export_doc_storage::{Connection, DataScope, GenericQuery};
use serde_json::Value;
use std::collections::HashSet;

pub fn departments(tx: &Connection, user: &Value, include_managed: bool) -> Result<Vec<String>> {
    let mut result: Vec<String> = match user.get("organizationDepartments") {
        Some(value) => {
            serde_json::from_value(value.clone()).map_err(|_| unavailable("账号任职部门损坏。"))?
        }
        None => vec![text(user, "departmentId")],
    };
    if !include_managed {
        return Ok(result);
    }
    let Some(person) =
        super::personnel_accounts::linked_person(tx, user["id"].as_i64().unwrap_or(0))?
    else {
        return Ok(result);
    };
    if person["companyScope"] != user["companyScope"]
        || !matches!(person["status"].as_str(), Some("Active" | "Probation"))
    {
        return Ok(result);
    }
    let company = text(user, "companyScope");
    let filters = [
        ("companyCode", company.clone()),
        ("isActive", "true".into()),
        ("managerEmployeeId", person["id"].to_string()),
    ];
    if query(tx, &filters, 0, 1)?.0 == 0 {
        return Ok(result);
    }
    let mut rows = vec![];
    loop {
        crate::operation::check()?;
        let (total, page) = query(
            tx,
            &[("companyCode", company.clone())],
            rows.len() as i64,
            200,
        )?;
        rows.extend(page);
        if rows.len() as i64 >= total {
            break;
        }
    }
    let tree = rows
        .iter()
        .cloned()
        .map(serde_json::from_value::<export_doc_domain::organization::DepartmentLink>)
        .collect::<std::result::Result<Vec<_>, _>>()
        .map_err(|_| unavailable("部门层级数据损坏。"))?;
    let ancestors = export_doc_domain::organization::ancestry(&tree).map_err(unavailable)?;
    let managed: HashSet<_> = rows
        .iter()
        .filter(|row| row["isActive"] == true && row["managerEmployeeId"] == person["id"])
        .map(|row| text(row, "code"))
        .collect();
    result.extend(
        rows.iter()
            .filter(|row| row["isActive"] == true)
            .filter_map(|row| {
                let code = text(row, "code");
                (managed.contains(&code)
                    || ancestors[&code]
                        .iter()
                        .any(|parent| managed.contains(parent)))
                .then_some(code)
            }),
    );
    result.sort();
    result.dedup();
    Ok(result)
}

fn query(
    tx: &Connection,
    filters: &[(&str, String)],
    offset: i64,
    limit: i64,
) -> Result<(i64, Vec<Value>)> {
    Ok(tx.query_generic(&GenericQuery {
        kind: "departments",
        scope: DataScope {
            all: true,
            ..Default::default()
        },
        filters,
        keyword: "",
        search_root: None,
        fields: &[
            ("code", "code"),
            ("companyCode", "companyCode"),
            ("parentCode", "parentCode"),
            ("isActive", "isActive"),
            ("managerEmployeeId", "managerEmployeeId"),
        ],
        offset,
        limit,
    })?)
}
