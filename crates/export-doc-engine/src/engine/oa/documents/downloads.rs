//! Capture authorized metadata; stream immutable files outside database transactions.
use super::*;
use crate::engine::backup_io::Download;
use std::io::{Seek, SeekFrom, Write};
#[cfg(test)]
#[path = "downloads/tests.rs"]
mod tests;
const PDF_LIMIT: usize = 16 * 1024 * 1024;
pub(in crate::engine) fn download(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<FileOutput> {
    prepare(service, actor, operation, parameters)?.into_memory()
}
pub(in crate::engine) fn prepare(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<Download> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知审批材料下载。"))?;
    let partial = service.store.read(|tx| {
        let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let snapshot = submissions::checked(tx, &row, submissions::id(parameters)?)?;
        if snapshot["status"] != "Approved" {
            return Err(conflict("提交版本尚未批准或已作废。"));
        }
        if super::super::materials::full(tx, &actor, &row, &snapshot)? {
            return Ok(None);
        }
        if meta["action"] != "document-download" {
            return Err(error(403, "完整归档需要完整材料权限。"));
        }
        Ok(Some(super::super::materials::snapshot(
            tx, &actor, &row, snapshot,
        )?))
    })?;
    if let Some(snapshot) = partial {
        let mut reservation = service.jobs.reserve_download(PDF_LIMIT * 2, true)?;
        let cancelled = crate::operation::cancellation_flag();
        let design = sheet_templates::validate_design(&text(&snapshot, "template.content"))?;
        let data = export_doc_report::approval::data(&snapshot, &store::timestamp())?;
        let content = {
            let _rendering = crate::engine::reports::resources::acquire()?;
            let _fonts = service.report_fonts.enter()?;
            let document = export_doc_report::render_design(&data, &design, &cancelled)?;
            export_doc_report::pdf_document(&document, &service.report_fonts, &cancelled)?
        };
        if content.len() > PDF_LIMIT {
            return Err(unavailable("材料投影审批单超过容量。"));
        }
        service.store.read(|tx| {
            let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
            let fresh = submissions::checked(tx, &row, submissions::id(parameters)?)?;
            if fresh["status"] != "Approved" {
                return Err(conflict("批准已作废。"));
            }
            let fresh = super::super::materials::snapshot(tx, &actor, &row, fresh)?;
            if fresh["request"] != snapshot["request"]
                || fresh["approvalPlan"] != snapshot["approvalPlan"]
            {
                return Err(conflict("材料权限或版本已变化，请重新下载。"));
            }
            Ok(())
        })?;
        reservation.shrink_to(content.len());
        return Ok(Download::Memory {
            output: FileOutput {
                file_name: format!("审批单-{}-材料投影.pdf", records::id(parameters)?),
                media_type: "application/pdf".into(),
                content,
            },
            reservation,
        });
    }
    let source = service
        .store
        .read(|tx| gather(tx, actor, meta, parameters))?;
    let direct = meta["action"] == "document-download";
    let output = if direct {
        let entry = &source.files[0];
        Download::File {
            file_name: source.file_name.clone(),
            media_type: "application/pdf".into(),
            file: read_source(service, entry)?,
            size: entry.info.size,
        }
    } else {
        let file =
            crate::paths::anonymous_file(&service.paths.cache_root.join("ApprovalDownloads"))
                .map_err(unavailable)?;
        let mut archive = zip::ZipWriter::new(file);
        let options = zip::write::SimpleFileOptions::default()
            .compression_method(zip::CompressionMethod::Stored)
            .large_file(true);
        for (index, entry) in source.files.iter().enumerate() {
            crate::operation::check()?;
            let mut file = read_source(service, entry)?;
            let name = if index == 0 {
                entry.info.file_name.clone()
            } else {
                format!("{index:02}-{}", entry.info.file_name)
            };
            if !crate::paths::valid_file_name(&name) {
                return Err(unavailable("历史材料文件名无效。"));
            }
            archive
                .start_file(name, options)
                .map_err(|e| unavailable(e.to_string()))?;
            crate::engine::backup_io::copy_chunks(
                &mut file,
                &mut archive,
                entry.info.size,
                |_| {},
            )?;
        }
        archive
            .start_file("审批材料清单.json", options)
            .map_err(|e| unavailable(e.to_string()))?;
        archive.write_all(&source.manifest)?;
        let mut file = archive.finish().map_err(|e| unavailable(e.to_string()))?;
        file.flush()?;
        let size = file.metadata()?.len();
        file.seek(SeekFrom::Start(0))?;
        Download::File {
            file_name: source.file_name.clone(),
            media_type: "application/zip".into(),
            file,
            size,
        }
    };
    service.store.read(|tx| {
        let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let snapshot = submissions::checked(tx, &row, submissions::id(parameters)?)?;
        if snapshot["status"] != "Approved"
            || !super::super::materials::full(tx, &actor, &row, &snapshot)?
        {
            return Err(error(403, "材料授权已变化，请重新下载。"));
        }
        for entry in &source.files {
            if tx
                .blob_info(entry.id, &entry.kind)?
                .is_none_or(|i| i.digest != entry.info.digest || i.size != entry.info.size)
            {
                return Err(conflict("审批材料已变化，请重新下载。"));
            }
        }
        Ok(())
    })?;
    Ok(output)
}

struct Entry {
    id: i64,
    kind: String,
    info: export_doc_storage::BlobInfo,
}
struct Source {
    files: Vec<Entry>,
    file_name: String,
    manifest: Vec<u8>,
}
fn read_source(service: &NativeService, entry: &Entry) -> Result<std::fs::File> {
    let blob = service.store.read(|tx| {
        tx.file_info(entry.id, &entry.kind)?
            .ok_or_else(|| unavailable("历史材料原文件缺失。"))
    })?;
    if blob.object.size != entry.info.size || blob.object.digest != entry.info.digest {
        return Err(unavailable("历史审批材料完整性校验失败。"));
    }
    service.store.files.open_object(&blob.object)
}
fn gather(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    parameters: &[(&str, String)],
) -> Result<Source> {
    let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
    let id = submissions::id(parameters)?;
    let snapshot = submissions::checked(tx, &row, id)?;
    if snapshot["status"] != "Approved" {
        return Err(conflict("该提交版本尚未批准或已作废。"));
    }
    if !super::super::materials::full(tx, &actor, &row, &snapshot)? {
        return Err(error(403, "完整材料授权已变化。"));
    }
    let pdf = tx
        .blob_info(id, submissions::PDF)?
        .ok_or_else(|| conflict("审批单尚未生成，请等待或重试。"))?;
    if pdf.size == 0 || pdf.size > PDF_LIMIT as u64 {
        return Err(unavailable("审批单下载大小无效。"));
    }
    let direct = meta["action"] == "document-download";
    let file_name = if direct {
        pdf.file_name.clone()
    } else {
        format!(
            "审批材料-{}-v{}.zip",
            row["id"], snapshot["submissionNumber"]
        )
    };
    let mut files = vec![Entry {
        id,
        kind: submissions::PDF.into(),
        info: pdf,
    }];
    let manifest = if direct {
        Vec::new()
    } else {
        let attachments = snapshot["request"]["attachments"]
            .as_array()
            .ok_or_else(|| unavailable("提交附件清单损坏。"))?;
        for attachment in attachments {
            let attachment_id = records::positive(attachment, "id", "附件")?;
            let info = tx
                .blob_info(attachment_id, "oa-attachment")?
                .ok_or_else(|| unavailable("历史附件元数据缺失。"))?;
            if info.size == 0
                || info.size > media::DOCUMENT_LIMIT as u64
                || attachment["digest"] != info.digest
            {
                return Err(unavailable("历史附件元数据不一致。"));
            }
            files.push(Entry {
                id: attachment_id,
                kind: "oa-attachment".into(),
                info,
            });
        }
        serde_json::to_vec_pretty(
            &json!({"submission":submissions::project(tx,snapshot.clone(),false)?,"attachments":attachments}),
        )?
    };
    Ok(Source {
        files,
        file_name,
        manifest,
    })
}
