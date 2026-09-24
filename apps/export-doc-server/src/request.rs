use crate::{ServerState, response};
use axum::{
    body::{Body, to_bytes},
    extract::{FromRequest, FromRequestParts, Multipart, Path, Request},
    http::Response,
};
use export_doc_contracts::{contracts, generated_api::*};
use export_doc_engine::{api::ApiError, operation::OperationScope};
use serde_json::{Map, Value};
use std::{collections::HashMap, time::Duration};

fn error(status: u16, message: &str) -> ApiError {
    ApiError {
        status: Some(status),
        message: message.into(),
    }
}

struct Cancellation(OperationScope);
impl Drop for Cancellation {
    fn drop(&mut self) {
        self.0.cancel();
    }
}

pub async fn handle(state: ServerState, operation: Operation, request: Request) -> Response<Body> {
    match tokio::time::timeout(
        Duration::from_secs(request_seconds(operation) + 5),
        execute(state, operation, request),
    )
    .await
    .unwrap_or_else(|_| Err(error(504, "请求读取或处理超过时限。")))
    {
        Ok(bytes) => response::success(operation, bytes),
        Err(error) => response::error(error),
    }
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
    let permit = state
        .requests
        .clone()
        .try_acquire_owned()
        .map_err(|_| error(429, "当前请求较多，请稍后重试。"))?;
    let scope = OperationScope::new(Duration::from_secs(request_seconds(operation)));
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
                .map_err(|_| error(429, "大文件上传正在处理，请稍后重试。"))?,
        )
    } else {
        None
    };
    let _cancel = Cancellation(scope.clone());
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
    let multipart = parts
        .headers
        .get("content-type")
        .and_then(|v| v.to_str().ok())
        .is_some_and(|v| v.starts_with("multipart/form-data"));
    let binary = contracts::contract()["operations"][operation.id]["requestBody"]["content"]
        .get("application/octet-stream")
        .is_some();
    let mut upload = None;
    let mut merge_uploads = Vec::new();
    let mut merge_bytes = 0usize;
    let body = if binary && !multipart {
        let filename = query
            .iter()
            .find(|(key, _)| key == "fileName" || key == "sourceName")
            .map(|(_, value)| value.clone())
            .or_else(|| {
                parameters
                    .get(if operation == STAGE_SERVER_MIGRATION_RESTORE {
                        "X-ExportDocManager-Migration-File-Name"
                    } else {
                        "X-ExportDocManager-PostgreSql-Backup-File-Name"
                    })
                    .cloned()
            })
            .unwrap_or_default();
        let limit = if [
            STAGE_SERVER_MIGRATION_RESTORE,
            UPLOAD_AND_RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
        ]
        .contains(&operation)
        {
            257
        } else if [
            IMPORT_HS_CODE_KNOWLEDGE,
            UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
            UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
        ]
        .contains(&operation)
        {
            100
        } else if operation == UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE {
            32
        } else {
            25
        };
        let bytes = to_bytes(body, limit * 1024 * 1024)
            .await
            .map_err(|_| error(413, "上传超过该操作的容量上限。"))?;
        upload = Some((filename, bytes.to_vec()));
        let mut metadata = Map::new();
        for parameter in contracts::contract()["operations"][operation.id]["parameters"]
            .as_array()
            .into_iter()
            .flatten()
        {
            let Some(name) = parameter["name"]
                .as_str()
                .filter(|name| *name != "fileName")
            else {
                continue;
            };
            if parameter["in"] != "query" {
                continue;
            }
            if let Some((_, value)) = query.iter().find(|(key, _)| key == name) {
                let value = match contracts::kind(&parameter["schema"]) {
                    "integer" => Value::from(
                        value
                            .parse::<i64>()
                            .map_err(|_| error(400, "上传参数必须是整数。"))?,
                    ),
                    "boolean" => Value::from(
                        value
                            .parse::<bool>()
                            .map_err(|_| error(400, "上传参数必须是布尔值。"))?,
                    ),
                    _ => Value::String(value.clone()),
                };
                metadata.insert(name.into(), value);
            }
        }
        Some(Value::Object(metadata))
    } else if multipart {
        let mut form = Multipart::from_request(Request::from_parts(parts, body), &state)
            .await
            .map_err(|_| error(400, "上传表单无效。"))?;
        let mut metadata = Map::new();
        while let Some(field) = form
            .next_field()
            .await
            .map_err(|_| error(400, "上传读取失败或内容超出上限。"))?
        {
            let name = field
                .name()
                .ok_or_else(|| error(400, "上传字段缺少名称。"))?
                .to_owned();
            if let Some(filename) = field.file_name().map(str::to_owned) {
                if upload.is_some() && operation != UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
                    return Err(error(400, "每次只能上传一个文件。"));
                }
                let bytes = field
                    .bytes()
                    .await
                    .map_err(|_| error(400, "文件读取失败或超过容量。"))?;
                if operation == UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
                    merge_bytes = merge_bytes
                        .checked_add(bytes.len())
                        .filter(|n| *n <= export_doc_engine::pdf::MAX_MERGE_INPUT)
                        .ok_or_else(|| error(413, "PDF 总大小超过 128 MiB。"))?;
                    if merge_uploads.len() >= 100 {
                        return Err(error(400, "每次最多合并 100 个 PDF 文件。"));
                    }
                    merge_uploads.push(export_doc_engine::engine::tasks::FileOutput {
                        file_name: filename,
                        media_type: "application/pdf".into(),
                        content: bytes.to_vec(),
                    });
                    continue;
                }
                if bytes.len()
                    > (if [
                        IMPORT_HS_CODE_KNOWLEDGE,
                        UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
                        UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
                    ]
                    .contains(&operation)
                    {
                        100
                    } else {
                        16
                    }) * 1024
                        * 1024
                {
                    return Err(error(413, "文件超过该操作的容量上限。"));
                }
                upload = Some((filename, bytes.to_vec()));
            } else {
                let value = field
                    .text()
                    .await
                    .map_err(|_| error(400, "上传参数编码无效。"))?;
                if value.len() > 65536 || metadata.len() >= 32 || metadata.contains_key(&name) {
                    return Err(error(400, "上传参数重复或超过容量。"));
                }
                let schema = &contracts::contract()["operations"][operation.id]["requestBody"]["content"]
                    ["multipart/form-data"]["schema"];
                let property = &contracts::resolve(schema)["properties"][&name];
                let value = match contracts::kind(property) {
                    "integer" => Value::from(
                        value
                            .parse::<i64>()
                            .map_err(|_| error(400, "上传编号必须是整数。"))?,
                    ),
                    "boolean" => Value::from(
                        value
                            .parse::<bool>()
                            .map_err(|_| error(400, "上传开关无效。"))?,
                    ),
                    _ => Value::String(value),
                };
                metadata.insert(name, value);
            }
        }
        Some(Value::Object(metadata))
    } else {
        let bytes = to_bytes(body, 16 * 1024 * 1024)
            .await
            .map_err(|_| error(413, "请求超过容量上限。"))?;
        if bytes.is_empty() {
            None
        } else {
            Some(serde_json::from_slice(&bytes).map_err(|_| error(400, "请求必须是有效 JSON。"))?)
        }
    };
    let worker = tokio::task::spawn_blocking(move || {
        let _permit = permit;
        scope.run(|| {
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
                    .download_job(&id, &session)
                    .map(response::Reply::file);
            }
            if operation == DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET {
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
                    .download_file(operation, &[("token", token)], &session)
                    .map(response::Reply::file);
            }
            if [DOWNLOAD_JOB_RESULT, CREATE_JOB_DOWNLOAD_TICKET].contains(&operation) {
                let id = parameters
                    .iter()
                    .find(|(name, _)| *name == "jobId")
                    .map(|(_, value)| value.as_str())
                    .unwrap_or("");
                let file = state.service.download_job(id, &token)?;
                if operation == DOWNLOAD_JOB_RESULT {
                    return Ok(response::Reply::file(file));
                }
                let (ticket, cookie) =
                    state
                        .tickets
                        .issue(id, &token, &cookie_binding, secure_cookie)?;
                return Ok(response::Reply {
                    bytes: serde_json::to_vec(&ticket)
                        .map_err(|_| error(503, "下载票据编码失败。"))?,
                    file: None,
                    cookie: Some(cookie),
                });
            }
            let _bulk_permit = bulk_permit;
            if operation == UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
                return serde_json::to_vec(&state.service.upload_pdf_merge(merge_uploads, &token)?)
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
                if [
                    DOWNLOAD_REPORT_TEMPLATE_FILE,
                    DOWNLOAD_REPORT_TEMPLATE_PACKAGE,
                    DOWNLOAD_SUPPORT_PACKAGE,
                    DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
                ]
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
                if operation == CREATE_POSTGRE_SQL_PHYSICAL_BACKUP_DOWNLOAD_TICKET {
                    let original: Value = serde_json::from_slice(&result)
                        .map_err(|_| error(503, "备份下载票据无效。"))?;
                    let id = original["token"]
                        .as_str()
                        .ok_or_else(|| error(503, "备份下载票据缺失。"))?;
                    let (ticket, cookie) = state.tickets.issue_for(
                        DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
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
                            media_type: "application/vnd.exportdocmanager.hs-knowledge+zip".into(),
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
