use super::super::{media, tasks::FileOutput};
use super::*;
use export_doc_storage::BlobWrite;
use unicode_normalization::UnicodeNormalization;
const FILE_LIMIT: usize = 10 * 1024 * 1024;

pub(in crate::engine) fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
    file_name: &str,
    bytes: &[u8],
) -> Result<Value> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知申请附件操作。"))?;
    if meta["action"] != "upload" {
        return Err(invalid("该操作不能上传附件。"));
    }
    let name = file_name.nfc().collect::<String>();
    if !crate::paths::valid_file_name(&name) || bytes.is_empty() || bytes.len() > FILE_LIMIT {
        return Err(invalid("文件名无效，或文件超过 10 MiB。"));
    }
    let pdf = bytes.starts_with(b"%PDF-")
        && bytes[bytes.len().saturating_sub(1024)..]
            .windows(5)
            .any(|w| w == b"%%EOF");
    let media_type = if pdf {
        "application/pdf"
    } else {
        media::image_type(bytes, FILE_LIMIT)?
    };
    let extension = std::path::Path::new(&name)
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if !match media_type {
        "application/pdf" => extension == "pdf",
        "image/png" => extension == "png",
        _ => matches!(extension.as_str(), "jpg" | "jpeg"),
    } {
        return Err(invalid(
            "附件扩展名与实际类型不一致，只支持 PDF、PNG、JPEG。",
        ));
    }
    let digest = media::digest(bytes);
    service.store.transaction(|tx| {
        let (actor,row)=current(tx,actor,meta,records::id(parameters)?)?;
        store::check_version(&row,store::expected(body))?; mutable(&row)?;
        let (count,existing)=children(tx,&row,"oa-attachment",0,20)?;
        if existing.iter().any(|item| item["digest"]==digest) { return Err(conflict("该凭证已上传，请勿重复添加。")); }
        let size: u64=existing.iter().map(|v| v["sizeBytes"].as_u64().unwrap_or(0)).sum();
        if count>=20 || size+bytes.len() as u64>50*1024*1024 { return Err(invalid("每份申请最多 20 个附件、合计 50 MiB。")); }
        let record=store::save_in_scope(tx,"oa-attachment",0,json!({"requestId":row["id"],"fileName":name,"mediaType":media_type,"sizeBytes":bytes.len(),"digest":digest}),Some(format!("{}:{digest}",row["id"])),&actor,"upload",Some(&row))?;
        tx.insert_blob(&BlobWrite {kind:"oa-attachment",record_id:records::positive(&record,"id","附件")?,file_name:&name,media_type,digest:&digest,content:bytes,created_at:&store::timestamp()})?;
        save(tx,&actor,meta,row,"upload",&name)
    })
}

fn attachment(tx: &Connection, row: &Value, parameters: &[(&str, String)]) -> Result<Value> {
    let id = parameters
        .iter()
        .find(|(k, _)| *k == "attachmentId")
        .and_then(|(_, v)| v.parse::<i64>().ok())
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("附件编号无效。"))?;
    let value = store::get(tx, "oa-attachment", id)?;
    if value["requestId"] != row["id"] || value["companyScope"] != row["companyScope"] {
        return Err(error(404, "申请附件不存在。"));
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
    mutable(&row)?;
    let value = attachment(tx, &row, parameters)?;
    required(body, "note", "删除原因", 500)?;
    let id = records::positive(&value, "id", "附件")?;
    tx.delete_blobs(id)?;
    if !tx.delete("oa-attachment", id, store::expected(&value))? {
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
        let attachment = attachment(tx, &row, parameters)?;
        let blob = tx
            .blob(
                records::positive(&attachment, "id", "附件")?,
                "oa-attachment",
            )?
            .ok_or_else(|| super::super::error::unavailable("附件内容缺失。"))?;
        if media::digest(&blob.content) != blob.digest || attachment["digest"] != blob.digest {
            return Err(super::super::error::unavailable("附件摘要校验失败。"));
        }
        Ok(FileOutput {
            file_name: blob.file_name,
            media_type: blob.media_type,
            content: blob.content,
        })
    })
}
