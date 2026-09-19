//! Personnel directory and detail projections never expose a stored permission snapshot.
use super::{
    auth,
    error::{Result, error, invalid, unavailable},
    office,
    records::text,
    store::{self, Actor, Store},
};
use crate::{clock::BusinessClock, contracts};
use chrono::{Duration, NaiveDate};
use export_doc_storage::Connection;
use serde_json::{Value, json};

pub fn employee(tx: &Connection, actor: &Actor, record: &Value) -> Result<Value> {
    Ok(directory_entry(
        actor,
        record,
        &store::all(tx, "departments")?,
    ))
}
fn directory_entry(actor: &Actor, record: &Value, departments: &[Value]) -> Value {
    let mut employee = record["employee"].clone();
    let department = departments.iter().find(|department| {
        department["code"] == record["departmentId"]
            && department["companyCode"] == record["companyScope"]
    });
    employee["departmentName"] = department
        .map(|department| department["name"].clone())
        .unwrap_or_else(|| record["departmentId"].clone());
    employee["canViewDetails"] = json!(auth::visible(
        actor,
        "office.people",
        "view-details",
        record
    ));
    employee
}

pub fn detail(tx: &Connection, actor: &Actor, mut record: Value) -> Result<Value> {
    record["employee"] = employee(tx, actor, &record)?;
    let editable = record["status"] != "Departed";
    let correct = office::can_correct(
        tx,
        record["id"]
            .as_i64()
            .ok_or_else(|| unavailable("人员编号损坏。"))?,
    )?;
    for (field, action, allowed) in [
        ("canEdit", "edit", editable),
        ("canTransition", "transition", true),
        (
            "canLinkAccount",
            "assign",
            editable && tx.provider() == "PostgreSQL" && actor.admin,
        ),
        ("canDelete", "delete", true),
        ("canCorrectRegistration", "edit", editable && correct),
    ] {
        record[field] = json!(allowed && auth::visible(actor, "office.people", action, &record));
    }
    record["deleteRestriction"] = json!(if correct {
        ""
    } else {
        "已有账号、业务记录、负责人职责或正式任职变动，请通过调岗、离职流程处理。"
    });
    Ok(record)
}

pub fn read(store: &Store, actor: &Actor, id: i64) -> Result<Value> {
    auth::authorize(actor, "office.people", "view-details")?;
    store.transaction(|tx| {
        let record = store::get(tx, "people", id)?;
        if !auth::visible(actor, "office.people", "view-details", &record) {
            return Err(error(403, "没有查看此人员档案的权限。"));
        }
        detail(tx, actor, record)
    })
}

pub fn options(store: &Store, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "office.people", "view")?;
    let departments: Vec<_> = store
        .all("departments")?
        .into_iter()
        .filter(|department| department["companyCode"] == actor.company)
        .map(|department| {
            contracts::project(contracts::schema("PersonnelDepartmentRecord"), department)
        })
        .collect();
    Ok(
        json!({"departments":departments,"canCreate":auth::authorize(actor,"office.people","create").is_ok()}),
    )
}

pub fn directory(
    store: &Store,
    actor: &Actor,
    clock: &BusinessClock,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, "office.people", "view")?;
    let argument = |name| {
        query
            .iter()
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let status = argument("status");
    if !["", "Probation", "Active", "Departed"].contains(&status) {
        return Err(invalid("任职状态无效。"));
    }
    let attention = argument("attentionOnly") == "true";
    if attention || status == "Departed" {
        auth::authorize(actor, "office.people", "view-details")?;
    }
    let today = clock.now().map_err(unavailable)?.today;
    let through = today
        .checked_add_signed(Duration::days(30))
        .ok_or_else(|| unavailable("业务日期超出范围。"))?;
    let keyword = super::organization::search_key(argument("keyword"));
    store.transaction(|tx| {
        let mut items = vec![];
        let departments = store::all(tx, "departments")?;
        for record in store::all(tx, "people")? {
            crate::operation::check()?;
            if record["companyScope"] != actor.company
                || !auth::visible(actor, "office.people", "view", &record)
            {
                continue;
            }
            if (!status.is_empty() && record["status"] != status)
                || (status.is_empty() && record["status"] == "Departed")
            {
                continue;
            }
            if !argument("departmentId").is_empty()
                && record["departmentId"] != argument("departmentId")
            {
                continue;
            }
            if attention
                && (!auth::visible(actor, "office.people", "view-details", &record)
                    || !needs_attention(&record, through)?)
            {
                continue;
            }
            let row = directory_entry(actor, &record, &departments);
            let searchable = [
                "fullName",
                "employeeNumber",
                "jobTitle",
                "workEmail",
                "workPhone",
                "workLocation",
            ]
            .into_iter()
            .map(|field| text(&row, field))
            .collect::<Vec<_>>()
            .join(" ");
            if !super::organization::search_key(&searchable).contains(&keyword) {
                continue;
            }
            items.push(row);
        }
        items.sort_by(|a, b| {
            store::normalize(&text(a, "employeeNumber"))
                .cmp(&store::normalize(&text(b, "employeeNumber")))
                .then_with(|| a["id"].as_i64().cmp(&b["id"].as_i64()))
        });
        let paging: Vec<_> = query
            .iter()
            .filter(|(key, _)| ["pageNumber", "pageSize"].contains(key))
            .cloned()
            .collect();
        Ok(store::paged(items, &paging))
    })
}

fn needs_attention(record: &Value, through: NaiveDate) -> Result<bool> {
    if record["status"] == "Departed" {
        return Ok(false);
    }
    for (enabled, field) in [
        (record["status"] == "Probation", "probationEndsOn"),
        (true, "contractEndsOn"),
        (
            record["profile"]["identityLongTerm"] != true,
            "profile.identityValidUntil",
        ),
    ] {
        let value = text(record, field);
        if !enabled || value.is_empty() {
            continue;
        }
        let date = NaiveDate::parse_from_str(&value, "%Y-%m-%d")
            .map_err(|_| unavailable("人员到期日期无效。"))?;
        if date <= through {
            return Ok(true);
        }
    }
    Ok(false)
}

pub fn history(store: &Store, actor: &Actor, id: i64, query: &[(&str, String)]) -> Result<Value> {
    read(store, actor, id)?;
    let mut events: Vec<_> = store
        .all("personnel-events")?
        .into_iter()
        .filter(|event| event["employeeId"] == id)
        .collect();
    events.sort_by_key(|event| std::cmp::Reverse(event["id"].as_i64()));
    Ok(store::paged(events, query))
}
