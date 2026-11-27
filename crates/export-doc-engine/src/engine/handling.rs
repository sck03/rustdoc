//! Company service ownership is independent of approval chains and stock transitions.
use super::{
    auth,
    error::{Result, conflict, invalid, unavailable},
    records::{required, text},
    store::{self, Actor},
};
use crate::contracts;
use crate::engine::store::Connection;
use export_doc_storage::RecordQuery;
use serde_json::{Value, json};
use std::collections::HashSet;
mod account_references;
pub(in crate::engine) use account_references::validate_account_in;

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
    let (resource, requests, category) = match kind {
        "rooms" => ("office.rooms", "bookings", "Room"),
        "supplies" => ("office.supplies", "supply-requests", "Supply"),
        _ => return Err(invalid("未知行政资源。")),
    };
    let changed = text(row, "serviceId") != text(previous, "serviceId");
    if id > 0 {
        if !resource_access(tx, actor, resource, "edit", previous, true)? {
            return Err(super::error::error(403, "只能维护本人负责的资源。"));
        }
        let states = if requests == "bookings" {
            export_doc_domain::oa::ROOM_OCCUPYING_STATES
        } else {
            export_doc_domain::oa::SUPPLY_IN_FLIGHT_STATES
        };
        if (changed || row["isActive"] == false)
            && tx.resource_has_open_requests(requests, id, states)?
        {
            return Err(conflict(
                "资源尚有未结申请，不能停用或更换分工；人员交接请修改原分工的办理人员。",
            ));
        }
    }
    if id == 0 || changed {
        select(tx, actor, row, category, false, true)?;
    } else {
        row["handlingName"] = json!(text(previous, "handlingName"));
    }
    if !text(row, "serviceId").is_empty() && !can_handle(tx, actor, row, resource)? {
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
    for field in ["serviceId", "handlingName"] {
        row[field] = json!(text(source, field));
    }
    if new {
        select(tx, actor, row, category, false, true)?;
    }
    Ok(())
}

pub(in crate::engine) fn services(tx: &Connection, company: &str) -> Result<Vec<Value>> {
    let mut result = vec![];
    let mut cursor = String::new();
    loop {
        crate::operation::check()?;
        let page = tx.handling_service_page(company, "", &cursor, 100)?;
        if page.is_empty() {
            return Ok(result);
        }
        cursor = text(page.last().unwrap(), "key");
        result.extend(page);
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
    keys.contains(&text(row, "serviceId"))
}
pub(in crate::engine) fn can_handle(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    resource: &str,
) -> Result<bool> {
    can_handle_using(actor, row, resource, || {
        Ok(assigned(&keys(tx, actor, resource)?, row))
    })
}
pub(in crate::engine) fn can_handle_using(
    actor: &Actor,
    row: &Value,
    resource: &str,
    assigned: impl FnOnce() -> Result<bool>,
) -> Result<bool> {
    if row["companyScope"] != actor.company {
        return Ok(false);
    }
    let action = action(resource)?;
    if !auth::restriction_allows(actor, resource, "view", row, true)
        || !auth::restriction_allows(actor, resource, action, row, true)
    {
        return Ok(false);
    }
    if text(row, "serviceId").is_empty() {
        return Ok(auth::visible(actor, resource, action, row));
    }
    assigned()
}
pub(in crate::engine) fn resource_access(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    action: &str,
    row: &Value,
    directory: bool,
) -> Result<bool> {
    if row["companyScope"] != actor.company
        || auth::authorize(actor, resource, action).is_err()
        || !auth::restriction_allows(actor, resource, action, row, true)
    {
        return Ok(false);
    }
    let assigned = can_handle(tx, actor, row, resource)?;
    if action == "view" {
        if !directory && super::oa::assigned_to(tx, actor, resource, row)? {
            return Ok(true);
        }
        if !directory && actor.oa_assigned_only && !assigned {
            return Ok(false);
        }
        return Ok(auth::visible(actor, resource, action, row)
            || ((directory || states(resource)?.contains(&text(row, "status").as_str()))
                && assigned));
    }
    if !text(row, "serviceId").is_empty()
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
    for field in ["serviceId", "handlingName"] {
        row[field] = json!(text(&row, field));
    }
    row["canHandle"] = json!(can_handle(tx, actor, &row, resource)?);
    row["handlerNames"] = json!(names(tx, actor, &row)?);
    let review = super::oa::approval::ReviewSnapshot::read(tx, actor, &row)?;
    let tasks = review.task_keys();
    row["canReview"] = json!(!tasks.is_empty());
    row["reviewTaskKeys"] = json!(tasks);
    Ok(row)
}
pub(in crate::engine) fn select(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    category: &str,
    required: bool,
    submitting: bool,
) -> Result<()> {
    let key = text(row, "serviceId");
    if submitting && !required && category == "Other" {
        row["serviceId"] = json!("");
        row["handlingName"] = json!("");
        row["serviceRevision"] = json!(0);
        return Ok(());
    }
    if key.is_empty() {
        if required {
            let label = export_doc_contracts::native_contract::general_categories::label(category)
                .unwrap_or("当前");
            let available = !tx
                .search_handling_services(&actor.company, category, "", "", "active", "", 1)?
                .is_empty();
            return Err(invalid(if available {
                format!("{label}申请尚未选择办理事项，请选择后提交。")
            } else {
                format!("{label}类别尚未配置启用的办理事项，请联系流程管理员。")
            })
            .coded(if available {
                crate::api::ErrorCode::HandlingRequired
            } else {
                crate::api::ErrorCode::HandlingUnconfigured
            })
            .at("serviceId", "在办理分工中核对该类别的启用事项及办理人员。"));
        }
        row["handlingName"] = json!("");
        return Ok(());
    }
    let service = tx
        .get_handling_service(&actor.company, &key)?
        .filter(|s| {
            s["key"] == key && s["category"] == category && (!submitting || s["isActive"] == true)
        })
        .ok_or_else(|| {
            invalid("所选办理事项不存在、已停用或类别不符，请重新选择。")
                .coded(crate::api::ErrorCode::HandlingUnavailable)
                .at("serviceId", "重新选择当前类别的启用事项。")
        })?;
    if submitting
        && row["serviceRevision"]
            .as_i64()
            .is_some_and(|revision| revision > 0 && Some(revision) != service["revision"].as_i64())
    {
        return Err(
            conflict("办理事项及人员配置已变化，请重新选择并核对办理人员后提交。")
                .coded(crate::api::ErrorCode::ConcurrencyConflict)
                .at("serviceId", "刷新办理事项并重新确认选择。"),
        );
    }
    if submitting {
        validate_handlers(tx, actor, &service)?;
    }
    row["handlingName"] = service["name"].clone();
    if submitting
        || row["serviceRevision"]
            .as_i64()
            .is_none_or(|value| value == 0)
    {
        row["serviceRevision"] = service["revision"].clone();
    }
    row["categoryName"] = json!(
        export_doc_contracts::native_contract::general_categories::label(category)
            .unwrap_or(category)
    );
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
        let user = super::units::for_operation(
            tx,
            id,
            actor.edition,
            &actor.company,
            resource,
            action(resource)?,
        )
        .map_err(|cause| {
            if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) {
                invalid(format!(
                    "办理事项“{}”的人员资格无效：{}",
                    text(service, "name"),
                    cause.message
                ))
                .coded(crate::api::ErrorCode::HandlerUnavailable)
                .at(
                    "handlerUserIds",
                    "核对启用账号、人员关联、当前单位有效任职及实际办理权限。",
                )
            } else {
                cause
            }
        })?;
        if user.company != actor.company
            || auth::authorize(&user, resource, "view").is_err()
            || auth::authorize(&user, resource, action(resource)?).is_err()
        {
            return Err(invalid(format!(
                "办理事项“{}”的人员缺少当前单位的查看或{}权限。",
                text(service, "name"),
                if resource == "office.general" {
                    "完成办理"
                } else {
                    "发放/交接"
                }
            ))
            .coded(crate::api::ErrorCode::HandlerPermission)
            .at(
                "handlerUserIds",
                "请管理员核对办理人员的实际动作权限及单位范围。",
            ));
        }
    }
    Ok(())
}
pub(in crate::engine) fn validate(
    tx: &Connection,
    actor: &Actor,
    service: Option<&Value>,
    previous: Option<&Value>,
) -> Result<()> {
    if let Some(row) = service {
        required(row, "key", "事项编号", 100)?;
        required(row, "name", "事项名称", 100)?;
        category_resource(&text(row, "category"))?;
        validate_handlers(tx, actor, row)?;
    }
    if let Some(old) = previous {
        if service.is_some_and(|row| row["category"] == old["category"]) {
            return Ok(());
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
                    "已有记录引用此事项，不能删除或更换类别；可停用新申请并保留办理人员。",
                ));
            }
        }
    }
    Ok(())
}
pub(in crate::engine) fn names(tx: &Connection, actor: &Actor, row: &Value) -> Result<String> {
    let service = tx.get_handling_service(&actor.company, &text(row, "serviceId"))?;
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
                "{}（{}）{}",
                text(&user, "fullName"),
                text(&user, "username"),
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
    parameters: &[(&str, String)],
) -> Result<Value> {
    let query = &Value::Object(
        parameters
            .iter()
            .map(|(key, value)| ((*key).into(), json!(value)))
            .collect(),
    );
    if !resource.is_empty() {
        action(resource)?;
    }
    let category = text(query, "category");
    if !category.is_empty() {
        category_resource(&category)?;
    }
    let keyword = text(query, "keyword");
    if keyword.len() > 200 {
        return Err(invalid("事项搜索词过长。"));
    }
    let limit = query["pageSize"]
        .as_i64()
        .or_else(|| query["pageSize"].as_str().and_then(|s| s.parse().ok()))
        .unwrap_or(50)
        .clamp(1, 100);
    let mut rows = tx.search_handling_services(
        &actor.company,
        &category,
        resource,
        &keyword,
        &text(query, "state"),
        &text(query, "after"),
        limit,
    )?;
    let next = if rows.len() == limit as usize {
        text(rows.last().unwrap(), "key")
    } else {
        String::new()
    };
    for row in &mut rows {
        row["handlerNames"] = json!(names(tx, actor, &json!({"serviceId":row["key"]}))?);
    }
    let mut selected = tx
        .get_handling_service(&actor.company, &text(query, "selected"))?
        .filter(|s| {
            (resource.is_empty() || category_resource(&text(s, "category")).ok() == Some(resource))
                && (category.is_empty() || s["category"] == category)
        });
    if let Some(row) = &mut selected {
        row["handlerNames"] = json!(names(tx, actor, &json!({"serviceId":row["key"]}))?);
    }
    Ok(contracts::dto(
        contracts::schema("OfficeHandlingDirectory"),
        json!({"items":rows,"nextCursor":next,"selected":selected}),
    ))
}
