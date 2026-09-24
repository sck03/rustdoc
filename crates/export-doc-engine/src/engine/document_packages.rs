//! A set of invoice reports shares rendering, permissions and durable file jobs.
use super::{
    NativeService, auth,
    error::{Result, error, invalid, unavailable},
    records::{id, text},
    reports,
    store::{Actor, Store},
    tasks::{DirectoryOutput, FileOutput, TaskOutput, retry::Replay},
};
use crate::{generated_api::*, paths};
use serde_json::{Value, json};
use std::{path::PathBuf, sync::atomic::AtomicBool};
pub const OPERATIONS: &[Operation] = &[
    PREVIEW_INVOICE_DOCUMENT_PACKAGE_HTML,
    START_INVOICE_DOCUMENT_PACKAGE_DOWNLOAD_JOB,
    START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB,
];
const POLICY: &str = "单据组合复用原生报表排版，文件任务保存在业务库，最终输出写入用户选择的位置。";
pub(crate) fn items(body: &Value) -> Result<Vec<Value>> {
    let items = body["items"]
        .as_array()
        .filter(|v| !v.is_empty() && v.len() <= 20)
        .ok_or_else(|| invalid("请选择 1–20 个单据模板。"))?;
    for item in items {
        if item["reportType"] != "ExportDocument"
            || text(item, "templatePath").is_empty()
            || text(item, "name").chars().count() > 160
        {
            return Err(invalid("组合单据须选择出口单据模板，名称不超过 160 字。"));
        }
    }
    Ok(items.clone())
}
pub(super) fn files(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    invoice: i64,
    items: &[Value],
    merged: bool,
    action: &str,
    cancelled: &AtomicBool,
) -> Result<Vec<(String, Vec<u8>)>> {
    let mut files = vec![];
    let mut combined = export_doc_report::Document { pages: vec![] };
    let mut total = 0usize;
    for (index, item) in items.iter().enumerate() {
        crate::operation::check()?;
        let (document, fallback) =
            reports::invoice_document(store, paths, actor, invoice, item, action, cancelled)?;
        let name = text(item, "name");
        let name = if name.is_empty() {
            fallback
        } else {
            paths::suggested_pdf_name(&name)
        };
        let name = format!("{:02}-{name}", index + 1);
        export_doc_report::configure(&paths.font_path);
        let bytes = export_doc_report::pdf_document(&document, &paths.font_path, cancelled)?;
        total = total
            .checked_add(bytes.len())
            .filter(|n| *n <= 64 * 1024 * 1024)
            .ok_or_else(|| invalid("组合单据超过 64 MiB，请减少模板。"))?;
        if merged {
            combined.pages.extend(document.pages);
        }
        files.push((name, bytes));
    }
    if merged {
        files.push((
            "合并单据.pdf".into(),
            export_doc_report::pdf_document(&combined, &paths.font_path, cancelled)?,
        ));
    }
    Ok(files)
}

/// Native desktop preview renders the same document package through the same
/// permission checks and report renderer, but returns one in-memory PDF
/// instead of creating a durable ZIP job.
pub fn preview_pdf(
    service: &NativeService,
    actor: &Actor,
    invoice: i64,
    body: &Value,
) -> Result<Vec<u8>> {
    let items = items(body)?;
    let source = service.store.get("invoices", invoice)?;
    if !auth::visible(actor, "document.invoices", "view", &source) {
        return Err(error(403, "单据不在当前账号的输出范围内。"));
    }
    let merged = files(
        &service.store,
        &service.paths,
        actor,
        invoice,
        &items,
        true,
        "export-zip",
        &crate::operation::cancellation_flag(),
    )?;
    merged
        .into_iter()
        .find(|(name, _)| name == "合并单据.pdf")
        .map(|(_, bytes)| bytes)
        .ok_or_else(|| invalid("组合单据预览未生成合并 PDF。"))
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let invoice = id(parameters)?;
    let items = items(body)?;
    let source = service.store.get("invoices", invoice)?;
    let action = if operation == PREVIEW_INVOICE_DOCUMENT_PACKAGE_HTML {
        "view"
    } else {
        "export-zip"
    };
    let permission = if action == "view" {
        "document.invoices"
    } else {
        "document.invoice-output"
    };
    if !auth::visible(actor, permission, action, &source) {
        return Err(error(403, "单据不在当前账号的输出范围内。"));
    }
    if operation == PREVIEW_INVOICE_DOCUMENT_PACKAGE_HTML {
        let mut output = vec![];
        let mut total = 0usize;
        for item in items {
            let (document, _) = reports::invoice_document(
                &service.store,
                &service.paths,
                actor,
                invoice,
                &item,
                "preview-html",
                &crate::operation::cancellation_flag(),
            )?;
            let html = document.html()?;
            total = total
                .checked_add(html.len())
                .filter(|n| *n <= 32 * 1024 * 1024)
                .ok_or_else(|| invalid("组合预览超过 32 MiB。"))?;
            let mut item = item;
            item["html"] = json!(html);
            output.push(item);
        }
        return Ok(json!({"invoiceId":invoice,"items":output,"storagePolicy":POLICY}));
    }
    let local = operation == START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB;
    if local && service.provider()? != "SQLite" {
        return Err(error(403, "网页或容器不能写客户端本机目录。"));
    }
    let zip = !local || body["createZip"] != false;
    let destination = if local {
        let selected = PathBuf::from(text(body, "destinationPath"));
        paths::ensure_safe_absolute(&selected).map_err(invalid)?;
        if zip {
            if !selected
                .file_name()
                .and_then(|n| n.to_str())
                .is_some_and(paths::valid_file_name)
                || !selected
                    .extension()
                    .is_some_and(|n| n.eq_ignore_ascii_case("zip"))
            {
                return Err(invalid("请选择有效的 ZIP 输出文件。"));
            }
            Some(selected)
        } else {
            if !selected.is_dir() {
                return Err(invalid("请选择存在的输出文件夹。"));
            }
            Some(selected.join(format!(
                "单据-{}-{}",
                chrono::Utc::now().format("%Y%m%d-%H%M%S"),
                &paths::nonce().map_err(unavailable)?[..8]
            )))
        }
    } else {
        None
    };
    let store = service.store.clone();
    let paths = service.paths.clone();
    let actor_id = actor.id;
    let merged = body["includeMergedPdf"] == true;
    service.jobs.start_replayable(
        actor,
        "ReportDocumentPackage",
        if zip {
            "发票单据包 ZIP 导出"
        } else {
            "发票单据文件夹导出"
        },
        Some(Replay::new(operation, parameters, body)),
        move |cancelled| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            let files = files(
                &store,
                &paths,
                &actor,
                invoice,
                &items,
                merged,
                "export-zip",
                cancelled,
            )?;
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            if !auth::visible(
                &actor,
                "document.invoice-output",
                "export-zip",
                &store.get("invoices", invoice)?,
            ) {
                return Err(error(403, "生成期间单据输出权限发生变化。"));
            }
            if zip {
                let mut output = TaskOutput::file(
                    "单据组合.zip".into(),
                    "application/zip",
                    export_doc_report::zip_documents(files)?,
                );
                output.destination = destination;
                Ok(output)
            } else {
                let path = destination.ok_or_else(|| invalid("缺少输出文件夹。"))?;
                Ok(TaskOutput {
                    managed_file: None,
                    file: None,
                    destination: None,
                    detail: format!("已导出 {} 份单据至 {}", files.len(), path.display()),
                    directory: Some(DirectoryOutput {
                        path,
                        files: files
                            .into_iter()
                            .map(|(file_name, content)| FileOutput {
                                file_name,
                                content,
                                media_type: "application/pdf".into(),
                            })
                            .collect(),
                    }),
                })
            }
        },
    )
}
