use crate::{
    api::{ApiClient, ApiError},
    generated_api::*,
    operation::OperationScope,
};
use serde_json::{Value, json};
use std::{
    sync::atomic::{AtomicBool, Ordering},
    thread,
    time::{Duration, Instant},
};

pub const PDF_LIMIT: u64 = 64 * 1024 * 1024;
fn terminal(status: &str) -> bool {
    matches!(status, "Succeeded" | "Failed" | "Canceled")
}

/// The UI's canceled scope must never prevent a cleanup request from reaching
/// the independently running file task.
pub fn wait_for_completion(
    client: &ApiClient,
    job: BackgroundJobSnapshot,
    cancelled: &AtomicBool,
) -> Result<BackgroundJobSnapshot, ApiError> {
    wait(client, job, cancelled, &mut |_| {})
}
fn wait(
    client: &ApiClient,
    mut job: BackgroundJobSnapshot,
    cancelled: &AtomicBool,
    progress: &mut impl FnMut(&BackgroundJobSnapshot),
) -> Result<BackgroundJobSnapshot, ApiError> {
    if job.job_id.is_empty() {
        return Err(ApiError::local("服务没有返回有效任务编号。"));
    }
    let parameters = [("jobId", job.job_id.clone())];
    let deadline = Instant::now() + Duration::from_secs(120);
    let result = (|| loop {
        progress(&job);
        if cancelled.load(Ordering::Relaxed) || Instant::now() >= deadline {
            return Err(ApiError::local(
                "操作已取消或超时；后台任务保留在文件任务中。",
            ));
        }
        match job.status.as_str() {
            "Succeeded" => return Ok(job.clone()),
            "Failed" => return Err(ApiError::local(job.error_message.clone())),
            "Canceled" => return Err(ApiError::local("文件任务已取消。")),
            "Running" | "Queued" | "Pending" => {}
            _ => return Err(ApiError::local("文件任务返回未知状态。")),
        }
        thread::sleep(Duration::from_millis(100));
        job = client.json(GET_JOB, &parameters, &[], None)?;
    })();
    if let Err(cause) = &result {
        if !terminal(&job.status) {
            let cleanup = OperationScope::new(Duration::from_secs(5));
            if let Err(cancel_error) =
                cleanup.run(|| client.json::<Value>(CANCEL_JOB, &parameters, &[], Some(json!({}))))
            {
                return Err(ApiError::local(format!(
                    "{cause}；取消后台任务失败：{cancel_error}"
                )));
            }
        }
    }
    result
}
pub fn render_pdf(
    client: &ApiClient,
    invoice_id: i64,
    template_path: &str,
    cancelled: &AtomicBool,
    progress: &mut impl FnMut(&BackgroundJobSnapshot),
) -> Result<Vec<u8>, ApiError> {
    render_report_pdf(
        client,
        START_INVOICE_REPORT_PDF_DOWNLOAD_JOB,
        &[("invoiceId", invoice_id.to_string())],
        json!({"reportType":"ExportDocument","templatePath":template_path,"withSeal":false}),
        cancelled,
        progress,
    )
}

pub fn render_report_pdf(
    client: &ApiClient,
    operation: Operation,
    parameters: &[(&str, String)],
    body: Value,
    cancelled: &AtomicBool,
    progress: &mut impl FnMut(&BackgroundJobSnapshot),
) -> Result<Vec<u8>, ApiError> {
    if ![
        START_INVOICE_REPORT_PDF_DOWNLOAD_JOB,
        START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB,
    ]
    .contains(&operation)
    {
        return Err(ApiError::local("此操作不是单据 PDF 输出。"));
    }
    crate::operation::check()?;
    if cancelled.load(Ordering::Relaxed) {
        return Err(ApiError::local("已取消 PDF 输出。"));
    }
    let job: BackgroundJobSnapshot = client.json(operation, parameters, &[], Some(body))?;
    let parameters = [("jobId", job.job_id.clone())];
    let result = wait(client, job, cancelled, progress).and_then(|_| {
        let bytes = client.bytes(DOWNLOAD_JOB_RESULT, &parameters, &[], None, PDF_LIMIT)?;
        if !bytes.starts_with(b"%PDF-") {
            return Err(ApiError::local("服务返回的文件不是有效 PDF。"));
        }
        Ok(bytes)
    });
    let cleanup = OperationScope::new(Duration::from_secs(6)).run(|| {
        let deadline = Instant::now() + Duration::from_secs(5);
        loop {
            let job: BackgroundJobSnapshot = client.json(GET_JOB, &parameters, &[], None)?;
            if terminal(&job.status) {
                return client.json::<Value>(DELETE_JOB, &parameters, &[], None);
            }
            if Instant::now() >= deadline {
                return Err(ApiError::local(
                    "文件任务尚未结束，请稍后在文件任务中清理。",
                ));
            }
            thread::sleep(Duration::from_millis(100));
        }
    });
    match (result, cleanup) {
        (Ok(bytes), Ok(_)) => Ok(bytes),
        (Ok(_), Err(error)) => Err(ApiError::local(format!(
            "PDF 已生成，但临时任务清理失败：{error}"
        ))),
        (Err(error), _) => Err(error),
    }
}
