//! Resolve organization leaders once, using only the public display fields.
use super::*;
use crate::engine::error::unavailable;
use std::collections::BTreeSet;

pub(super) fn enrich(
    tx: &Connection,
    companies: &mut [Value],
    departments: &mut [Value],
) -> Result<()> {
    let mut ids = BTreeSet::new();
    for row in companies.iter().chain(departments.iter()) {
        ids.extend(row["executiveEmployeeId"].as_i64());
        ids.extend(row["managerEmployeeId"].as_i64());
        ids.extend(leaders(row)?.iter().map(|leader| leader.employee_id));
    }
    let people: HashMap<_, _> = tx
        .records_by_ids(
            "people",
            &ids.into_iter().collect::<Vec<_>>(),
            &[
                ("id", "id"),
                ("fullName", "profile.fullName"),
                ("employeeNumber", "employeeNumber"),
            ],
        )?
        .into_iter()
        .map(|person| (person["id"].as_i64().unwrap_or(0), person))
        .collect();
    let person = |id| {
        people
            .get(&id)
            .ok_or_else(|| unavailable("组织负责人引用无效。"))
    };
    for row in companies.iter_mut().chain(departments.iter_mut()) {
        let mut names = serde_json::Map::new();
        for leader in leaders(row)? {
            let leader_row = person(leader.employee_id)?;
            names.insert(
                leader.employee_id.to_string(),
                json!(format!(
                    "{} · {}",
                    text(leader_row, "fullName"),
                    text(leader_row, "employeeNumber")
                )),
            );
        }
        row["additionalLeaderNames"] = Value::Object(names);
    }
    for row in companies {
        row["executiveName"] = match row["executiveEmployeeId"].as_i64() {
            Some(id) => person(id)?["fullName"].clone(),
            None => json!(""),
        };
    }
    for row in departments {
        *row = crate::contracts::overlay(
            crate::contracts::initial(crate::contracts::schema("ApiOrganizationDepartmentDto")),
            row,
        );
        row["managerName"] = json!("");
        row["managerEmployeeNumber"] = json!("");
        if let Some(id) = row["managerEmployeeId"].as_i64() {
            let leader = person(id)?;
            row["managerName"] = leader["fullName"].clone();
            row["managerEmployeeNumber"] = leader["employeeNumber"].clone();
        }
    }
    Ok(())
}

pub(in crate::engine) fn project(tx: &Connection, row: Value) -> Result<Value> {
    let mut rows = [row];
    enrich(tx, &mut [], &mut rows)?;
    Ok(rows.into_iter().next().unwrap())
}
