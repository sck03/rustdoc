use super::{
    NativeService, communication,
    error::{Result, conflict, error, invalid, unavailable},
    media, oa,
    records::{self, required, text},
    store::{self, Actor},
    tasks::FileOutput,
};
use crate::generated_api::Operation;
use export_doc_storage::BlobWrite;
use export_doc_storage::Connection;
use serde_json::{Value, json};

fn metadata(op: Operation) -> Option<&'static Value> {
    communication::metadata(op).or_else(|| oa::metadata(op))
}
fn announcement(meta: &Value) -> bool {
    meta["kind"] == "announcement"
}
fn attachment_kind(meta: &Value) -> &'static str {
    if announcement(meta) {
        "announcement-attachment"
    } else {
        "oa-attachment"
    }
}
fn current(tx: &Connection, actor: &Actor, meta: &Value, id: i64) -> Result<(Actor, Value)> {
    if announcement(meta) {
        communication::current(tx, actor, meta, id)
    } else {
        oa::current(tx, actor, meta, id)
    }
}
fn mutable(meta: &Value, row: &Value) -> Result<()> {
    if announcement(meta) {
        communication::mutable(row)
    } else {
        oa::mutable(row)
    }
}
fn project(tx: &Connection, actor: &Actor, meta: &Value, row: Value) -> Result<Value> {
    if announcement(meta) {
        communication::project(tx, actor, row, true)
    } else {
        oa::project(tx, actor, row, true)
    }
}
fn save(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    row: Value,
    action: &str,
    note: &str,
) -> Result<Value> {
    if announcement(meta) {
        communication::save(tx, actor, row, action, note)
    } else {
        oa::save(tx, actor, meta, row, action, note)
    }
}

pub(in crate::engine) fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
    file_name: &str,
    bytes: &[u8],
) -> Result<Value> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知文档附件操作。"))?;
    if meta["action"] != "upload" {
        return Err(invalid("该操作不能上传附件。"));
    }
    let (name, media_type) = media::document_type(file_name, bytes)?;
    let digest = media::digest(bytes);
    service.store.transaction(|tx| {
        let (actor,row)=current(tx,actor,meta,records::id(parameters)?)?;
        mutable(meta,&row)?;
        let kind=attachment_kind(meta);
        let (count,existing)=communication::children(tx,&row,kind,0,20)?;
        if let Some(existing) = existing.iter().find(|item| item["digest"]==digest) {
            if existing["fileName"] == name { return project(tx,&actor,meta,row); }
            return Err(conflict("该凭证已上传，请勿重复添加。"));
        }
        store::check_version(&row,store::expected(body))?;
        let size: u64=existing.iter().map(|v| v["sizeBytes"].as_u64().unwrap_or(0)).sum();
        if count>=20 || size+bytes.len() as u64>50*1024*1024 { return Err(invalid("每份记录最多 20 个附件、合计 50 MiB。")); }
        let record=store::save_in_scope(tx,kind,0,json!({"requestId":row["id"],"fileName":name,"mediaType":media_type,"sizeBytes":bytes.len(),"digest":digest}),Some(format!("{}:{digest}",row["id"])),&actor,"upload",Some(&row))?;
        tx.insert_blob(&BlobWrite {kind,record_id:records::positive(&record,"id","附件")?,file_name:&name,media_type,digest:&digest,content:bytes,created_at:&store::timestamp()})?;
        save(tx,&actor,meta,row,"upload",&name)
    })
}

fn attachment(
    tx: &Connection,
    meta: &Value,
    row: &Value,
    parameters: &[(&str, String)],
) -> Result<Value> {
    let id = parameters
        .iter()
        .find(|(k, _)| *k == "attachmentId")
        .and_then(|(_, v)| v.parse::<i64>().ok())
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("附件编号无效。"))?;
    let value = store::get(tx, attachment_kind(meta), id)?;
    if value["requestId"] != row["id"] || value["companyScope"] != row["companyScope"] {
        return Err(error(404, "文档附件不存在。"));
    }
    Ok(value)
}

pub(super) fn remove(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    row: Value,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    mutable(meta, &row)?;
    let value = attachment(tx, meta, &row, parameters)?;
    required(body, "note", "删除原因", 500)?;
    let id = records::positive(&value, "id", "附件")?;
    tx.delete_blobs(id)?;
    if !tx.delete(attachment_kind(meta), id, store::expected(&value))? {
        return Err(conflict("附件已被其他人修改。"));
    }
    save(
        tx,
        actor,
        meta,
        row,
        "delete-attachment",
        &format!("{}：{}", text(&value, "fileName"), text(body, "note")),
    )
}

pub(in crate::engine) fn download(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<FileOutput> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知附件下载。"))?;
    service.store.transaction(|tx| {
        let (_, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let attachment = attachment(tx, meta, &row, parameters)?;
        let blob = tx
            .blob(
                records::positive(&attachment, "id", "附件")?,
                attachment_kind(meta),
            )?
            .ok_or_else(|| unavailable("附件内容缺失。"))?;
        if media::digest(&blob.content) != blob.digest || attachment["digest"] != blob.digest {
            return Err(unavailable("附件摘要校验失败。"));
        }
        Ok(FileOutput {
            file_name: blob.file_name,
            media_type: blob.media_type,
            content: blob.content,
        })
    })
}
