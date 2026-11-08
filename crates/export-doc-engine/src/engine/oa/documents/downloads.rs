//! Read blobs in one authorized snapshot; hashing and compression release the connection.
use super::*;
use crate::engine::backup_io::{Download, Reservation};
#[cfg(test)]
#[path = "downloads/tests.rs"]
mod tests;
const PDF_LIMIT: usize = 16 * 1024 * 1024;
const ARCHIVE_LIMIT: usize = 64 * 1024 * 1024;
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
    let (files, file_name, media_type, pdf_digest, mut reservation) = service
        .store
        .read(|tx| gather(service, tx, actor, meta, parameters))?;
    for (_, bytes, expected) in &files {
        crate::operation::check()?;
        if !expected.is_empty() && media::digest(bytes) != *expected {
            return Err(unavailable("历史审批材料完整性校验失败。"));
        }
    }
    let content = if meta["action"] == "document-download" {
        files
            .into_iter()
            .next()
            .ok_or_else(|| unavailable("审批单缺失。"))?
            .1
    } else {
        let mut archive = export_doc_report::DocumentArchive::new();
        for (name, bytes, _) in files {
            crate::operation::check()?;
            archive.add(&name, &bytes)?;
        }
        archive.finish()?
    };
    service.store.read(|tx| {
        let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let snapshot = submissions::checked(tx, &row, submissions::id(parameters)?)?;
        if snapshot["status"] != "Approved"
            || !super::super::materials::full(tx, &actor, &row, &snapshot)?
        {
            return Err(error(403, "材料授权已变化，请重新下载。"));
        }
        if tx
            .blob_info(submissions::id(parameters)?, submissions::PDF)?
            .is_none_or(|i| i.digest != pdf_digest)
        {
            return Err(conflict("审批单已重新生成，请重新下载。"));
        }
        Ok(())
    })?;
    reservation.shrink_to(content.len());
    Ok(Download::Memory {
        output: FileOutput {
            file_name,
            media_type,
            content,
        },
        reservation,
    })
}
type Source = (
    Vec<(String, Vec<u8>, String)>,
    String,
    String,
    String,
    Reservation,
);
fn gather(
    service: &NativeService,
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
    let mut sources = vec![(
        id,
        submissions::PDF.to_owned(),
        pdf.digest.clone(),
        pdf.size,
    )];
    let mut total = pdf.size;
    let mut manifest = vec![];
    if !direct {
        let attachments = snapshot["request"]["attachments"]
            .as_array()
            .filter(|a| a.len() <= 20)
            .ok_or_else(|| unavailable("提交附件清单损坏。"))?;
        for attachment in attachments {
            let attachment_id = records::positive(attachment, "id", "附件")?;
            let info = tx
                .blob_info(attachment_id, "oa-attachment")?
                .ok_or_else(|| unavailable("历史附件元数据缺失。"))?;
            if info.size > media::DOCUMENT_LIMIT as u64 || attachment["digest"] != info.digest {
                return Err(unavailable("历史附件元数据不一致。"));
            }
            total = total
                .checked_add(info.size)
                .ok_or_else(|| invalid("材料归档超过容量。"))?;
            sources.push((
                attachment_id,
                "oa-attachment".into(),
                info.digest,
                info.size,
            ));
        }
        manifest = serde_json::to_vec_pretty(
            &json!({"submission":submissions::project(tx,snapshot.clone(),false)?,"attachments":attachments}),
        )?;
        total = total
            .checked_add(manifest.len() as u64)
            .ok_or_else(|| invalid("材料归档超过容量。"))?;
        if total > ARCHIVE_LIMIT as u64 {
            return Err(invalid("审批材料归档累计超过 64 MiB。"));
        }
    }
    let reservation = service
        .jobs
        .reserve_download(total as usize * if direct { 1 } else { 2 }, !direct)?;
    let mut files = vec![];
    let mut direct_name = String::new();
    let mut direct_media = String::new();
    for (index, (id, kind, digest, size)) in sources.into_iter().enumerate() {
        crate::operation::check()?;
        let blob = tx
            .blob(id, &kind)?
            .ok_or_else(|| unavailable("历史材料原文件缺失。"))?;
        if blob.content.len() as u64 != size || blob.digest != digest {
            return Err(unavailable("历史材料内容与元数据不一致。"));
        }
        if index == 0 {
            direct_name = blob.file_name.clone();
            direct_media = blob.media_type.clone();
        }
        let name = if index == 0 {
            blob.file_name
        } else {
            format!("{index:02}-{}", blob.file_name)
        };
        files.push((name, blob.content, digest));
    }
    if !direct {
        files.push(("审批材料清单.json".into(), manifest, String::new()));
    }
    Ok((
        files,
        if direct {
            direct_name
        } else {
            format!(
                "审批材料-{}-v{}.zip",
                row["id"], snapshot["submissionNumber"]
            )
        },
        if direct {
            direct_media
        } else {
            "application/zip".into()
        },
        pdf.digest,
        reservation,
    ))
}
