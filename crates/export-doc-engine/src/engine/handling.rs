//! Company service ownership is independent of approval chains and stock transitions.
use super::{
    auth,
    error::{Result, conflict, invalid, unavailable},
    records::{required, text},
    store::{self, Actor},
};
use crate::contracts;
use export_doc_storage::{Connection, RecordQuery};
use serde_json::{Value, json};
use std::collections::HashSet;

pub(in crate::engine) const GENERAL_STATES: &[&str] = &["Approved", "Completed"];
pub(in crate::engine) const SUPPLY_STATES: &[&str] = &["Approved", "Issued", "Returned"];
pub(in crate::engine) const ROOM_STATES: &[&str] = &["Approved", "InUse", "Completed"];

fn category_resource(category: &str) -> Result<&'static str> {
    match category {
        "Seal" | "Certificate" | "IT" | "Repair" | "Other" => Ok("office.general"),
        "Supply" => Ok("office.supplies"),
        "Room" => Ok("office.rooms"),
        _ => Err(invalid("办理分工类别无效。")),
    }
}
fn action(resource: &str) -> Result<&'static str> {
    match resource {
        "office.general" => Ok("complete"),
        "office.supplies" | "office.rooms" => Ok("issue"),
        _ => Err(invalid("不支持此模块的办理分工。")),
    }
}
pub(super) fn states(resource: &str) -> Result<&'static [&'static str]> {
    match resource {
        "office.general" => Ok(GENERAL_STATES),
        "office.supplies" => Ok(SUPPLY_STATES),
        "office.rooms" => Ok(ROOM_STATES),
        _ => Err(invalid("不支持此模块的办理分工。")),
    }
}
pub(super) fn outstanding(row: &Value) -> bool {
    row["status"] == "Approved"
        || row["status"] == "InUse"
        || row["status"] == "Issued" && row["isReturnable"] == true
}
pub(super) fn validate_resource(
    tx: &Connection,
    actor: &Actor,
    kind: &str,
    id: i64,
    previous: &Value,
    row: &mut Value,
) -> Result<()> {
    let (resource, requests, parent, category) = match kind {
        "rooms" => ("office.rooms", "bookings", "meetingRoomId", "Room"),
        "supplies" => (
            "office.supplies",
            "supply-requests",
            "officeSupplyId",
            "Supply",
        ),
        _ => return Err(invalid("未知行政资源。")),
    };
    let changed = text(row, "handlingKey") != text(previous, "handlingKey");
    if id > 0 {
        if !resource_access(tx, actor, resource, "edit", previous, true)? {
            return Err(super::error::error(403, "只能维护本人负责的资源。"));
        }
        if (changed || row["isActive"] == false)
            && store::all(tx, requests)?
                .iter()
                .any(|r| r[parent] == id && (r["status"] == "Pending" || outstanding(r)))
        {
            return Err(conflict(
                "资源尚有未结申请，不能停用或更换分工；人员交接请修改原分工的办理人员。",
            ));
        }
    }
    if id == 0 || changed {
        select(tx, actor, row, category, false)?;
    } else {
        row["handlingName"] = json!(text(previous, "handlingName"));
    }
    if !text(row, "handlingKey").is_empty() && !can_handle(tx, actor, row, resource)? {
        return Err(super::error::error(403, "只能选择本人负责的资源分工。"));
    }
    Ok(())
}

pub(super) fn snapshot(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    source: &Value,
    category: &str,
    new: bool,
) -> Result<()> {
    for field in ["handlingKey", "handlingName"] {
        row[field] = json!(text(source, field));
    }
    if new {
        select(tx, actor, row, category, false)?;
    }
    Ok(())
}

pub(in crate::engine) fn services(tx: &Connection, company: &str) -> Result<Vec<Value>> {
    let settings = super::oa::settings::load(tx, company)?;
    match settings.get("handlingServices") {
        None => Ok(vec![]),
        Some(Value::Array(rows)) => Ok(rows.clone()),
        _ => Err(unavailable("办理分工目录损坏。")),
    }
}
pub(in crate::engine) fn member(actor: &Actor, service: &Value) -> bool {
    service["handlerUserIds"]
        .as_array()
        .is_some_and(|ids| ids.contains(&json!(actor.id)))
}
pub(in crate::engine) fn keys(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
) -> Result<Vec<String>> {
    let action = action(resource)?;
    if auth::authorize(actor, resource, "view").is_err()
        || auth::authorize(actor, resource, action).is_err()
    {
        return Ok(vec![]);
    }
    Ok(services(tx, &actor.company)?
        .iter()
        .filter(|s| {
            category_resource(&text(s, "category")).ok() == Some(resource)
                && (actor.admin || member(actor, s))
        })
        .map(|s| text(s, "key"))
        .collect())
}
pub(in crate::engine) fn assigned(keys: &[String], row: &Value) -> bool {
    keys.contains(&text(row, "handlingKey"))
}
pub(in crate::engine) fn can_handle(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    resource: &str,
) -> Result<bool> {
    if row["companyScope"] != actor.company {
        return Ok(false);
    }
    let action = action(resource)?;
    if text(row, "handlingKey").is_empty() {
        return Ok(auth::visible(actor, resource, action, row));
    }
    Ok(assigned(&keys(tx, actor, resource)?, row))
}
pub(in crate::engine) fn resource_access(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    action: &str,
    row: &Value,
    directory: bool,
) -> Result<bool> {
    if row["companyScope"] != actor.company || auth::authorize(actor, resource, action).is_err() {
        return Ok(false);
    }
    let assigned = can_handle(tx, actor, row, resource)?;
    if action == "view" {
        return Ok(auth::visible(actor, resource, action, row)
            || ((directory || states(resource)?.contains(&text(row, "status").as_str()))
                && assigned));
    }
    if !text(row, "handlingKey").is_empty()
        && matches!(action, "issue" | "return" | "restock" | "manage" | "edit")
    {
        return Ok(assigned);
    }
    Ok(auth::visible(actor, resource, action, row))
}
pub(in crate::engine) fn project_resource(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    mut row: Value,
) -> Result<Value> {
    for field in ["handlingKey", "handlingName"] {
        row[field] = json!(text(&row, field));
    }
    row["canHandle"] = json!(can_handle(tx, actor, &row, resource)?);
    row["handlerNames"] = json!(names(tx, actor, &row)?);
    Ok(row)
}
pub(in crate::engine) fn select(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    category: &str,
    required: bool,
) -> Result<()> {
    let key = text(row, "handlingKey");
    if key.is_empty() {
        if required {
            return Err(invalid("请选择已配置办理人员的事项或资源分工。"));
        }
        row["handlingName"] = json!("");
        return Ok(());
    }
    let service = services(tx, &actor.company)?
        .into_iter()
        .find(|s| s["key"] == key && s["category"] == category && s["isActive"] == true)
        .ok_or_else(|| invalid("所选办理事项不存在、已停用或类别不符，请重新选择。"))?;
    validate_handlers(tx, actor, &service)?;
    row["handlingName"] = service["name"].clone();
    Ok(())
}
fn validate_handlers(tx: &Connection, actor: &Actor, service: &Value) -> Result<()> {
    let ids = service["handlerUserIds"]
        .as_array()
        .filter(|ids| !ids.is_empty() && ids.len() <= 10)
        .ok_or_else(|| invalid("每项分工须指定 1–10 位办理人员。"))?;
    let resource = category_resource(&text(service, "category"))?;
    let mut seen = HashSet::new();
    for id in ids {
        let id = id
            .as_i64()
            .filter(|id| *id > 0 && seen.insert(*id))
            .ok_or_else(|| invalid("办理人员无效或重复。"))?;
        let user = auth::current_actor_in(tx, id, actor.edition)?;
        if user.company != actor.company
            || auth::authorize(&user, resource, "view").is_err()
            || auth::authorize(&user, resource, action(resource)?).is_err()
        {
            return Err(invalid(
                "办理人员须为本公司启用账号，并具有相应查看及发放/完成登记权限；本人范围即可按分工办理。",
            ));
        }
    }
    Ok(())
}
pub(in crate::engine) fn validate(
    tx: &Connection,
    actor: &Actor,
    values: &Value,
    previous: &Value,
) -> Result<()> {
    let rows = values
        .as_array()
        .filter(|rows| rows.len() <= 100)
        .ok_or_else(|| invalid("办理分工最多 100 项。"))?;
    let mut keys = HashSet::new();
    let mut names = HashSet::new();
    for row in rows {
        required(row, "key", "分工编号", 100)?;
        required(row, "name", "事项或物品组名称", 100)?;
        let category = text(row, "category");
        if category_resource(&category).is_err()
            || !keys.insert(text(row, "key"))
            || !names.insert((category.clone(), store::normalize(&text(row, "name"))))
        {
            return Err(invalid("办理分工类别无效、编号或同类名称重复。"));
        }
        validate_handlers(tx, actor, row)?;
    }
    for old in previous.as_array().into_iter().flatten() {
        if rows
            .iter()
            .any(|row| row["key"] == old["key"] && row["category"] == old["category"])
        {
            continue;
        }
        for kind in [
            "oa-general",
            "supplies",
            "supply-requests",
            "rooms",
            "bookings",
        ] {
            if tx
                .query_records(&RecordQuery {
                    kind,
                    company: &actor.company,
                    handling_key: old["key"].as_str(),
                    limit: 1,
                    ..Default::default()
                })?
                .0
                > 0
            {
                return Err(conflict(
                    "已有记录引用此分工，不能删除或更换类别；可停用新申请并保留办理人员。",
                ));
            }
        }
    }
    Ok(())
}
pub(in crate::engine) fn names(tx: &Connection, actor: &Actor, row: &Value) -> Result<String> {
    let service = services(tx, &actor.company)?
        .into_iter()
        .find(|s| s["key"] == row["handlingKey"]);
    let mut names = vec![];
    if let Some(service) = service {
        for id in service["handlerUserIds"].as_array().into_iter().flatten() {
            let user = store::get(
                tx,
                "users",
                id.as_i64()
                    .ok_or_else(|| unavailable("办理人员编号损坏。"))?,
            )?;
            names.push(format!(
                "{}{}",
                text(&user, "fullName"),
                if user["isActive"] == true {
                    ""
                } else {
                    "（已停用）"
                }
            ));
        }
    }
    Ok(names.join("、"))
}
pub(in crate::engine) fn directory(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
) -> Result<Value> {
    action(resource)?;
    let mut rows = services(tx, &actor.company)?;
    rows.retain(|s| category_resource(&text(s, "category")).ok() == Some(resource));
    for row in &mut rows {
        row["handlerNames"] = json!(names(tx, actor, &json!({"handlingKey":row["key"]}))?);
    }
    Ok(contracts::dto(
        contracts::schema("OfficeHandlingDirectory"),
        json!({"items":rows}),
    ))
}
