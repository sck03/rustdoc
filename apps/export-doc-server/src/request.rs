use crate::{ServerState, response};
mod backup;
pub(super) mod body;
use axum::{
    body::Body,
    extract::{FromRequestParts, Path, Request},
    http::Response,
};
use export_doc_contracts::{contracts, generated_api::*};
use export_doc_engine::{api::ApiError, operation::OperationScope};
use serde_json::Value;
use std::{collections::HashMap, time::Duration};

pub(super) fn error(status: u16, message: &str) -> ApiError {
    ApiError {
        status: Some(status),
        message: message.into(),
        diagnostic: (status >= 500).then_some("http-processing"),
    }
}

struct Cancellation {
    scope: OperationScope,
    _registration: crate::shutdown::Registration,
}
impl Drop for Cancellation {
    fn drop(&mut self) {
        self.scope.cancel();
    }
}

pub async fn handle(state: ServerState, operation: Operation, request: Request) -> Response<Body> {
    let mut span = state.telemetry.start(operation.id);
    let request_id = span.id().to_owned();
    let result = tokio::select! {
        result = tokio::time::timeout(Duration::from_secs(request_seconds(operation) + 5), execute(state.clone(), operation, request)) =>
            result.unwrap_or_else(|_| Err(error(504, "请求读取或处理超过时限。"))),
        _ = state.shutdown.wait() => Err(error(503, "服务正在关闭，已取消请求。")),
    };
    let mut response = match result {
        Ok(mut reply) => {
            if operation == GET_RUNTIME_METRICS {
                match serde_json::from_slice::<Value>(&reply.bytes) {
                    Ok(mut value) => {
                        value["http"] = state.telemetry.snapshot(
                            state.requests.available(),
                            state.bulk_uploads.available_permits(),
                            state.requests.queued(),
                        );
                        reply.bytes = value.to_string().into_bytes();
                        response::success(operation, reply, &state, &request_id)
                    }
                    Err(_) => response::error(error(503, "运行指标响应无效。")),
                }
            } else {
                response::success(operation, reply, &state, &request_id)
            }
        }
        Err(error) => {
            span.failure(&error);
            response::error(error)
        }
    };
    response.headers_mut().insert(
        "x-request-id",
        request_id.parse().expect("generated ASCII request id"),
    );
    span.finish(response.status().as_u16());
    response
}

async fn execute(
    state: ServerState,
    operation: Operation,
    request: Request,
) -> Result<response::Reply, ApiError> {
    let local_operation =
        export_doc_engine::engine::NativeService::requires_local_transport(operation);
    if local_operation && state.desktop_token.is_none() {
        return Err(error(403, "此操作只支持本机桌面调用。"));
    }
    if let Some(expected) = &state.desktop_token {
        // Read the official endpoint policy, never infer it from a URL prefix.
        let policy = &contracts::contract()["operations"][operation.id]["policy"];
        if local_operation || policy["requiresDesktopAccess"].as_bool().unwrap_or(true) {
            use subtle::ConstantTimeEq;
            let actual = request
                .headers()
                .get("x-exportdocmanager-desktop-token")
                .map(|value| value.as_bytes())
                .unwrap_or_default();
            if !bool::from(actual.ct_eq(expected.as_bytes())) {
                return Err(error(403, "桌面访问令牌无效，请从桌面程序打开。"));
            }
        }
    }
    let permit = state.requests.acquire(&state.telemetry).await?;
    let download_permit = if response::is_download(operation) {
        Some(
            state
                .downloads
                .clone()
                .try_acquire_owned()
                .map_err(|_| error(429, "下载正在处理，请稍后重试。"))?,
        )
    } else {
        None
    };
    let scope = OperationScope::new(Duration::from_secs(request_seconds(operation)));
    let registration = state.shutdown.register(scope.clone())?;
    let processing = if body::needs_processing_slot(operation) && !body::backup_upload(operation) {
        Some(
            state
                .processing
                .clone()
                .try_acquire_owned()
                .map_err(|_| error(429, "文件或图片正在处理，请稍后重试。"))?,
        )
    } else {
        None
    };
    let bulk_permit = if [
        UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB,
        STAGE_SERVER_MIGRATION_RESTORE,
        UPLOAD_AND_RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
    ]
    .contains(&operation)
    {
        Some(
            state
                .bulk_uploads
                .clone()
                .try_acquire_owned()
                .map_err(|_| {
                    state.telemetry.reject(true);
                    error(429, "大文件上传正在处理，请稍后重试。")
                })?,
        )
    } else {
        None
    };
    let _cancel = Cancellation {
        scope: scope.clone(),
        _registration: registration.clone(),
    };
    let (mut parts, body) = request.into_parts();
    let cookie_binding = crate::downloads::binding(&parts.headers);
    let secure_cookie = parts.uri.scheme_str() == Some("https")
        || parts
            .headers
            .get("origin")
            .and_then(|v| v.to_str().ok())
            .and_then(|v| url::Url::parse(v).ok())
            .is_some_and(|origin| origin.scheme() == "https");
    let token = parts
        .headers
        .get("authorization")
        .and_then(|v| v.to_str().ok())
        .and_then(|v| v.strip_prefix("Bearer "))
        .unwrap_or("")
        .to_owned();
    let bootstrap = parts
        .headers
        .get("x-exportdocmanager-bootstrap-token")
        .and_then(|v| v.to_str().ok())
        .unwrap_or("")
        .to_owned();
    if operation.requires_authentication && token.is_empty() {
        return Err(error(401, "请先登录。"));
    }
    let Path(mut parameters) =
        Path::<HashMap<String, String>>::from_request_parts(&mut parts, &state)
            .await
            .map_err(|_| error(400, "路由参数无效。"))?;
    for parameter in contracts::contract()["operations"][operation.id]["parameters"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|p| p["in"] == "header")
    {
        if let Some(name) = parameter["name"].as_str() {
            if let Some(value) = parts.headers.get(name) {
                parameters.insert(
                    name.to_owned(),
                    header_text(
                        name,
                        value.to_str().map_err(|_| error(400, "请求头格式无效。"))?,
                    )?,
                );
            }
        }
    }
    let query: Vec<(String, String)> =
        url::form_urlencoded::parse(parts.uri.query().unwrap_or("").as_bytes())
            .into_owned()
            .collect();
    if query.len() > 64 {
        return Err(error(400, "查询参数过多。"));
    }
    if operation.requires_authentication
        && !body::backup_upload(operation)
        && contracts::contract()["operations"][operation.id]["requestBody"].is_object()
    {
        let service = state.service.clone();
        let authentication_token = token.clone();
        let authentication_scope = scope.clone();
        let authentication_registration = registration.clone();
        tokio::task::spawn_blocking(move || {
            let _registration = authentication_registration;
            authentication_scope
                .run_result(|| service.session_actor(&authentication_token).map(|_| ()))
        })
        .await
        .map_err(|_| error(503, "上传会话验证中断。"))??;
    }
    let mut backup_upload = None;
    let parsed = if body::backup_upload(operation) {
        if parts
            .headers
            .get("content-type")
            .and_then(|value| value.to_str().ok())
            .is_none_or(|value| !value.starts_with("application/octet-stream"))
        {
            return Err(error(400, "备份上传须使用二进制文件请求。"));
        }
        let name = parameters
            .get(if operation == STAGE_SERVER_MIGRATION_RESTORE {
                "X-ExportDocManager-Migration-File-Name"
            } else {
                "X-ExportDocManager-PostgreSql-Backup-File-Name"
            })
            .cloned()
            .or_else(|| {
                query
                    .iter()
                    .find(|(key, _)| key == "fileName")
                    .map(|(_, value)| value.clone())
            })
            .unwrap_or_default();
        let service = state.service.clone();
        let token = token.clone();
        let parameters: Vec<_> = parameters
            .iter()
            .map(|(key, value)| (key.clone(), value.clone()))
            .collect();
        let upload_scope = scope.clone();
        let authorization = tokio::task::spawn_blocking(move || {
            upload_scope.run_result(|| {
                service.begin_backup_upload(
                    operation,
                    &parameters
                        .iter()
                        .map(|(key, value)| (key.as_str(), value.clone()))
                        .collect::<Vec<_>>(),
                    &name,
                    &token,
                )
            })
        })
        .await
        .map_err(|_| error(503, "上传授权任务中断。"))??;
        let file = backup::receive(body, &state.service.paths.cache_root).await?;
        backup_upload = Some((authorization, file));
        body::Parsed {
            body: None,
            upload: None,
            merge_uploads: vec![],
        }
    } else {
        body::parse(
            operation,
            Request::from_parts(parts, body),
            &parameters,
            &query,
        )
        .await?
    };
    let body::Parsed {
        body,
        upload,
        merge_uploads,
    } = parsed;
    let worker = tokio::task::spawn_blocking(move || {
        let _registration = registration;
        let _permit = permit;
        let _processing = processing;
        scope
            .run_result(|| {
                let parameters: Vec<_> = parameters
                    .iter()
                    .map(|(k, v)| (k.as_str(), v.clone()))
                    .collect();
                let query: Vec<_> = query.iter().map(|(k, v)| (k.as_str(), v.clone())).collect();
                if operation == PREVIEW_OCR_IMAGE {
                    return state
                        .service
                        .preview_ocr_image(&query, &token)
                        .map(response::Reply::file);
                }
                if operation == DOWNLOAD_JOB_RESULT_WITH_TICKET {
                    let ticket = parameters
                        .iter()
                        .find(|(name, _)| *name == "token")
                        .map(|(_, value)| value.as_str())
                        .unwrap_or("");
                    let (id, session) = state.tickets.resolve(ticket, &cookie_binding)?;
                    return state
                        .service
                        .prepare_job_download(&id, &session)
                        .map(response::Reply::download);
                }
                if [
                    DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
                    DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET,
                ]
                .contains(&operation)
                {
                    let ticket = parameters
                        .iter()
                        .find(|(name, _)| *name == "token")
                        .map(|(_, value)| value.as_str())
                        .unwrap_or("");
                    let (token, session) =
                        state
                            .tickets
                            .resolve_for(operation, ticket, &cookie_binding)?;
                    return state
                        .service
                        .prepare_backup_download(operation, &[("token", token)], &session)
                        .map(response::Reply::download);
                }
                if [DOWNLOAD_JOB_RESULT, CREATE_JOB_DOWNLOAD_TICKET].contains(&operation) {
                    let id = parameters
                        .iter()
                        .find(|(name, _)| *name == "jobId")
                        .map(|(_, value)| value.as_str())
                        .unwrap_or("");
                    if operation == DOWNLOAD_JOB_RESULT {
                        return state
                            .service
                            .prepare_job_download(id, &token)
                            .map(response::Reply::download);
                    }
                    state.service.authorize_job_download(id, &token)?;
                    let (ticket, cookie) =
                        state
                            .tickets
                            .issue(id, &token, &cookie_binding, secure_cookie)?;
                    return Ok(response::Reply {
                        bytes: serde_json::to_vec(&ticket)
                            .map_err(|_| error(503, "下载票据编码失败。"))?,
                        file: None,
                        cookie: Some(cookie),
                        stream: None,
                        download_permit: None,
                        memory_reservation: None,
                    });
                }
                let _bulk_permit = bulk_permit;
                if let Some((authorization, file)) = backup_upload {
                    return serde_json::to_vec(&state.service.finish_backup_upload(
                        authorization,
                        file,
                        &token,
                    )?)
                    .map(response::Reply::bytes)
                    .map_err(|_| error(503, "上传结果编码失败。"));
                }
                if operation == UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
                    return serde_json::to_vec(
                        &state.service.upload_pdf_merge(merge_uploads, &token)?,
                    )
                    .map(response::Reply::bytes)
                    .map_err(|_| error(503, "文件任务响应编码失败。"));
                }
                if let Some((name, bytes)) = upload {
                    let value = state.service.upload(
                        operation,
                        &parameters,
                        body.unwrap_or(Value::Null),
                        &name,
                        &bytes,
                        &token,
                    )?;
                    serde_json::to_vec(&value)
                        .map(response::Reply::bytes)
                        .map_err(|_| error(503, "上传结果编码失败。"))
                } else {
                    if operation == DOWNLOAD_REPORT_TEMPLATE_FILE {
                        let request: DownloadReportTemplateFileBody = serde_json::from_value(
                            body.ok_or_else(|| error(400, "缺少模板下载请求。"))?,
                        )
                        .map_err(|_| error(400, "模板下载请求格式无效。"))?;
                        return state
                            .service
                            .download_file(
                                operation,
                                &[
                                    ("reportType", request.report_type.unwrap_or_default()),
                                    ("templatePath", request.template_path.unwrap_or_default()),
                                ],
                                &token,
                            )
                            .map(response::Reply::file);
                    }
                    if operation == DOWNLOAD_PERSONNEL_ATTACHMENT
                        || export_doc_engine::engine::oa::is_download(operation)
                        || export_doc_engine::engine::communication::is_download(operation)
                        || [DOWNLOAD_REPORT_TEMPLATE_PACKAGE, DOWNLOAD_SUPPORT_PACKAGE]
                            .contains(&operation)
                    {
                        return state
                            .service
                            .download_file(operation, &parameters, &token)
                            .map(response::Reply::file);
                    }
                    if operation == DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE {
                        return state
                            .service
                            .download_report_resource(&parameters, &token)
                            .map(response::Reply::file);
                    }
                    let result = state.service.dispatch_with_bootstrap(
                        operation,
                        &parameters,
                        &query,
                        body,
                        &token,
                        &bootstrap,
                    )?;
                    if [
                        CREATE_POSTGRE_SQL_PHYSICAL_BACKUP_DOWNLOAD_TICKET,
                        CREATE_RECOVERY_ARCHIVE_DOWNLOAD_TICKET,
                    ]
                    .contains(&operation)
                    {
                        let original: Value = serde_json::from_slice(&result)
                            .map_err(|_| error(503, "备份下载票据无效。"))?;
                        let id = original["token"]
                            .as_str()
                            .ok_or_else(|| error(503, "备份下载票据缺失。"))?;
                        let (ticket, cookie) = state.tickets.issue_for(
                            if operation == CREATE_RECOVERY_ARCHIVE_DOWNLOAD_TICKET {
                                DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET
                            } else {
                                DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET
                            },
                            id,
                            &token,
                            &cookie_binding,
                            secure_cookie,
                        )?;
                        return Ok(response::Reply {
                            bytes: serde_json::to_vec(&ticket)
                                .map_err(|_| error(503, "票据编码失败。"))?,
                            file: None,
                            cookie: Some(cookie),
                            stream: None,
                            download_permit: None,
                            memory_reservation: None,
                        });
                    }
                    if operation == LOGOUT {
                        state.tickets.revoke(&token)?;
                    }
                    if operation == DOWNLOAD_CONTAINER_PACKING_PDF {
                        return Ok(response::Reply::file(
                            export_doc_engine::engine::tasks::FileOutput {
                                file_name: "装柜现场作业单.pdf".into(),
                                media_type: "application/pdf".into(),
                                content: result,
                            },
                        ));
                    }
                    if operation == EXPORT_HS_CODE_KNOWLEDGE {
                        return Ok(response::Reply::file(
                            export_doc_engine::engine::tasks::FileOutput {
                                file_name: "HS知识库.zip".into(),
                                media_type: "application/vnd.exportdocmanager.hs-knowledge+zip"
                                    .into(),
                                content: result,
                            },
                        ));
                    }
                    if operation == DOWNLOAD_INVOICE_TRANSFER_PACKAGE {
                        return Ok(response::Reply::file(
                            export_doc_engine::engine::tasks::FileOutput {
                                file_name: "单据.edpkg".into(),
                                media_type: "application/zip".into(),
                                content: result,
                            },
                        ));
                    }
                    if [
                        DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE,
                        DOWNLOAD_AGENT_CONSIGNMENT_SUBMIT_PACKAGE,
                        DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
                    ]
                    .contains(&operation)
                    {
                        return Ok(response::Reply::file(
                            export_doc_engine::engine::tasks::FileOutput {
                                file_name: if operation == DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE {
                                    "单一窗口回执包.zip"
                                } else {
                                    "单一窗口提交包.zip"
                                }
                                .into(),
                                media_type: "application/zip".into(),
                                content: result,
                            },
                        ));
                    }
                    if [DOWNLOAD_AUDIT_LOGS, EXPORT_CRM_CUSTOMERS, EXPORT_SUPPLIERS]
                        .contains(&operation)
                    {
                        return Ok(response::Reply::file(
                        export_doc_engine::engine::tasks::FileOutput {
                            file_name: match operation {
                                EXPORT_CRM_CUSTOMERS => "CRM客户.xlsx",
                                EXPORT_SUPPLIERS => "供应商.xlsx",
                                _ => "AuditLogs.xlsx",
                            }
                            .into(),
                            media_type:
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                                    .into(),
                            content: result,
                        },
                    ));
                    }
                    Ok(response::Reply::bytes(result))
                }
            })
            .map(|mut reply| {
                reply.download_permit = download_permit;
                reply
            })
    });
    tokio::time::timeout(Duration::from_secs(request_seconds(operation) + 2), worker)
        .await
        .map_err(|_| error(504, "请求超过时限，服务正在取消操作。"))?
        .map_err(|_| error(503, "应用服务意外中断。"))?
}

fn header_text(name: &str, value: &str) -> Result<String, ApiError> {
    if ![
        "X-ExportDocManager-Migration-Password",
        "X-ExportDocManager-Migration-File-Name",
        "X-ExportDocManager-PostgreSql-Backup-File-Name",
    ]
    .contains(&name)
    {
        return Ok(value.into());
    }
    let Some(encoded) = value.strip_prefix("UTF-8''") else {
        return Ok(value.into());
    };
    let bytes = encoded.as_bytes();
    let mut result = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'%' {
            let hex = encoded
                .get(index + 1..index + 3)
                .ok_or_else(|| error(400, "请求头编码不完整。"))?;
            result.push(u8::from_str_radix(hex, 16).map_err(|_| error(400, "请求头编码无效。"))?);
            index += 3;
        } else {
            result.push(bytes[index]);
            index += 1;
        }
    }
    String::from_utf8(result).map_err(|_| error(400, "请求头不是有效 UTF-8 文本。"))
}

#[cfg(test)]
mod header_tests {
    use super::*;
    #[test]
    fn backup_header_unicode_and_literal_password_characters_roundtrip() {
        let name = "X-ExportDocManager-Migration-Password";
        assert_eq!(
            header_text(name, "UTF-8''%E4%B8%AD%E6%96%87%20%2B%25").unwrap(),
            "中文 +%"
        );
        assert_eq!(header_text(name, "plain+%41").unwrap(), "plain+%41");
        assert!(header_text(name, "UTF-8''%GG").is_err());
        assert!(header_text(name, "UTF-8''%FF").is_err());
    }
}

fn request_seconds(operation: Operation) -> u64 {
    if body::backup_upload(operation)
        || [
            DOWNLOAD_JOB_RESULT,
            DOWNLOAD_JOB_RESULT_WITH_TICKET,
            DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
            DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET,
            RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
        ]
        .contains(&operation)
    {
        return export_doc_engine::engine::backup_io::TIMEOUT_SECONDS;
    }
    if [
        UPLOAD_LETTER_OF_CREDIT_DOCUMENT,
        IMPORT_LETTER_OF_CREDIT_DOCUMENT,
    ]
    .contains(&operation)
    {
        600
    } else if operation == REVIEW_LETTER_OF_CREDIT_COMPLIANCE {
        125
    } else if [UPLOAD_OCR_IMAGE, RECOGNIZE_OCR_IMAGE].contains(&operation) {
        95
    } else if [SEND_EMAIL, TEST_EMAIL_CONNECTION].contains(&operation) {
        65
    } else {
        30
    }
}
