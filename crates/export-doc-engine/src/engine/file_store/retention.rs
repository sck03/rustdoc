//! Explicit archival review. Active references and holds cannot be approved away.
use super::*;
use crate::{
    engine::{
        NativeService, auth,
        store::{Actor, Connection},
    },
    generated_api::*,
};
use export_doc_storage::AuditWrite;
use serde_json::{Value, json};
mod review;
const PLAN: &str = "file-retention-review";
fn text<'a>(body: &'a Value, key: &str) -> &'a str {
    body[key].as_str().unwrap_or("")
}
pub(super) fn archive_blocker(row: &Value) -> Option<&'static str> {
    if row["activeReferences"].as_u64().unwrap_or(0) != 0 {
        Some("该文件仍被业务记录使用，请结束业务引用后再迁移档案。")
    } else if row["basisDate"].as_str().unwrap_or("").is_empty() {
        Some("请先确认所选资料的档案类别和归档日期。")
    } else {
        None
    }
}
fn project(today: chrono::NaiveDate, mut row: Value) -> Result<Value> {
    let expiry = export_doc_domain::retention::expires_on(
        row["basisDate"].as_str(),
        row["days"].as_u64().unwrap_or(0) as u32,
    )
    .map_err(invalid)?;
    row["basisDate"] = json!(row["basisDate"].as_str().unwrap_or(""));
    row["held"] = json!(row["held"] == 1);
    row["expiresOn"] = json!(expiry.map(|v| v.to_string()).unwrap_or_default());
    let blocker = archive_blocker(&row);
    row["canArchive"] = json!(blocker.is_none());
    row["archiveBlocker"] = json!(blocker.unwrap_or(""));
    row["eligible"] = json!(
        expiry.is_some_and(|date| date <= today)
            && row["held"] == false
            && row["activeReferences"] == 0
    );
    Ok(row)
}
pub(in crate::engine) fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    if operation == QUERY_FILE_RETENTION {
        return service.store.read(|tx| {
            let actor = auth::refresh_actor_in(tx, actor)?;
            authorize(&actor, operation)?;
            query(tx, &actor, body)
        });
    }
    service.store.transaction_as(actor, |tx, actor| {
        authorize(actor, operation)?;
        let today = crate::engine::units::today(tx)?;
        let revision = body["expectedVersion"].as_i64().unwrap_or(-1);
        let reason = text(body, "reason").trim();
        let result = match operation {
            SAVE_RETENTION_POLICY => {
                tx.save_retention_policy(text(body, "archiveClass"), body["days"].as_i64().unwrap_or(0), revision)?;
                json!({"message":"保留政策已保存。0 天表示长期保留；到期仍需独立复核。","count":0,"bytes":0})
            }
            CLASSIFY_RETAINED_FILE => {
                let basis = text(body, "basisDate");
                let date = chrono::NaiveDate::parse_from_str(basis, "%Y-%m-%d")
                    .map_err(|_| invalid("请填写有效的归档或结案日期。"))?;
                if basis.len() != 10 || date > today { return Err(invalid("归档依据须为已发生的业务日期。")); }
                tx.classify_retained_file(text(body, "id"), text(body, "archiveClass"), basis, revision)?;
                json!({"message":"档案类别和依据日期已登记。","count":1,"bytes":0})
            }
            HOLD_RETAINED_FILE => {
                tx.hold_retained_file(text(body, "digest"), body["active"] == true, reason, revision)?;
                json!({"message":"暂停保留状态已更新。","count":1,"bytes":0})
            }
            PREVIEW_RETENTION_DELETION => review::preview(tx, actor, body, today)?,
            APPROVE_RETENTION_DELETION => review::approve(tx, actor, body, today)?,
            _ => return Err(invalid("档案保留操作无效。")),
        };
        tx.append_audit_details(&AuditWrite {
            kind: "file-retention", record_id: 0, version: revision.max(0) + 1,
            action: operation.id, actor_id: actor.id,
            occurred_at: &crate::engine::store::timestamp(), note: reason,
        }, &json!({"request":body,"result":result}))?;
        Ok(result)
    })
}

fn authorize(actor: &Actor, operation: Operation) -> Result<()> {
    if !actor.admin {
        return Err(crate::engine::error::error(403, "档案保留仅限系统管理员。"));
    }
    auth::authorize_operation(actor, operation, &[])
}

fn public_plan(plan: &Value, actor: &Actor) -> Result<Value> {
    let mut result = plan["summary"].clone();
    result["reason"] = plan["reason"].clone();
    result["items"] = plan["rows"].clone();
    let expires = chrono::DateTime::parse_from_rfc3339(text(&result, "expiresAt"))
        .map_err(|_| invalid("处置计划期限无效，请重新预检。"))?;
    let blocker = if result["requestedBy"] == actor.id {
        "申请人不能自行批准，请由另一名系统管理员复核。"
    } else if expires < chrono::Utc::now() {
        "处置预检已过期，请重新预检。"
    } else {
        ""
    };
    result["canApprove"] = json!(blocker.is_empty());
    result["approvalBlocker"] = json!(blocker);
    Ok(result)
}

fn query(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    let today = crate::engine::units::today(tx)?;
    let mut rows = tx
        .retained_files(text(body, "after"), 101)?
        .as_array()
        .cloned()
        .unwrap_or_default();
    let more = rows.len() > 100;
    rows.truncate(100);
    let next = if more {
        rows.last()
            .and_then(|row| row["id"].as_str())
            .unwrap_or("")
            .to_owned()
    } else {
        String::new()
    };
    let rows = rows
        .into_iter()
        .map(|row| project(today, row))
        .collect::<Result<Vec<_>>>()?;
    let mut result = json!({"items":rows,"nextCursor":next});
    if let Some(plan) = tx.settings(PLAN)?
        && plan["state"] == "Ready"
    {
        result["plan"] = public_plan(&plan, actor)?;
    }
    Ok(result)
}

pub(in crate::engine) fn download_info(
    store: &crate::engine::store::Store,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<export_doc_storage::FileInfo> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        if !actor.admin {
            return Err(crate::engine::error::error(403, "档案取回仅限系统管理员。"));
        }
        let id = crate::engine::request_values::find(parameters, "id").unwrap_or("");
        let row = tx.retained_file(id)?.ok_or_else(|| {
            crate::engine::error::error(404, "归档引用已不存在，请刷新档案列表。")
        })?;
        Ok(export_doc_storage::FileInfo {
            file_name: text(&row, "fileName").into(),
            media_type: "application/octet-stream".into(),
            object: export_doc_storage::FileObject {
                digest: text(&row, "digest").into(),
                archive_identity: text(&row, "archiveIdentity").into(),
                size: row["size"]
                    .as_u64()
                    .ok_or_else(|| invalid("档案容量记录无效。"))?,
            },
        })
    })
}
