//! Company announcements and personal, transactional OA notifications.
mod announcements;
mod notifications;
use super::{
    NativeService, auth,
    error::{Result, conflict, error, invalid},
    records::{self, required, text},
    store::{self, Actor},
};
use crate::{contracts, generated_api::Operation};
pub(super) use announcements::{current, mutable, project, save};
use export_doc_storage::{CommunicationQuery, CommunicationView, Connection, RecordQuery};
pub(super) use notifications::on_event;

pub(super) fn check_references(tx: &Connection, kind: &str, record: &Value) -> Result<()> {
    if !matches!(kind, "users" | "companies" | "departments") {
        return Ok(());
    }
    let company = text(
        record,
        if kind == "companies" {
            "code"
        } else if kind == "departments" {
            "companyCode"
        } else {
            "companyScope"
        },
    );
    for child in [
        "announcement",
        "announcement-publication",
        "announcement-receipt",
        "site-notification",
    ] {
        let mut offset = 0;
        loop {
            crate::operation::check()?;
            let (_, rows) = tx.query_records(&RecordQuery {
                kind: child,
                company: &company,
                owner: if kind == "users" {
                    record["id"].as_i64()
                } else {
                    None
                },
                offset,
                limit: 100,
                ..Default::default()
            })?;
            if rows.is_empty() {
                break;
            }
            offset += rows.len() as i64;
            if rows.iter().any(|r| {
                kind != "departments"
                    || r["departmentId"] == record["code"]
                    || r["audienceDepartment"] == record["code"]
            }) {
                return Err(conflict(
                    "已有公告或通知历史引用，不能删除；可停用并保留历史。",
                ));
            }
        }
    }
    Ok(())
}
use serde_json::{Value, json};

pub fn metadata(op: Operation) -> Option<&'static Value> {
    contracts::contract()["operations"][op.id]
        .get("communication")
        .filter(|m| m.is_object())
}
pub fn is_download(op: Operation) -> bool {
    metadata(op).is_some_and(|m| m["action"] == "download")
}
pub(super) fn handle(
    service: &NativeService,
    actor: &Actor,
    op: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let meta = metadata(op).ok_or_else(|| invalid("未知公告通知操作。"))?;
    service.store.transaction_as(actor, |tx, actor| {
        auth::authorize(actor, &text(meta, "resource"), &text(meta, "permission"))?;
        if meta["kind"] == "notification" {
            notifications::handle(tx, actor, meta, parameters, query)
        } else {
            announcements::handle(tx, actor, meta, parameters, query, body)
        }
    })
}
pub(super) fn paging(query: &[(&str, String)]) -> Result<(i64, i64)> {
    super::oa::paging(query)
}
fn unread(query: &[(&str, String)]) -> Result<bool> {
    match query
        .iter()
        .find(|(k, _)| *k == "unreadOnly")
        .map(|(_, v)| v.as_str())
    {
        None | Some("false") => Ok(false),
        Some("true") => Ok(true),
        _ => Err(invalid("未读筛选参数无效。")),
    }
}
fn page(items: Vec<Value>, total: i64, page: i64, size: i64) -> Value {
    json!({"items":items,"totalCount":total,"pageNumber":page,"pageSize":size})
}
pub(super) fn children(
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
