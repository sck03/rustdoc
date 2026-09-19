//! Retry descriptions are private persisted inputs, never browser-provided commands.
use super::{FileOutput, persistence};
use crate::{
    engine::{
        NativeService, auth,
        error::{Result, conflict, error, invalid, unavailable},
        media::digest,
        reports,
        store::{Actor, Connection, Store},
    },
    generated_api::*,
};
use export_doc_storage::BlobWrite;
use serde::{Deserialize, Serialize};
use serde_json::Value;

const INPUT: &str = "job-input";
const MAX_INPUT: usize = 25 * 1024 * 1024;

#[derive(Serialize, Deserialize)]
pub(crate) struct Replay {
    operation: String,
    parameters: Vec<(String, String)>,
    body: Value,
    #[serde(skip)]
    input: Option<FileOutput>,
}
impl Replay {
    pub fn new(operation: Operation, parameters: &[(&str, String)], body: &Value) -> Self {
        Self {
            operation: operation.id.into(),
            parameters: parameters
                .iter()
                .map(|(key, value)| ((*key).into(), value.clone()))
                .collect(),
            body: body.clone(),
            input: None,
        }
    }
    #[allow(dead_code)]
    pub fn with_input(mut self, name: &str, bytes: &[u8]) -> Self {
        self.input = Some(FileOutput {
            file_name: name.into(),
            media_type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet".into(),
            content: bytes.into(),
        });
        self
    }
    pub(super) fn persist(&self, tx: &Connection, job: &mut Value) -> Result<()> {
        let descriptor = serde_json::to_value(self)?;
        if serde_json::to_vec(&descriptor)?.len() > 64 * 1024 {
            return Err(invalid("任务重试参数超过容量上限。"));
        }
        job["_retry"] = descriptor;
        let public_operation = match self.operation.as_str() {
            "StartExcelTemplateDownloadJob" | "StartExcelTemplateSaveToPathJob" => {
                "StartExcelTemplateExportJob"
            }
            "StartBlankBookingSheetDownloadJob" | "StartBlankBookingSheetSaveToPathJob" => {
                "StartBlankBookingSheetExportJob"
            }
            "StartInvoiceBookingSheetDownloadJob" | "StartInvoiceBookingSheetSaveToPathJob" => {
                "StartInvoiceBookingSheetExportJob"
            }
            "UploadAndStartBookingSheetConvertDownloadJob"
            | "StartBookingSheetConvertSaveToPathJob" => "StartBookingSheetConvertJob",
            "StartInvoiceReportPdfDownloadJob" | "StartInvoiceReportPdfSaveToPathJob" => {
                "StartInvoiceReportPdfJob"
            }
            "StartPaymentVoucherPdfDownloadJob" | "StartPaymentVoucherPdfSaveToPathJob" => {
                "StartPaymentVoucherPdfJob"
            }
            "StartInvoiceReportPdfZipDownloadJob" | "StartInvoiceReportPdfZipSaveToPathJob" => {
                "StartInvoiceReportPdfZipJob"
            }
            "DownloadQueriedInvoices" | "SaveQueriedInvoicesToPath" => "StartQueryInvoiceExportJob",
            "StartInvoiceDocumentPackageDownloadJob"
            | "StartInvoiceDocumentPackageSaveToPathJob" => "StartInvoiceDocumentPackageJob",
            "StartInvoiceDocumentEmailJob" => "StartInvoiceDocumentEmailJob",
            _ => return Err(invalid("此文件任务没有已声明的重试合同。")),
        };
        job["retryOperation"] = serde_json::json!(public_operation);
        if let Some(input) = &self.input {
            if input.content.is_empty()
                || input.content.len() > MAX_INPUT
                || !crate::paths::valid_file_name(&input.file_name)
            {
                return Err(invalid("任务输入文件名或容量无效。"));
            }
            let id = job["id"]
                .as_i64()
                .ok_or_else(|| unavailable("任务记录缺少编号。"))?;
            tx.insert_blob(&BlobWrite {
                record_id: id,
                kind: INPUT,
                file_name: &input.file_name,
                media_type: &input.media_type,
                digest: &digest(&input.content),
                content: &input.content,
                created_at: &crate::engine::store::timestamp(),
            })?;
            job["_retryInput"] = serde_json::json!(true);
        }
        Ok(())
    }
}

pub(super) fn remove_input(tx: &Connection, id: i64) -> Result<()> {
    tx.delete_blob(id, INPUT)?;
    Ok(())
}
fn read(store: &Store, actor: &Actor, id: &str) -> Result<Replay> {
    let tx = store.connection()?;
    let job = persistence::checked(&tx, actor, id)?;
    if !matches!(job["status"].as_str(), Some("Failed" | "Canceled")) || !job["_retry"].is_object()
    {
        return Err(conflict("仅失败或取消且保留重试输入的任务可重试。"));
    }
    let mut replay: Replay = serde_json::from_value(job["_retry"].clone())
        .map_err(|_| unavailable("任务重试描述损坏。"))?;
    if job["_retryInput"] == true {
        let blob = tx
            .blob(
                job["id"]
                    .as_i64()
                    .ok_or_else(|| unavailable("任务记录缺少编号。"))?,
                INPUT,
            )?
            .ok_or_else(|| unavailable("任务重试输入缺失。"))?;
        if blob.content.len() > MAX_INPUT || digest(&blob.content) != blob.digest {
            return Err(unavailable("任务重试输入未通过完整性检查。"));
        }
        replay.input = Some(FileOutput {
            file_name: blob.file_name,
            media_type: blob.media_type,
            content: blob.content,
        });
    }
    Ok(replay)
}

pub(crate) fn execute(service: &NativeService, actor: &Actor, id: &str) -> Result<Value> {
    let replay = read(&service.store, actor, id)?;
    let operation = ALL_OPERATIONS
        .iter()
        .find(|op| op.id == replay.operation)
        .copied()
        .ok_or_else(|| conflict("任务对应的操作已不可用，请重新提交。"))?;
    // Resolve current grants, current records and current template versions in
    // the normal use case. Stored input never carries an authorization snapshot.
    auth::authorize_operation(actor, operation, &[])?;
    let parameters: Vec<_> = replay
        .parameters
        .iter()
        .map(|(key, value)| (key.as_str(), value.clone()))
        .collect();
    if service.provider()? != "SQLite" && NativeService::requires_local_transport(operation) {
        return Err(error(403, "服务器不能重试本机路径操作。"));
    }
    #[cfg(feature = "excel")]
    {
        if crate::engine::invoice_query::EXPORTS.contains(&operation) {
            return crate::engine::invoice_query::export(service, actor, operation, &replay.body);
        }
        use crate::engine::excel;
        if operation == UPLOAD_AND_START_BOOKING_SHEET_CONVERT_DOWNLOAD_JOB {
            let input = replay
                .input
                .ok_or_else(|| unavailable("重试工作簿缺失。"))?;
            return excel::upload(service, actor, operation, &input.file_name, &input.content);
        }
        if excel::OPERATIONS.contains(&operation) && operation.id.starts_with("Start") {
            return excel::handle(service, actor, operation, &parameters, &replay.body);
        }
    }
    if reports::OPERATIONS.contains(&operation) && operation.id.starts_with("Start") {
        return reports::handle(service, actor, operation, &parameters, &[], &replay.body);
    }
    Err(conflict("此任务不能自动重试，请回到原功能重新提交。"))
}
