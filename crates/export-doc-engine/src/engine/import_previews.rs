//! Single-use previews are bound to an actor and kept in the same business database.
use super::{
    error::{Result, conflict, error, invalid, unavailable},
    media::digest,
    store::{self, Actor},
};
use crate::{clock::BusinessClock, paths::nonce};
use chrono::{DateTime, Duration};
use export_doc_storage::{BlobWrite, Connection, RecordWrite};
use serde_json::{Value, json};
const KIND: &str = "import-previews";
const MAX_PAYLOAD: usize = 16 * 1024 * 1024;

pub fn save(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    kind: &str,
    rows: &[Value],
) -> Result<String> {
    let bytes = serde_json::to_vec(rows)?;
    if bytes.len() > MAX_PAYLOAD {
        return Err(invalid("导入预检内容超过 16 MiB，请拆分文件。"));
    }
    let now = clock.now().map_err(unavailable)?.utc_now;
    let mut removed = 0;
    for value in store::all(tx, KIND)? {
        let expires = DateTime::parse_from_rfc3339(value["expiresAt"].as_str().unwrap_or(""))
            .map_err(|_| unavailable("导入预检到期时间无效。"))?;
        if expires > now {
            continue;
        }
        let id = value["id"]
            .as_i64()
            .ok_or_else(|| unavailable("导入预检缺少编号。"))?;
        tx.delete_blobs(id)?;
        if !tx.delete(KIND, id, 1)? {
            return Err(conflict("导入预检已变化，请重试。"));
        }
        removed += 1;
        if removed == 200 {
            break;
        }
    }
    let identity = nonce().map_err(unavailable)?;
    let mut value = json!({"previewId":identity,"kind":kind,"ownerUserId":actor.id,"companyScope":actor.company,"departmentId":actor.department,
        "versionNumber":1,"createdAt":now.to_rfc3339(),"expiresAt":(now+Duration::minutes(30)).to_rfc3339(),
        "consumed":false,"rowCount":rows.len(),"payloadSha256":digest(&bytes)});
    let id = tx.insert(&RecordWrite {
        kind: KIND,
        identity: Some(&identity),
        body: &value,
    })?;
    value["id"] = json!(id);
    tx.set_body(id, &value)?;
    tx.insert_blob(&BlobWrite {
        kind: KIND,
        record_id: id,
        file_name: "preview.json",
        media_type: "application/json",
        digest: value["payloadSha256"].as_str().unwrap(),
        content: &bytes,
        created_at: &now.to_rfc3339(),
    })?;
    Ok(identity)
}
pub fn consume(
    tx: &Connection,
    actor: &Actor,
    clock: &BusinessClock,
    kind: &str,
    identity: &str,
) -> Result<Vec<Value>> {
    if identity.len() != 32 || !identity.bytes().all(|ch| ch.is_ascii_hexdigit()) {
        return Err(invalid("导入预检编号无效，请重新选择文件。"));
    }
    let mut preview = tx
        .find_identity(KIND, identity)?
        .filter(|value| {
            value["kind"] == kind
                && value["ownerUserId"] == actor.id
                && value["companyScope"] == actor.company
        })
        .ok_or_else(|| error(404, "导入预检不存在或不属于当前账号。"))?;
    if preview["consumed"] == true {
        return Err(conflict("该导入预检已经提交，不能重复导入。"));
    }
    let expires = DateTime::parse_from_rfc3339(preview["expiresAt"].as_str().unwrap_or(""))
        .map_err(|_| unavailable("导入预检到期时间无效。"))?;
    if expires <= clock.now().map_err(unavailable)?.utc_now {
        return Err(conflict("导入预检已过期，请重新选择文件。"));
    }
    let id = preview["id"]
        .as_i64()
        .ok_or_else(|| unavailable("导入预检缺少编号。"))?;
    let payload = tx
        .blob(id, KIND)?
        .ok_or_else(|| unavailable("导入预检内容缺失。"))?;
    if payload.content.is_empty()
        || payload.content.len() > MAX_PAYLOAD
        || preview["payloadSha256"] != digest(&payload.content)
    {
        return Err(unavailable("导入预检完整性校验失败。"));
    }
    let rows: Vec<Value> = serde_json::from_slice(&payload.content)
        .map_err(|cause| unavailable(format!("导入预检内容无效：{cause}")))?;
    if rows.len() > 5000 || preview["rowCount"] != json!(rows.len()) {
        return Err(unavailable("导入预检行数无效。"));
    }
    preview["consumed"] = json!(true);
    tx.set_body(id, &preview)?;
    Ok(rows)
}
