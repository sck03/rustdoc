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
        // Admission contention is temporary. Leave the durable system node active
        // so the bounded scheduler retries after its normal interval.
        Err(cause) if cause.status == Some(429) => Err(cause),
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
        .map_err(|cause| match cause {
            std::sync::TryLockError::WouldBlock => error(429, "正在准备审批单，请稍后重试。"),
            std::sync::TryLockError::Poisoned(_) => unavailable("审批单准备状态异常。"),
        })?;
    let meta = metadata(operation).ok_or_else(|| invalid("未知审批单操作。"))?;
    let id = submissions::id(parameters)?;
    let request_id = records::id(parameters)?;
    let (current_actor, snapshot, projected) = service.store.read(|tx| {
        let (actor, row) = current(tx, actor, meta, request_id)?;
        let snapshot = submissions::checked(tx, &row, id)?;
        if snapshot["status"] != "Approved" {
            return Err(conflict("只有已批准且未作废的提交版本可以生成正式审批单。"));
        }
        if approval::plan(&row)?.is_some_and(|plan| {
            plan.nodes.iter().any(|n| {
                n.system_action
                    == export_doc_domain::approval_graph::SystemAction::GenerateApprovalDocument
                    && !n.skipped
            }) && !plan.nodes.iter().any(|n| {
                n.system_action
                    == export_doc_domain::approval_graph::SystemAction::GenerateApprovalDocument
                    && !n.skipped
                    && matches!(
                        n.state,
                        export_doc_domain::approval_graph::NodeState::Active
                            | export_doc_domain::approval_graph::NodeState::Blocked
                            | export_doc_domain::approval_graph::NodeState::Completed
                    )
            })
        }) {
            return Err(conflict("审批单系统节点尚未激活，请先完成前置办理。"));
        }
        if !super::materials::full(tx, &actor, &row, &snapshot)? {
            return Err(error(
                403,
                "生成完整归档审批单需要完整材料权限；可下载本节点的材料投影审批单。",
            ));
        }
        let projected = submissions::project(tx, snapshot.clone(), true)?;
        Ok((actor, snapshot, projected))
    })?;
    let actor = &current_actor;
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
            let bytes = {
                let _rendering = crate::engine::reports::resources::acquire()?;
                let _fonts = fonts.enter()?;
                let document = export_doc_report::render_design(&data, &design, cancelled)?;
                export_doc_report::pdf_document(&document, &fonts, cancelled)?
            };
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
                    let mut row = store::get(tx, &check_kind, request_id)?;
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
                    super::fulfillment::system_result(
                        &mut row,
                        export_doc_domain::approval_graph::SystemAction::GenerateApprovalDocument,
                        export_doc_domain::approval_graph::NodeState::Completed,
                        "",
                    )?;
                    super::super::workflow_tasks::persist(tx, &check_kind, &row)?;
                    Ok(())
                })),
            })
        },
    )?;
    let projected = service.store.transaction(|tx| {
        let (actor, row) = current(tx, actor, meta, request_id)?;
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
            &actor,
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

#[path = "documents/downloads.rs"]
mod downloads;
pub(in crate::engine) use downloads::{download, prepare};
