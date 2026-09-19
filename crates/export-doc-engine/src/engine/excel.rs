//! Excel use cases share one optional native library. Desktop path operations
//! and browser uploads join here after their distinct transport boundaries.
use super::{
    NativeService, auth,
    error::{Result, error, invalid, unavailable},
    records::text,
    settings,
    store::Actor,
    tasks::{TaskOutput, retry::Replay},
};
use crate::{
    generated_api::*,
    paths::{ensure_safe_absolute, valid_file_name},
};
use serde_json::Value;
use std::{
    fs,
    path::{Path, PathBuf},
};

pub const OPERATIONS: &[Operation] = &[
    PREVIEW_EXCEL_IMPORT,
    PREVIEW_UPLOADED_EXCEL_IMPORT,
    START_EXCEL_TEMPLATE_DOWNLOAD_JOB,
    START_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
    START_BLANK_BOOKING_SHEET_DOWNLOAD_JOB,
    START_BLANK_BOOKING_SHEET_SAVE_TO_PATH_JOB,
    START_INVOICE_BOOKING_SHEET_DOWNLOAD_JOB,
    START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
    START_BOOKING_SHEET_CONVERT_SAVE_TO_PATH_JOB,
    UPLOAD_AND_START_BOOKING_SHEET_CONVERT_DOWNLOAD_JOB,
];
pub const LOCAL_OPERATIONS: &[Operation] = &[
    PREVIEW_EXCEL_IMPORT,
    START_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
    START_BLANK_BOOKING_SHEET_SAVE_TO_PATH_JOB,
    START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
    START_BOOKING_SHEET_CONVERT_SAVE_TO_PATH_JOB,
];
pub const UPLOADS: &[Operation] = &[
    PREVIEW_UPLOADED_EXCEL_IMPORT,
    UPLOAD_AND_START_BOOKING_SHEET_CONVERT_DOWNLOAD_JOB,
];
fn check() -> std::result::Result<(), String> {
    crate::operation::check().map_err(|e| e.to_string())
}
fn source_name(name: &str) -> Result<()> {
    if !valid_file_name(name)
        || !Path::new(name)
            .extension()
            .and_then(|value| value.to_str())
            .is_some_and(|ext| {
                ["xlsx", "xlsm", "xltx", "xltm", "xls"].contains(&ext.to_ascii_lowercase().as_str())
            })
    {
        return Err(invalid("请选择文件名有效的 Excel 工作簿。"));
    }
    Ok(())
}
use super::media::read_local as read;

pub(super) fn destination(body: &Value, local: bool) -> Result<Option<PathBuf>> {
    if !local {
        return Ok(None);
    }
    let path = PathBuf::from(text(body, "destinationPath"));
    ensure_safe_absolute(&path).map_err(invalid)?;
    if !path
        .file_name()
        .and_then(|name| name.to_str())
        .is_some_and(valid_file_name)
        || !path
            .extension()
            .is_some_and(|ext| ext.eq_ignore_ascii_case("xlsx"))
    {
        return Err(invalid("请选择合法的 .xlsx 输出文件名。"));
    }
    if !path.parent().is_some_and(Path::is_dir) {
        return Err(invalid("输出目录不存在。"));
    }
    Ok(Some(path))
}
pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    source_name(name)?;
    if bytes.is_empty() || bytes.len() > export_doc_excel::MAX_INPUT {
        return Err(error(413, "上传的 Excel 为空或超过 25 MiB。"));
    }
    if operation == PREVIEW_UPLOADED_EXCEL_IMPORT {
        let settings = settings::current(&service.store)?;
        let date = service.clock.now().map_err(unavailable)?.today.to_string();
        let preview =
            export_doc_excel::preview(bytes, name, &settings["excelImport"], &date, &check)
                .map_err(invalid)?;
        return serde_json::to_value(preview).map_err(Into::into);
    }
    let replay = Replay::new(operation, &[], &serde_json::json!({})).with_input(name, bytes);
    let bytes = bytes.to_vec();
    let store = service.store.clone();
    let actor_id = actor.id;
    service.jobs.start_replayable(
        actor,
        "BookingSheetConvert",
        "转换订舱托单",
        Some(replay),
        move |_| {
            auth::authorize_operation(&auth::current_actor(&store, actor_id)?, operation, &[])?;
            let bytes = export_doc_excel::convert_booking(&bytes, &check).map_err(invalid)?;
            auth::authorize_operation(&auth::current_actor(&store, actor_id)?, operation, &[])?;
            Ok(TaskOutput::file(
                "订舱托单.xlsx".into(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                bytes,
            ))
        },
    )
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    if operation == PREVIEW_EXCEL_IMPORT {
        let path = PathBuf::from(text(body, "filePath"));
        let name = path
            .file_name()
            .and_then(|name| name.to_str())
            .ok_or_else(|| invalid("Excel 源文件名无效。"))?;
        let bytes = read(&path, export_doc_excel::MAX_INPUT)?;
        return upload(service, actor, PREVIEW_UPLOADED_EXCEL_IMPORT, name, &bytes);
    }
    let destination = destination(body, LOCAL_OPERATIONS.contains(&operation))?;
    if destination.is_some() && service.provider()? != "SQLite" {
        return Err(error(403, "服务器不能保存到客户端本机路径。"));
    }
    let replay = Replay::new(operation, parameters, body);
    if operation == START_BOOKING_SHEET_CONVERT_SAVE_TO_PATH_JOB {
        let source = PathBuf::from(text(body, "sourcePath"));
        if let Some(target) = &destination {
            let source_name = fs::canonicalize(&source)?;
            if target.exists() && fs::canonicalize(target)? == source_name {
                return Err(invalid("托单必须另存为新文件，不能覆盖源文件。"));
            }
        }
        let bytes = read(&source, export_doc_excel::MAX_INPUT)?;
        let store = service.store.clone();
        let actor_id = actor.id;
        return service.jobs.start_replayable(
            actor,
            "BookingSheetConvert",
            "转换订舱托单",
            Some(replay),
            move |_| {
                auth::authorize_operation(&auth::current_actor(&store, actor_id)?, operation, &[])?;
                let bytes = export_doc_excel::convert_booking(&bytes, &check).map_err(invalid)?;
                auth::authorize_operation(&auth::current_actor(&store, actor_id)?, operation, &[])?;
                let mut output = TaskOutput::file(
                    "订舱托单.xlsx".into(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    bytes,
                );
                output.destination = destination;
                Ok(output)
            },
        );
    }
    let settings = settings::current(&service.store)?;
    let template = service
        .paths
        .app_root
        .join("Resources")
        .join("ExcelTemplates")
        .join("invoice-import-template.xlsx");
    let bytes = read(&template, export_doc_excel::MAX_INPUT)
        .map_err(|e| unavailable(format!("原版 Excel 模板不可用：{e}")))?;
    let invoice = if [
        START_INVOICE_BOOKING_SHEET_DOWNLOAD_JOB,
        START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
    ]
    .contains(&operation)
    {
        let id = super::records::id(parameters)?;
        let value = service.store.get("invoices", id)?;
        if !auth::visible(actor, "document.invoices", "view", &value) {
            return Err(error(403, "没有读取发票的权限。"));
        }
        Some(serde_json::from_value::<ApiInvoiceDetailDto>(value)?)
    } else {
        None
    };
    let mut exporter_name = text(&settings["system"], "defaultTemplateExporterNameCn");
    if exporter_name.trim().is_empty() {
        let mut names: Vec<_> = service
            .store
            .all("exporters")?
            .iter()
            .map(|exporter| text(exporter, "exporterNameCN"))
            .filter(|name| !name.trim().is_empty())
            .collect();
        names.sort_by_key(|name| name.to_lowercase());
        names.dedup_by(|a, b| a.eq_ignore_ascii_case(b));
        exporter_name = if names.len() == 1 {
            names.remove(0)
        } else {
            "请填写出口商中文名称".into()
        };
    }
    let booking = ![
        START_EXCEL_TEMPLATE_DOWNLOAD_JOB,
        START_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
    ]
    .contains(&operation);
    let store = service.store.clone();
    let actor_id = actor.id;
    service.jobs.start_replayable(
        actor,
        if booking {
            "BookingSheetExport"
        } else {
            "ExcelTemplateExport"
        },
        if booking {
            "导出订舱托单"
        } else {
            "导出 Excel 导入模板"
        },
        Some(replay),
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            if let Some(invoice) = &invoice {
                let current = store.get("invoices", i64::from(invoice.id))?;
                if !auth::visible(&actor, "document.invoices", "view", &current) {
                    return Err(error(403, "当前账号已没有读取发票的权限。"));
                }
            }
            let invoice_id = invoice.as_ref().map(|invoice| i64::from(invoice.id));
            let bytes = if let Some(invoice) = invoice {
                export_doc_excel::booking_from_invoice(
                    &bytes,
                    &invoice,
                    &settings["excelImport"],
                    &check,
                )
            } else {
                export_doc_excel::blank_template(&bytes, &exporter_name, booking, &check)
            }
            .map_err(invalid)?;
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            if let Some(id) = invoice_id {
                if !auth::visible(
                    &actor,
                    "document.invoices",
                    "view",
                    &store.get("invoices", id)?,
                ) {
                    return Err(error(403, "当前账号已没有读取发票的权限。"));
                }
            }
            let mut output = TaskOutput::file(
                if booking {
                    "订舱托单.xlsx"
                } else {
                    "导入数据模板.xlsx"
                }
                .into(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                bytes,
            );
            output.destination = destination;
            Ok(output)
        },
    )
}
