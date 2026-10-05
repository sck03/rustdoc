//! Independent office requests. Shared transitions, scoped persistence and files;
//! no dependency on invoice/payment models, reports or financial software.
pub(in crate::engine) mod approval;
pub(in crate::engine) mod approval_references;
pub(in crate::engine) mod handling;
mod handling_handover;
mod queries;
mod settings;
mod validation;
use super::{
    NativeService, auth,
    error::{Result, conflict, error, invalid},
    records::{self, required, text},
    store::{self, Actor},
};
use crate::{contracts, generated_api::Operation};
use export_doc_storage::{Connection, RecordQuery};
use serde_json::{Value, json};

pub fn metadata(operation: Operation) -> Option<&'static Value> {
    contracts::contract()["operations"][operation.id]
        .get("office")
        .filter(|v| v.is_object())
}
pub fn is_download(operation: Operation) -> bool {
    metadata(operation).is_some_and(|m| m["action"] == "download")
}
fn kind(meta: &Value) -> String {
    format!("oa-{}", text(meta, "kind"))
}
pub(super) const FINANCE_STATUSES: &[&str] = &["Approved", "HandedOff"];
pub(super) fn viewable(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    row: &Value,
) -> Result<bool> {
    Ok(row["companyScope"] == actor.company
        && auth::authorize(actor, resource, "view").is_ok()
        && (auth::visible(actor, resource, "view", row)
            || (resource == "office.expenses"
                && FINANCE_STATUSES.contains(&text(row, "status").as_str())
                && auth::visible(actor, resource, "complete", row))
            || (resource == "office.general"
                && handling::GENERAL_STATES.contains(&text(row, "status").as_str())
                && handling::can_handle(tx, actor, row, false)?)))
}
fn access(tx: &Connection, actor: &Actor, meta: &Value, row: &Value) -> Result<()> {
    let resource = text(meta, "resource");
    let permission = text(meta, "permission");
    auth::authorize(actor, &resource, &permission)?;
    let visible = if permission == "view" {
        viewable(tx, actor, &resource, row)?
    } else if permission == "complete" && resource == "office.general" {
        handling::can_handle(tx, actor, row, false)?
    } else {
        row["companyScope"] == actor.company && auth::visible(actor, &resource, &permission, row)
    };
    if !visible {
        return Err(error(403, "没有访问此申请的权限。"));
    }
    Ok(())
}
pub(super) fn mutable(row: &Value) -> Result<()> {
    if !["Draft", "Rejected"].contains(&text(row, "status").as_str()) {
        return Err(conflict(
            "只有草稿或已驳回的申请可修改，请先撤回待审批申请。",
        ));
    }
    Ok(())
}
pub(super) fn current(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    id: i64,
) -> Result<(Actor, Value)> {
    let actor = auth::current_actor_in(tx, actor.id, actor.edition)?;
    let row = store::get(tx, &kind(meta), id)?;
    access(tx, &actor, meta, &row)?;
    Ok((actor, row))
}
fn children(
    tx: &Connection,
    row: &Value,
    kind: &str,
    offset: i64,
    limit: i64,
) -> Result<(i64, Vec<Value>)> {
    Ok(tx.query_records(&RecordQuery {
        kind,
        company: row["companyScope"].as_str().unwrap_or(""),
        parent: row["id"].as_i64(),
        offset,
        limit,
        ..Default::default()
    })?)
}
pub(super) fn project(
    tx: &Connection,
    actor: &Actor,
    mut row: Value,
    detail: bool,
) -> Result<Value> {
    row["attachments"] = if detail {
        json!(children(tx, &row, "oa-attachment", 0, 20)?.1)
    } else {
        json!([])
    };
    if detail {
        row["canReview"] = json!(approval::authority(tx, actor, &row)?.is_some());
    }
    if row["kind"] == "general" {
        row["canHandle"] = json!(handling::can_handle(tx, actor, &row, false)?);
        row["handlerNames"] = json!(handling::names(tx, actor, &row)?);
    }
    Ok(contracts::dto(contracts::schema("OaRequest"), row))
}
fn append_event(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    action: &str,
    note: &str,
) -> Result<Value> {
    let mut body = json!({"requestId":row["id"],"requestVersion":row["versionNumber"],"action":action,"actorName":actor.name,"occurredAt":store::timestamp(),"note":note});
    if let Some(plan) = row.get("approvalPlan") {
        body["approvalPlan"] = plan.clone();
    }
    store::save_in_scope(tx, "oa-event", 0, body, None, actor, "oa-event", Some(row))
}
pub(super) fn save(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    row: Value,
    action: &str,
    note: &str,
) -> Result<Value> {
    let id = row["id"].as_i64().unwrap_or(0);
    let scope = row.clone();
    let identity = text(&row, "identity");
    let saved = store::save_in_scope(
        tx,
        &kind(meta),
        id,
        row,
        Some(identity),
        actor,
        action,
        Some(&scope),
    )?;
    let event = append_event(tx, actor, &saved, action, note)?;
    super::communication::on_event(tx, actor, &saved, &event, meta)?;
    project(tx, actor, saved, true)
}
pub(super) fn paging(query: &[(&str, String)]) -> Result<(i64, i64)> {
    let number = |key, default, max| -> Result<i64> {
        query
            .iter()
            .find(|(k, _)| *k == key)
            .map(|(_, v)| {
                v.parse::<i64>()
                    .ok()
                    .filter(|n| *n > 0 && *n <= max)
                    .ok_or_else(|| invalid("分页参数无效。"))
            })
            .unwrap_or(Ok(default))
    };
    Ok((
        number("pageNumber", 1, 1_000_000)?,
        number("pageSize", 20, 100)?,
    ))
}

pub(super) fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知审批功能。"))?;
    let action = text(meta, "action");
    let today = service
        .clock
        .now()
        .map_err(super::error::unavailable)?
        .today;
    service.store.transaction(|tx| {
        let actor = auth::current_actor_in(tx, actor.id, actor.edition)?;
        auth::authorize(&actor, &text(meta, "resource"), &text(meta, "permission"))?;
        if meta["kind"] == "approval-settings" {
            if action.starts_with("handling-") {
                return handling::directory(tx, &actor, action == "handling-supply");
            }
            return settings::handle(tx, &actor, &action, body);
        }
        if action == "list" {
            return queries::list(tx, &actor, meta, query);
        }
        if action == "create" {
            return create(tx, &actor, meta, body, today);
        }
        let id = records::id(parameters)?;
        let (_, mut row) = current(tx, &actor, meta, id)?;
        if action == "get" {
            return project(tx, &actor, row, true);
        }
        if action == "history" {
            let (page, size) = paging(query)?;
            let (count, items) = children(tx, &row, "oa-event", (page - 1) * size, size)?;
            return Ok(contracts::dto(
                contracts::schema("OaEventPage"),
                json!({"items":items,"totalCount":count,"pageNumber":page,"pageSize":size}),
            ));
        }
        store::check_version(&row, store::expected(body))?;
        if action == "delete-attachment" {
            return super::record_documents::remove(tx, &actor, meta, row, parameters, body);
        }
        if action == "update" {
            mutable(&row)?;
            if row["requestKey"] != body["requestKey"]
                || (body["employeeId"].is_number() && row["employeeId"] != body["employeeId"])
            {
                return Err(invalid("申请编号和人员不能更换。"));
            }
            let values = validation::fields(meta, body, today)?;
            for (key, value) in values.as_object().unwrap() {
                row[key] = value.clone();
            }
            if meta["kind"] == "general" {
                let category = text(&row, "category");
                handling::select(tx, &actor, &mut row, &category, false)?;
            }
            validation::employee(tx, &actor, &row, true)?;
            return save(tx, &actor, meta, row, "update", "");
        }
        let note = text(body, "note");
        if matches!(action.as_str(), "submit" | "withdraw") && row["ownerUserId"] != actor.id {
            return Err(error(403, "只有申请人可以提交或撤回申请。"));
        }
        if note.chars().count() > 500
            || (matches!(
                action.as_str(),
                "reject" | "cancel" | "withdraw" | "void" | "complete" | "remind"
            ) && note.is_empty())
        {
            return Err(invalid(
                "撤回、驳回、取消、作废和完成须填写说明，最多 500 字。",
            ));
        }
        if action == "remind" {
            approval::remind(&actor, &mut row)?;
            return save(tx, &actor, meta, row, "remind", &note);
        }
        let mut next = export_doc_domain::oa::next_status(
            &text(&row, "status"),
            &action,
            row["kind"] == "expense",
        )
        .ok_or_else(|| conflict("当前状态不能执行此操作，请刷新后核对。"))?;
        if matches!(action.as_str(), "approve" | "reject" | "void")
            && tx.provider() != "SQLite"
            && row["ownerUserId"] == actor.id
        {
            return Err(error(
                403,
                "不能审批自己的申请，请交由另一位有权限的同事办理。",
            ));
        }
        if matches!(action.as_str(), "submit" | "approve") {
            validation::employee(tx, &actor, &row, true)?;
            validation::fields(meta, &row, today)?;
            validation::submission(tx, &row)?;
        }
        if action == "submit" {
            if row["kind"] == "general" {
                let category = text(&row, "category");
                handling::select(
                    tx,
                    &actor,
                    &mut row,
                    &category,
                    matches!(category.as_str(), "Seal" | "Certificate"),
                )?;
            }
            approval::submit(tx, &actor, &mut row)?;
        }
        if matches!(action.as_str(), "approve" | "reject") {
            next = approval::review(tx, &actor, &mut row, &action, &note)?;
        }
        if action == "complete" {
            validation::completion(&row, today)?;
        }
        row["status"] = json!(next);
        if next != "Pending" {
            row["currentApproverId"] = json!(0);
        }
        let event = if action == "approve" && next == "Pending" {
            "approve-step"
        } else {
            &action
        };
        save(tx, &actor, meta, row, event, &note)
    })
}

fn create(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    required(body, "requestKey", "请求编号", 100)?;
    let identity = store::normalize(&serde_json::to_string(&json!([
        actor.company,
        actor.id,
        text(body, "requestKey")
    ]))?);
    let mut row = validation::fields(meta, body, today)?;
    let employee = validation::employee(tx, actor, body, false)?;
    row["employeeId"] = employee["id"].clone();
    let mut submitted_fields = row.clone();
    submitted_fields.sort_all_objects();
    let submitted = super::media::digest(&serde_json::to_vec(&submitted_fields)?);
    row["employeeName"] = employee["fullName"].clone();
    row["departmentId"] = employee["departmentId"].clone();
    row["companyScope"] = json!(actor.company);
    row["ownerUserId"] = json!(actor.id);
    if meta["kind"] == "general" {
        let category = text(&row, "category");
        handling::select(tx, actor, &mut row, &category, false)?;
    }
    access(tx, actor, meta, &row)?;
    if let Some(existing) = tx.find_identity(&kind(meta), &identity)? {
        if existing["submissionDigest"] != submitted {
            return Err(conflict("同一请求编号不能用于不同内容。"));
        }
        access(tx, actor, meta, &existing)?;
        return project(tx, actor, existing, true);
    }
    row["submissionDigest"] = json!(submitted);
    row["identity"] = json!(identity);
    row["requestKey"] = body["requestKey"].clone();
    row["kind"] = meta["kind"].clone();
    row["status"] = json!("Draft");
    save(tx, actor, meta, row, "create", "")
}

pub(super) const KINDS: &[&str] = &[
    "oa-leave",
    "oa-overtime",
    "oa-expense",
    "oa-travel",
    "oa-purchase",
    "oa-general",
];
pub(super) fn references(
    tx: &Connection,
    company: &str,
    employee: Option<i64>,
    owner: Option<i64>,
    pending_only: bool,
) -> Result<i64> {
    let mut count = 0;
    for kind in KINDS {
        for status in if pending_only {
            vec![
                Some("Draft"),
                Some("Pending"),
                Some("Approved"),
                Some("Rejected"),
            ]
        } else {
            vec![None]
        } {
            count += tx
                .query_records(&RecordQuery {
                    kind,
                    company,
                    employee,
                    owner: if employee.is_some() { None } else { owner },
                    status,
                    limit: 1,
                    ..Default::default()
                })?
                .0;
        }
    }
    Ok(count)
}
