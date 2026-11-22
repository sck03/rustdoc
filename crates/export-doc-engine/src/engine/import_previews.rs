//! Single-use previews are bound to an actor and kept in the same business database.
use super::{
    error::{Result, conflict, error, invalid, unavailable},
    media::digest,
    store::{self, Actor},
};
use crate::engine::store::Connection;
use crate::{clock::BusinessClock, paths::nonce};
use chrono::{DateTime, Duration};
use export_doc_storage::RecordWrite;
use serde_json::{Value, json};
const KIND: &str = "import-previews";
const GLOBAL_BYTES: u64 = 256 * 1024 * 1024;
const USER_BYTES: u64 = 128 * 1024 * 1024;
#[cfg(test)]
mod tests;
fn limits(kind: &str) -> (usize, usize) {
    if kind == "hs-tariffs" {
        (50_000, 64 * 1024 * 1024)
    } else {
        (5_000, 16 * 1024 * 1024)
    }
}

#[cfg(any(feature = "excel", test))]
pub fn save(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    kind: &str,
    rows: &[Value],
) -> Result<String> {
    save_prepared(tx, actor, clock, &prepare(kind, rows)?)
}

pub struct PreparedPreview<'a> {
    kind: &'a str,
    row_count: usize,
    bytes: Vec<u8>,
    digest: String,
}

/// Expensive bounded serialization can finish before acquiring the write transaction.
pub fn prepare<'a>(kind: &'a str, rows: &[Value]) -> Result<PreparedPreview<'a>> {
    let (max_rows, max_payload) = limits(kind);
    if rows.iter().any(|row| !row.is_object()) {
        return Err(invalid("导入预检行必须是记录对象。"));
    }
    if rows.len() > max_rows {
        return Err(invalid(format!(
            "预检内容超过 {max_rows} 行或 {} MiB，请缩小范围。",
            max_payload / 1024 / 1024
        )));
    }
    let mut output = crate::bounded_bytes::BoundedBytes {
        bytes: vec![],
        limit: max_payload,
    };
    serde_json::to_writer(&mut output, rows).map_err(|cause| {
        if cause.is_io() {
            invalid("预检内容超过容量上限，请缩小范围。")
        } else {
            cause.into()
        }
    })?;
    let bytes = output.bytes;
    Ok(PreparedPreview {
        kind,
        row_count: rows.len(),
        digest: digest(&bytes),
        bytes,
    })
}

pub fn save_prepared(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    prepared: &PreparedPreview<'_>,
) -> Result<String> {
    let PreparedPreview {
        kind,
        row_count,
        bytes,
        digest,
    } = prepared;
    let now = clock.now().map_err(unavailable)?.utc_now;
    loop {
        crate::operation::check()?;
        let expired = tx.expired_import_previews(&now.to_rfc3339())?;
        if expired.is_empty() {
            break;
        }
        for value in expired {
            let id = value["id"]
                .as_i64()
                .ok_or_else(|| unavailable("导入预检缺少编号。"))?;
            if !tx.delete(KIND, id, store::expected(&value))? {
                return Err(conflict("导入预检已变化，请重试。"));
            }
        }
    }
    let usage = tx.import_preview_usage(actor.id)?;
    if usage.count >= 2000
        || usage.user_count >= 200
        || bytes.len() as u64 > GLOBAL_BYTES.saturating_sub(usage.bytes)
        || bytes.len() as u64 > USER_BYTES.saturating_sub(usage.user_bytes)
    {
        return Err(error(
            429,
            "导入预检暂存容量已满，请提交已有预检或待其过期后重试。",
        ));
    }
    let identity = nonce().map_err(unavailable)?;
    let mut value = json!({"previewId":identity,"kind":kind,"ownerUserId":actor.id,"companyScope":actor.company,"departmentId":actor.department,
        "versionNumber":1,"createdAt":now.to_rfc3339(),"expiresAt":(now+Duration::minutes(30)).to_rfc3339(),
        "consumed":false,"rowCount":row_count,"payloadSha256":digest});
    let id = tx.insert(&RecordWrite {
        kind: KIND,
        identity: Some(&identity),
        body: &value,
    })?;
    value["id"] = json!(id);
    tx.set_body(id, &value)?;
    tx.insert_import_preview_rows(
        id,
        std::str::from_utf8(bytes).map_err(|_| unavailable("导入预检编码无效。"))?,
    )?;
    Ok(identity)
}
fn authorized(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    kind: &str,
    identity: &str,
) -> Result<Value> {
    if identity.len() != 32 || !identity.bytes().all(|ch| ch.is_ascii_hexdigit()) {
        return Err(invalid("导入预检编号无效，请重新选择文件。"));
    }
    let preview = tx
        .find_identity(KIND, identity)?
        .filter(|value| {
            value["kind"] == kind
                && value["ownerUserId"] == actor.id
                && value["companyScope"] == actor.company
        })
        .ok_or_else(|| error(404, "导入预检不存在或不属于当前账号。"))?;
    if preview["consumed"]
        .as_bool()
        .ok_or_else(|| unavailable("导入预检状态无效。"))?
    {
        return Err(conflict("该导入预检已经提交，不能重复导入。"));
    }
    let expires = DateTime::parse_from_rfc3339(preview["expiresAt"].as_str().unwrap_or(""))
        .map_err(|_| unavailable("导入预检到期时间无效。"))?;
    if expires <= clock.now().map_err(unavailable)?.utc_now {
        return Err(conflict("导入预检已过期，请重新选择文件。"));
    }
    Ok(preview)
}

pub fn page(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    kind: &str,
    identity: &str,
    change: &str,
    query: &[(&str, String)],
) -> Result<Value> {
    let preview = authorized(tx, actor, clock, kind, identity)?;
    let id = preview["id"]
        .as_i64()
        .ok_or_else(|| unavailable("导入预检缺少编号。"))?;
    for (key, max) in [("pageNumber", i32::MAX as u64), ("pageSize", 200)] {
        if let Some(value) = super::request_values::find(query, key)
            && value
                .parse::<u64>()
                .ok()
                .filter(|value| (1..=max).contains(value))
                .is_none()
        {
            return Err(invalid("预检页码或每页条数无效。"));
        }
    }
    let (page, size, offset) = store::page_parameters(query);
    let total = tx.import_preview_row_count(id, change)?;
    let rows = tx.import_preview_rows(id, change, offset, size as i64)?;
    Ok(crate::contracts::page(rows, total as usize, page, size))
}

pub fn consume(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    kind: &str,
    identity: &str,
) -> Result<Vec<Value>> {
    let mut preview = authorized(tx, actor, clock, kind, identity)?;
    let id = preview["id"]
        .as_i64()
        .ok_or_else(|| unavailable("导入预检缺少编号。"))?;
    let expected = preview["rowCount"]
        .as_u64()
        .filter(|n| *n <= limits(kind).0 as u64)
        .ok_or_else(|| unavailable("导入预检行数无效。"))?;
    let mut rows = Vec::new();
    loop {
        crate::operation::check()?;
        let batch = tx.import_preview_rows(id, "", rows.len() as i64, 200)?;
        if batch.is_empty() {
            break;
        }
        if rows.len() + batch.len() > expected as usize {
            return Err(unavailable("导入预检行数无效。"));
        }
        rows.extend(batch);
    }
    if rows.len() != expected as usize {
        return Err(unavailable("导入预检行数无效。"));
    }
    let checked = prepare(kind, &rows).map_err(|_| unavailable("导入预检内容无效。"))?;
    if preview["payloadSha256"] != checked.digest {
        return Err(unavailable("导入预检完整性校验失败。"));
    }
    preview["consumed"] = json!(true);
    tx.set_body(id, &preview)?;
    tx.delete_import_preview_rows(id)?;
    Ok(rows)
}
