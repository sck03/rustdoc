use super::{
    NativeService,
    error::{Result, invalid, unavailable},
    settings,
};
use crate::{api::ApiError, controlled_process};
use serde_json::{Value, json};
use std::{process::Command, time::Duration};
pub fn review(service: &NativeService, body: &Value) -> Result<Value> {
    export_doc_contracts::validation::structure(
        crate::contracts::schema("ApiInvoiceDetailDto"),
        &body["invoice"],
    )
    .map_err(invalid)?;
    let review = export_doc_domain::letter_of_credit::context(&body["invoice"]).map_err(invalid)?;
    let settings = settings::current(&service.store)?;
    let ai = &settings["ai"];
    let text = |key| ai[key].as_str().unwrap_or("").to_owned();
    let request = export_doc_ai::Request {
        endpoint: text("apiEndpoint"),
        model: text("modelName"),
        api_key: settings::credential(&service.store, &service.protector, "/ai/apiKey")?
            .to_string(),
        system_prompt: text("systemPrompt"),
        content: review.content,
    };
    export_doc_ai::endpoint(&request.endpoint).map_err(|e| ApiError {
        status: Some(e.status),
        message: e.message,
    })?;
    let mut command = Command::new(std::env::current_exe()?);
    command
        .arg("--ai-review-worker")
        .current_dir(&service.paths.cache_root)
        .env_remove("EXPORTDOCMANAGER_MASTER_KEY");
    let output = controlled_process::run(
        &mut command,
        serde_json::to_vec(&request)?,
        5 * 1024 * 1024,
        Duration::from_secs(120),
    )?;
    if !output.status.success() {
        return Err(unavailable("AI 工作进程未能完成请求。"));
    }
    let report: std::result::Result<String, export_doc_ai::Error> =
        serde_json::from_slice(&output.stdout)
            .map_err(|_| unavailable("AI 工作进程返回无效响应。"))?;
    let report = report.map_err(|e| ApiError {
        status: Some(e.status),
        message: e.message,
    })?;
    Ok(
        json!({"reportText":report,"contextSummary":review.summary,"letterOfCreditContentTruncated":review.truncated,"storagePolicy":"只向系统设置中的 AI 服务提交当前发票／信用证草稿，审查结果不自动写入发票。"}),
    )
}
