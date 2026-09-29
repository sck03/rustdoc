//! Private documents share the personnel record's version and authorization.
use super::{
    auth,
    error::{Result, conflict, error, invalid, unavailable},
    media, personnel, personnel_queries, records,
    store::{self, Actor, Store},
    tasks::FileOutput,
};
use crate::generated_api::*;
use export_doc_storage::BlobWrite;
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    UPLOAD_PERSONNEL_ATTACHMENT,
    DOWNLOAD_PERSONNEL_ATTACHMENT,
    DELETE_PERSONNEL_ATTACHMENT,
];

fn documents(row: &Value) -> Result<Vec<Value>> {
    match row.get("attachments") {
        None => Ok(vec![]),
        Some(Value::Array(values)) => Ok(values.clone()),
        _ => Err(unavailable("人员附件记录损坏。")),
    }
}
fn mutable(row: &Value) -> Result<()> {
    if row["status"] == "Departed" {
        return Err(conflict("离职档案的附件不能修改。"));
    }
    Ok(())
}
fn attachment(row: &Value, parameters: &[(&str, String)]) -> Result<Value> {
    let id = parameters
        .iter()
        .find(|(key, _)| *key == "attachmentId")
        .map(|(_, id)| id.as_str())
        .ok_or_else(|| invalid("缺少附件编号。"))?;
    documents(row)?
        .into_iter()
        .find(|file| file["id"] == id)
        .ok_or_else(|| error(404, "人员附件不存在。"))
}
pub fn upload(
    store: &Store,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
    file_name: &str,
    bytes: &[u8],
) -> Result<Value> {
    let (name, media_type) = media::document_type(file_name, bytes)?;
    let digest = media::digest(bytes);
    let id = records::id(parameters)?;
    store.transaction_as(actor, |tx, actor| {
        let mut row = personnel::person(tx, actor, id, "edit")?;
        mutable(&row)?;
        let mut files = documents(&row)?;
        if let Some(existing) = files.iter().find(|file| file["digest"] == digest) {
            if existing["fileName"] == name { return personnel_queries::detail(tx, actor, row); }
            return Err(conflict("相同内容的附件已经存在。"));
        }
        store::check_version(&row, store::expected(body))?;
        let size: u64 = files.iter().map(|file| file["sizeBytes"].as_u64().unwrap_or(0)).sum();
        if files.len() >= 20 || size + bytes.len() as u64 > 50 * 1024 * 1024 {
            return Err(invalid("每份档案最多 20 个附件、合计 50 MiB。"));
        }
        files.push(json!({"id":digest,"fileName":name,"mediaType":media_type,"sizeBytes":bytes.len(),"digest":digest}));
        row["attachments"] = json!(files);
        tx.insert_blob(&BlobWrite { kind:&format!("personnel-file:{digest}"), record_id:id, file_name:&name,
            media_type, digest:&digest, content:bytes, created_at:&store::timestamp() })?;
        personnel::save(tx, actor, id, row, "attachment-upload", &name)
    })
}
pub fn remove(
    store: &Store,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let id = records::id(parameters)?;
    records::required(body, "note", "移除说明", 500)?;
    store.transaction_as(actor, |tx, actor| {
        let mut row = personnel::person(tx, actor, id, "edit")?;
        mutable(&row)?;
        store::check_version(&row, store::expected(body))?;
        let file = attachment(&row, parameters)?;
        let files: Vec<_> = documents(&row)?
            .into_iter()
            .filter(|other| other["id"] != file["id"])
            .collect();
        tx.delete_blob(
            id,
            &format!("personnel-file:{}", records::text(&file, "digest")),
        )?;
        row["attachments"] = json!(files);
        personnel::save(
            tx,
            actor,
            id,
            row,
            "attachment-delete",
            &format!(
                "{}：{}",
                records::text(&file, "fileName"),
                records::text(body, "note")
            ),
        )
    })
}
pub fn download(store: &Store, actor: &Actor, parameters: &[(&str, String)]) -> Result<FileOutput> {
    let id = records::id(parameters)?;
    store.transaction_as(actor, |tx, actor| {
        auth::authorize(actor, "office.people", "view-details")?;
        let row = personnel::person(tx, actor, id, "view-details")?;
        let file = attachment(&row, parameters)?;
        let blob = tx
            .blob(
                id,
                &format!("personnel-file:{}", records::text(&file, "digest")),
            )?
            .ok_or_else(|| unavailable("人员附件内容缺失。"))?;
        if media::digest(&blob.content) != blob.digest || file["digest"] != blob.digest {
            return Err(unavailable("人员附件摘要校验失败。"));
        }
        Ok(FileOutput {
            file_name: blob.file_name,
            media_type: blob.media_type,
            content: blob.content,
        })
    })
}
