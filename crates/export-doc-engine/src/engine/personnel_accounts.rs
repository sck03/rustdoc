//! Read the personnel/account relationship without loading private personnel files.
use super::{
    error::{Result, unavailable},
    store::Store,
};
use crate::contracts;
use export_doc_storage::{Connection, DataScope, GenericQuery};
use serde_json::Value;
use std::collections::HashMap;

const FIELDS: &[(&str, &str)] = &[
    ("id", "id"),
    ("accountId", "account.id"),
    ("employeeNumber", "employeeNumber"),
    ("fullName", "profile.fullName"),
    ("jobTitle", "jobTitle"),
    ("status", "status"),
    ("companyScope", "companyScope"),
    ("departmentId", "departmentId"),
];

fn query(
    tx: &Connection,
    filters: &[(&str, String)],
    offset: i64,
    limit: i64,
) -> Result<(i64, Vec<Value>)> {
    Ok(tx.query_generic(&GenericQuery {
        kind: "people",
        scope: DataScope {
            all: true,
            ..Default::default()
        },
        filters,
        keyword: "",
        search_root: None,
        fields: FIELDS,
        offset,
        limit,
    })?)
}

pub fn linked_person(tx: &Connection, user_id: i64) -> Result<Option<Value>> {
    let (total, mut rows) = query(tx, &[("account.id", user_id.to_string())], 0, 2)?;
    if total > 1 {
        return Err(unavailable("账号关联了多个员工档案，请核查数据。"));
    }
    Ok(rows.pop())
}

pub fn person_for_account(tx: &Connection, user_id: i64) -> Result<Option<Value>> {
    linked_person(tx, user_id)?
        .map(|person| {
            let id = person["id"]
                .as_i64()
                .ok_or_else(|| unavailable("人员编号损坏。"))?;
            tx.get("people", id)?
                .ok_or_else(|| unavailable("人员账号关联已失效。"))
        })
        .transpose()
}

pub fn links(tx: &Connection) -> Result<HashMap<i64, Value>> {
    let mut links = HashMap::new();
    let mut offset = 0;
    loop {
        crate::operation::check()?;
        let (total, rows) = query(tx, &[], offset, 200)?;
        if rows.is_empty() {
            break;
        }
        offset += rows.len() as i64;
        for row in rows {
            if let Some(id) = row["accountId"].as_i64() {
                if links.insert(id, row).is_some() {
                    return Err(unavailable("账号关联了多个员工档案，请核查数据。"));
                }
            }
        }
        if offset >= total {
            break;
        }
    }
    Ok(links)
}

pub fn summary(person: Value) -> Value {
    contracts::dto(contracts::schema("AccountPersonnelRecord"), person)
}

pub fn enrich_users(store: &Store, users: &mut [Value]) -> Result<()> {
    let mut links = store.read(links)?;
    for user in users {
        user["employee"] = user["id"]
            .as_i64()
            .and_then(|id| links.remove(&id))
            .map(summary)
            .unwrap_or(Value::Null);
    }
    Ok(())
}

pub fn current_account(tx: &Connection, person: &Value) -> Result<Value> {
    let Some(id) = person["account"]["id"].as_i64() else {
        return Ok(Value::Null);
    };
    let user = tx
        .get("users", id)?
        .filter(|user| user["companyScope"] == person["companyScope"] && user["role"] != "Admin")
        .ok_or_else(|| unavailable("人员关联账号不存在或公司、角色不一致。"))?;
    Ok(contracts::dto(
        contracts::schema("PersonnelAccountRecord"),
        user,
    ))
}
