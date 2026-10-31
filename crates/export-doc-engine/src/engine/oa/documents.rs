//! Approval PDF preparation and access. Jobs own execution/cancellation; archived
//! files commit with task completion and are never disposable download copies.
use super::super::{
    error::unavailable,
    media,
    tasks::{ArchiveOutput, FileOutput, PublicationCheck, TaskOutput},
};
use super::*;
use std::sync::Arc;

pub(super) fn start_approved(
    service: &NativeService,
    actor: &Actor,
    meta: &Value,
    row: &Value,
) -> Result<Value> {
    let operation = crate::generated_api::ALL_OPERATIONS
        .iter()
        .find(|op| {
            metadata(**op)
                .is_some_and(|m| m["kind"] == meta["kind"] && m["action"] == "document-generate")
        })
        .ok_or_else(|| unavailable("审批单生成契约缺失。"))?;
    let parameters = [
        ("id", row["id"].to_string()),
        ("submissionId", row["submissionId"].to_string()),
    ];
    match start(service, actor, *operation, &parameters) {
        Ok(snapshot) => Ok(snapshot),
        // Failure remains visible on the approved submission; the applicant can
        // retry without repeating approval or the personnel transaction.
        Err(cause) => service.store.transaction(|tx| {
            let id = records::positive(row, "submissionId", "提交版本")?;
            let mut snapshot = submissions::checked(tx, row, id)?;
            snapshot["documentError"] = json!(cause.message);
            let snapshot = store::save_in_scope(
                tx,
                submissions::KIND,
                id,
                snapshot,
                Some(format!("{}:{}", row["id"], row["submissionNumber"])),
                actor,
                "document-start-failed",
                Some(row),
            )?;
            submissions::project(tx, snapshot, false)
        }),
    }
}
pub(super) fn start(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<Value> {
    let _gate = service
        .oa_document_gate
        .try_lock()
        .map_err(|_| error(429, "正在准备审批单，请稍后重试。"))?;
    let meta = metadata(operation).ok_or_else(|| invalid("未知审批单操作。"))?;
    let id = submissions::id(parameters)?;
    let request_id = records::id(parameters)?;
    let snapshot = service.store.read(|tx| {
        let (_, row) = current(tx, actor, meta, request_id)?;
        let snapshot = submissions::checked(tx, &row, id)?;
        if snapshot["status"] != "Approved" {
            return Err(conflict("只有已批准且未作废的提交版本可以生成正式审批单。"));
        }
        if !super::materials::full(tx, actor, &row, &snapshot)? {
            return Err(error(
                403,
                "生成完整归档审批单需要完整材料权限；可下载本节点的材料投影审批单。",
            ));
        }
        Ok(snapshot)
    })?;
    let projected = service
        .store
        .read(|tx| submissions::project(tx, snapshot.clone(), true))?;
    if matches!(
        projected["documentStatus"].as_str(),
        Some("Ready" | "Running")
    ) {
        return Ok(projected);
    }
    let design = sheet_templates::validate_design(&text(&snapshot, "template.content"))?;
    let fonts = Arc::clone(&service.report_fonts);
    let data = export_doc_report::approval::data(&snapshot, &store::timestamp())?;
    let output_name = format!(
        "审批单-{}-v{}.pdf",
        request_id, snapshot["submissionNumber"]
    );
    let check_kind = kind(meta);
    // Do not render or publish before the durable submission owns this job.
    // A failed binding drops the sender, so the worker terminates without output.
    let (bound, ready) = std::sync::mpsc::sync_channel(1);
    let job = service.jobs.start(
        actor,
        operation,
        "oa-approval-document",
        &format!("{} · #{request_id}", text(&snapshot, "formName")),
        move |cancelled| {
            ready
                .recv_timeout(std::time::Duration::from_secs(30))
                .map_err(|_| unavailable("审批单任务未能绑定提交版本。"))?;
            crate::operation::check()?;
            let _fonts = fonts.enter()?;
            let document = export_doc_report::render_design(&data, &design, cancelled)?;
            let bytes = export_doc_report::pdf_document(&document, &fonts, cancelled)?;
            if bytes.len() > 16 * 1024 * 1024 {
                return Err(unavailable(
                    "审批单超过 16 MiB 容量，请联系管理员检查模板。",
                ));
            }
            crate::operation::check()?;
            Ok(TaskOutput {
                file: None,
                managed_file: None,
                destination: None,
                directory: None,
                detail: "审批单已保存到提交档案，可从申请详情下载。".into(),
                archive: Some(ArchiveOutput {
                    record_id: id,
                    kind: submissions::PDF,
                    file: FileOutput {
                        file_name: output_name,
                        media_type: "application/pdf".into(),
                        content: bytes,
                    },
                }),
                publication: Some(PublicationCheck::new(move |tx, current_actor| {
                    let row = store::get(tx, &check_kind, request_id)?;
                    access(tx, current_actor, meta, &row)?;
                    if submissions::checked(tx, &row, id)?["status"] != "Approved" {
                        return Err(conflict("批准已作废，不发布正式审批单。"));
                    }
                    if !super::materials::full(
                        tx,
                        current_actor,
                        &row,
                        &submissions::checked(tx, &row, id)?,
                    )? {
                        return Err(error(403, "材料权限已变化，不发布完整审批单。"));
                    }
                    Ok(())
                })),
            })
        },
    )?;
    let projected = service.store.transaction(|tx| {
        let (_, row) = current(tx, actor, meta, request_id)?;
        let mut snapshot = submissions::checked(tx, &row, id)?;
        if snapshot["status"] != "Approved" {
            return Err(conflict("批准已作废，不能生成正式审批单。"));
        }
        snapshot["documentJobId"] = job["jobId"].clone();
        snapshot["documentError"] = json!("");
        let snapshot = store::save_in_scope(
            tx,
            submissions::KIND,
            id,
            snapshot,
            Some(format!("{}:{}", row["id"], snapshot_number(tx, id)?)),
            actor,
            "generate-document",
            Some(&row),
        )?;
        submissions::project(tx, snapshot, true)
    })?;
    bound
        .send(())
        .map_err(|_| unavailable("审批单任务已终止，请重试。"))?;
    Ok(projected)
}
fn snapshot_number(tx: &Connection, id: i64) -> Result<i64> {
    records::positive(
        &store::get(tx, submissions::KIND, id)?,
        "submissionNumber",
        "提交版本",
    )
}

pub(in crate::engine) fn download(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<FileOutput> {
    let meta = metadata(operation).ok_or_else(|| invalid("未知审批单下载。"))?;
    let partial = service.store.read(|tx| {
        let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let snapshot = submissions::checked(tx, &row, submissions::id(parameters)?)?;
        if snapshot["status"] != "Approved" {
            return Err(conflict("该提交版本尚未批准或已作废。"));
        }
        if super::materials::full(tx, &actor, &row, &snapshot)? {
            return Ok(None);
        }
        if meta["action"] != "document-download" {
            return Err(error(403, "完整材料归档需要完整材料权限。"));
        }
        Ok(Some(super::materials::snapshot(
            tx, &actor, &row, snapshot,
        )?))
    })?;
    if let Some(snapshot) = partial {
        crate::operation::check()?;
        let cancelled = crate::operation::cancellation_flag();
        let design = sheet_templates::validate_design(&text(&snapshot, "template.content"))?;
        let data = export_doc_report::approval::data(&snapshot, &store::timestamp())?;
        let _fonts = service.report_fonts.enter()?;
        let document = export_doc_report::render_design(&data, &design, &cancelled)?;
        let bytes = export_doc_report::pdf_document(&document, &service.report_fonts, &cancelled)?;
        if bytes.len() > 16 * 1024 * 1024 {
            return Err(unavailable("材料投影审批单超过容量。"));
        }
        service.store.read(|tx| {
            let (actor, row) = current(tx, actor, meta, records::id(parameters)?)?;
            let current = submissions::checked(tx, &row, submissions::id(parameters)?)?;
            if current["status"] != "Approved" {
                return Err(conflict("批准已作废。"));
            }
            let current = super::materials::snapshot(tx, &actor, &row, current)?;
            if current["request"] != snapshot["request"]
                || current["approvalPlan"] != snapshot["approvalPlan"]
            {
                return Err(conflict("材料权限或版本已变化，请重新下载。"));
            }
            Ok(())
        })?;
        return Ok(FileOutput {
            file_name: format!("审批单-{}-材料投影.pdf", records::id(parameters)?),
            media_type: "application/pdf".into(),
            content: bytes,
        });
    }
    service.store.read(|tx| {
        let (_, row) = current(tx, actor, meta, records::id(parameters)?)?;
        let id = submissions::id(parameters)?;
        let snapshot = submissions::checked(tx, &row, id)?;
        if snapshot["status"] != "Approved" {
            return Err(conflict("该提交版本尚未批准或已作废，不能下载正式审批单。"));
        }
        let info = tx
            .blob_info(id, submissions::PDF)?
            .ok_or_else(|| conflict("审批单尚未生成，请等待或重试。"))?;
        if info.size > 16 * 1024 * 1024 {
            return Err(unavailable("审批单超过下载容量。"));
        }
        let blob = tx
            .blob(id, submissions::PDF)?
            .ok_or_else(|| unavailable("审批单文件缺失。"))?;
        if media::digest(&blob.content) != blob.digest || blob.digest != info.digest {
            return Err(unavailable("审批单完整性校验失败。"));
        }
        if meta["action"] == "document-download" {
            return Ok(FileOutput {
                file_name: blob.file_name,
                media_type: blob.media_type,
                content: blob.content,
            });
        }
        let mut archive = export_doc_report::DocumentArchive::new();
        archive.add(&blob.file_name, &blob.content)?;
        let attachments = snapshot["request"]["attachments"]
            .as_array()
            .ok_or_else(|| unavailable("提交附件清单损坏。"))?;
        for (index, attachment) in attachments.iter().enumerate() {
            crate::operation::check()?;
            let id = records::positive(attachment, "id", "附件")?;
            let info = tx
                .blob_info(id, "oa-attachment")?
                .ok_or_else(|| unavailable("历史附件元数据缺失。"))?;
            if info.size > media::DOCUMENT_LIMIT as u64 || attachment["digest"] != info.digest {
                return Err(unavailable("历史附件元数据不一致。"));
            }
            let file = tx
                .blob(id, "oa-attachment")?
                .ok_or_else(|| unavailable("历史附件原文件缺失。"))?;
            if media::digest(&file.content) != info.digest {
                return Err(unavailable("历史附件完整性校验失败。"));
            }
            archive.add(
                &format!("{:02}-{}", index + 1, file.file_name),
                &file.content,
            )?;
        }
        let manifest = submissions::project(tx, snapshot.clone(), false)?;
        archive.add(
            "审批材料清单.json",
            &serde_json::to_vec_pretty(&json!({"submission":manifest,"attachments":attachments}))?,
        )?;
        Ok(FileOutput {
            file_name: format!(
                "审批材料-{}-v{}.zip",
                row["id"], snapshot["submissionNumber"]
            ),
            media_type: "application/zip".into(),
            content: archive.finish()?,
        })
    })
}
