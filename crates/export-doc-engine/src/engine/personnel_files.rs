//! Private documents share the personnel record's version and authorization.
use super::{
    auth,
    error::{Result, conflict, error, invalid},
    media, personnel, personnel_queries, records,
    store::{self, Actor, Store},
    tasks::FileOutput,
};
use crate::generated_api::*;
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    LIST_PERSONNEL_ATTACHMENTS,
    UPLOAD_PERSONNEL_ATTACHMENT,
    DOWNLOAD_PERSONNEL_ATTACHMENT,
    DELETE_PERSONNEL_ATTACHMENT,
];

fn mutable(row: &Value, actor: &Actor) -> Result<()> {
    if row["status"] == "Departed" && !actor.admin {
        return Err(conflict("离职档案的附件须由管理员修正。"));
    }
    Ok(())
}
fn attachment(
    tx: &store::Connection,
    row: &Value,
    parameters: &[(&str, String)],
) -> Result<export_doc_storage::FileInfo> {
    let id = crate::engine::request_values::find(parameters, "attachmentId")
        .ok_or_else(|| invalid("缺少附件编号。"))?;
    if id.len() != 64 || !id.bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(invalid("附件编号无效。"));
    }
    tx.file_info(
        records::positive(row, "id", "人员")?,
        &format!("personnel-file:{id}"),
    )?
    .ok_or_else(|| error(404, "人员附件不存在。"))
}
pub(super) fn page(tx: &store::Connection, id: i64, after: &str) -> Result<Value> {
    if !after.is_empty() && (after.len() != 64 || !after.bytes().all(|c| c.is_ascii_hexdigit())) {
        return Err(invalid("附件分页游标无效。"));
    }
    let (total, mut files) = tx.file_page(id, "personnel-file:", after, 51)?;
    let more = files.len() > 50;
    files.truncate(50);
    let next = if more {
        files.last().map(|f| f.object.digest.clone())
    } else {
        None
    };
    let items: Vec<_> = files.into_iter().map(|file| json!({"id":file.object.digest,"fileName":file.file_name,"mediaType":file.media_type,"sizeBytes":file.object.size,"digest":file.object.digest})).collect();
    Ok(json!({"totalCount":total,"items":items,"nextCursor":next}))
}
pub fn list(store: &Store, actor: &Actor, id: i64, query: &[(&str, String)]) -> Result<Value> {
    store.read_for(actor, LIST_PERSONNEL_ATTACHMENTS, query, |tx, actor| {
        personnel::person(tx, actor, id, "view-details")?;
        page(
            tx,
            id,
            query
                .iter()
                .find(|(k, _)| *k == "after")
                .map(|(_, v)| v.as_str())
                .unwrap_or(""),
        )
    })
}
pub fn upload(
    store: &Store,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
    file_name: &str,
    input: &mut (impl std::io::BufRead + std::io::Seek),
    size: u64,
) -> Result<Value> {
    let id = records::id(parameters)?;
    store.read(|tx| personnel::person(tx, actor, id, "edit"))?;
    let (name, media_type, prepared) =
        super::document_upload::prepare(store, file_name, input, size, true)?;
    let digest = prepared.object.digest.as_str();
    store.transaction_as(actor, |tx, actor| {
        let row = personnel::person(tx, actor, id, "edit")?;
        mutable(&row, actor)?;
        if let Some(existing) = tx.file_info(id, &format!("personnel-file:{digest}"))? {
            if existing.file_name == name {
                return personnel_queries::detail(tx, actor, row);
            }
            return Err(conflict("相同内容的附件已经存在。"));
        }
        store::check_version(&row, store::expected(body))?;
        let policy = store.files.configuration()?.policy;
        policy.check_upload(size)?;
        tx.publish_file(
            &prepared,
            &format!("personnel-file:{digest}"),
            id,
            &name,
            media_type,
        )?;
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
        let row = personnel::person(tx, actor, id, "edit")?;
        mutable(&row, actor)?;
        store::check_version(&row, store::expected(body))?;
        let file = attachment(tx, &row, parameters)?;
        tx.delete_files(id, Some(&format!("personnel-file:{}", file.object.digest)))?;
        personnel::save(
            tx,
            actor,
            id,
            row,
            "attachment-delete",
            &format!("{}：{}", file.file_name, records::text(body, "note")),
        )
    })
}
pub fn download(store: &Store, actor: &Actor, parameters: &[(&str, String)]) -> Result<FileOutput> {
    let info = download_info(store, actor, parameters)?;
    let output = store.files.load(&info, media::DOCUMENT_LIMIT)?;
    if download_info(store, actor, parameters)? != info {
        return Err(super::error::conflict("人员附件已变化，请重新读取。"));
    }
    Ok(output)
}
pub fn download_info(
    store: &Store,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<export_doc_storage::FileInfo> {
    let id = records::id(parameters)?;
    let info = store.read_for(actor, DOWNLOAD_PERSONNEL_ATTACHMENT, &[], |tx, actor| {
        auth::authorize(actor, "office.people", "view-details")?;
        let row = personnel::person(tx, actor, id, "view-details")?;
        attachment(tx, &row, parameters)
    })?;
    Ok(info)
}
