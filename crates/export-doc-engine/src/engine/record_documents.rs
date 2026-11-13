use super::{
    NativeService, communication,
    error::{Result, conflict, error, invalid, unavailable},
    media, oa,
    records::{self, required, text},
    store::{self, Actor},
    tasks::FileOutput,
};
use crate::engine::store::Connection;
use crate::generated_api::Operation;
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
    input: &mut (impl std::io::BufRead + std::io::Seek),
    size: u64,
) -> Result<Value> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知文档附件操作。"))?;
    if meta["action"] != "upload" {
        return Err(invalid("该操作不能上传附件。"));
    }
    let purpose = match text(body, "purpose").as_str() {
        "" | "Voucher" => "Voucher",
        "ReimbursementForm" => "ReimbursementForm",
        "Supporting" => "Supporting",
        _ => return Err(invalid("附件用途无效。")),
    };
    let classification = match text(body, "classification").as_str() {
        "" | "Internal" => "Internal",
        "Restricted" => "Restricted",
        _ => return Err(invalid("附件材料级别无效。")),
    };
    service.store.read(|tx| {
        current(tx, actor, meta, records::id(parameters)?).and_then(|(_, row)| mutable(meta, &row))
    })?;
    let (name, media_type, prepared) =
        super::document_upload::prepare(&service.store, file_name, input, size, true)?;
    let digest = prepared.object.digest.as_str();
    service.store.transaction(|tx| {
        let (actor,row)=current(tx,actor,meta,records::id(parameters)?)?;
        mutable(meta,&row)?;
        if !announcement(meta){oa::materials::attachment(tx,&actor,&row,&json!({"classification":classification}))?;}
        let kind=attachment_kind(meta);
        let (count,existing)=communication::children(tx,&row,kind, 0, crate::engine::file_store::policy::MAX_REQUEST_FILES)?;
        if let Some(existing) = existing.iter().find(|item| item["digest"]==digest) {
            if existing["fileName"] == name { return project(tx,&actor,meta,row); }
            return Err(conflict("该凭证已上传，请勿重复添加。"));
        }
        store::check_version(&row,store::expected(body))?;
        let existing_size: u64=existing.iter().map(|v| v["sizeBytes"].as_u64().unwrap_or(0)).sum();
        let policy = service.store.files.configuration()?.policy;
        policy.check_upload(size)?;
        policy.check_request(count as usize + 1, existing_size + size)?;
        let identity = format!("{}:{digest}",row["id"]);
        if !announcement(meta) && let Some(mut retained) = tx.find_identity(kind,&identity)? {
            if retained["requestId"] != 0 || retained["sourceRequestId"] != row["id"] || retained["fileName"] != name { return Err(conflict("同一原始文件已有历史引用，请保留原文件名。")); }
            oa::materials::attachment(tx,&actor,&row,&retained)?;
            retained["requestId"] = row["id"].clone();
            retained["purpose"] = json!(purpose);
            let id = records::positive(&retained,"id","附件")?;
            store::save_in_scope(tx,kind,id,retained,Some(identity),&actor,"reattach",Some(&row))?;
            return save(tx,&actor,meta,row,"upload",&name);
        }
        let record=store::save_in_scope(tx,kind,0,json!({"requestId":row["id"],"sourceRequestId":row["id"],"fileName":name,"mediaType":media_type,"sizeBytes":size,"digest":digest,"purpose":purpose,"classification":classification}),Some(identity),&actor,"upload",Some(&row))?;
        tx.publish_file(&prepared,kind,records::positive(&record,"id","附件")?,&name,media_type)?;
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
    if (value["requestId"] != row["id"] && value["sourceRequestId"] != row["id"])
        || value["companyScope"] != row["companyScope"]
    {
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
    if value["requestId"] != row["id"] {
        return Err(conflict("附件已从当前草稿移除。"));
    }
    if !announcement(meta) && value["retained"] == true {
        let mut retained = value.clone();
        retained["requestId"] = json!(0);
        store::save_in_scope(
            tx,
            attachment_kind(meta),
            id,
            retained,
            Some(format!("{}:{}", row["id"], text(&value, "digest"))),
            actor,
            "detach-retained",
            Some(&row),
        )?;
    } else {
        tx.delete_blobs(id)?;
        if !tx.delete(attachment_kind(meta), id, store::expected(&value))? {
            return Err(conflict("附件已被其他人修改。"));
        }
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
    let info = download_info(service, actor, operation, parameters)?;
    let output = service.store.files.load(&info, media::DOCUMENT_LIMIT)?;
    if download_info(service, actor, operation, parameters)? != info {
        return Err(super::error::conflict("附件已变化，请重新读取。"));
    }
    Ok(output)
}
pub(in crate::engine) fn download_info(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<export_doc_storage::FileInfo> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知附件下载。"))?;
    let info = service.store.read(|tx| {
        let (current_actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let attachment = attachment(tx, meta, &row, parameters)?;
        if !announcement(meta) {
            oa::materials::attachment(tx, &current_actor, &row, &attachment)?;
        }
        let blob = tx
            .file_info(
                records::positive(&attachment, "id", "附件")?,
                attachment_kind(meta),
            )?
            .ok_or_else(|| unavailable("附件内容缺失。"))?;
        if attachment["digest"] != blob.object.digest {
            return Err(unavailable("附件摘要校验失败。"));
        }
        Ok(blob)
    })?;
    Ok(info)
}
