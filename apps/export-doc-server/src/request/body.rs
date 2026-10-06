//! Decode request bodies at the HTTP boundary, enforcing limits while reading.
use super::error;
use axum::{
    body::{Body, Bytes},
    extract::{DefaultBodyLimit, FromRequest, Multipart, Request, multipart::Field},
};
use export_doc_contracts::{contracts, generated_api::*};
use export_doc_engine::{api::ApiError, engine::tasks::FileOutput};
use serde_json::{Map, Value};
use std::collections::HashMap;

const MIB: usize = 1024 * 1024;
const MAX_FIELD: usize = 65536;
pub(super) struct Parsed {
    pub body: Option<Value>,
    pub upload: Option<(String, Vec<u8>)>,
    pub merge_uploads: Vec<FileOutput>,
}

fn catalog_upload(operation: Operation) -> bool {
    [
        IMPORT_HS_CODE_KNOWLEDGE,
        UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
        UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
    ]
    .contains(&operation)
}

fn backup_upload(operation: Operation) -> bool {
    [
        STAGE_SERVER_MIGRATION_RESTORE,
        UPLOAD_AND_RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
    ]
    .contains(&operation)
}

pub(crate) fn envelope_limit(operation: Operation) -> usize {
    (if backup_upload(operation) {
        257
    } else if operation == UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
        129
    } else if catalog_upload(operation) {
        101
    } else {
        33
    }) * MIB
}

fn parameter(value: String, schema: &Value) -> Result<Value, ApiError> {
    match contracts::kind(schema) {
        "integer" => value
            .parse::<i64>()
            .map(Value::from)
            .map_err(|_| error(400, "上传参数必须是整数。")),
        "boolean" => value
            .parse::<bool>()
            .map(Value::from)
            .map_err(|_| error(400, "上传参数必须是布尔值。")),
        _ => Ok(Value::String(value)),
    }
}

async fn bytes(body: Body, limit: usize) -> Result<Bytes, ApiError> {
    let mut request = Request::new(body);
    DefaultBodyLimit::max(limit).apply(&mut request);
    Bytes::from_request(request, &()).await.map_err(|cause| {
        if cause.status().as_u16() == 413 {
            error(413, "请求超过该操作的容量上限。")
        } else {
            error(400, "请求内容读取失败。")
        }
    })
}

fn multipart_error(cause: axum::extract::multipart::MultipartError) -> ApiError {
    if cause.status().as_u16() == 413 {
        error(413, "上传超过该操作的容量上限。")
    } else {
        error(400, "上传内容读取失败或格式无效。")
    }
}

async fn field_bytes(mut field: Field<'_>, limit: usize, status: u16) -> Result<Vec<u8>, ApiError> {
    let mut bytes = Vec::new();
    while let Some(chunk) = field.chunk().await.map_err(multipart_error)? {
        if chunk.len() > limit.saturating_sub(bytes.len()) {
            return Err(error(status, "上传字段超过该操作的容量上限。"));
        }
        bytes.extend_from_slice(&chunk);
    }
    Ok(bytes)
}

pub(super) async fn parse(
    operation: Operation,
    request: Request,
    parameters: &HashMap<String, String>,
    query: &[(String, String)],
) -> Result<Parsed, ApiError> {
    let multipart = request
        .headers()
        .get("content-type")
        .and_then(|v| v.to_str().ok())
        .is_some_and(|v| v.starts_with("multipart/form-data"));
    let contract = &contracts::contract()["operations"][operation.id];
    let binary = contract["requestBody"]["content"]
        .get("application/octet-stream")
        .is_some();
    let mut result = Parsed {
        body: None,
        upload: None,
        merge_uploads: Vec::new(),
    };
    if multipart {
        let mut form = Multipart::from_request(request, &())
            .await
            .map_err(|_| error(400, "上传表单无效。"))?;
        let mut metadata = Map::new();
        let mut remaining = export_doc_engine::pdf::MAX_MERGE_INPUT;
        while let Some(field) = form.next_field().await.map_err(multipart_error)? {
            let name = field
                .name()
                .ok_or_else(|| error(400, "上传字段缺少名称。"))?
                .to_owned();
            if let Some(filename) = field.file_name().map(str::to_owned) {
                if operation == UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
                    if result.merge_uploads.len() >= 100 {
                        return Err(error(400, "每次最多合并 100 个 PDF 文件。"));
                    }
                    let content = field_bytes(field, remaining, 413).await?;
                    remaining -= content.len();
                    result.merge_uploads.push(FileOutput {
                        file_name: filename,
                        media_type: "application/pdf".into(),
                        content,
                    });
                } else {
                    if result.upload.is_some() {
                        return Err(error(400, "每次只能上传一个文件。"));
                    }
                    let limit = if catalog_upload(operation) { 100 } else { 16 } * MIB;
                    result.upload = Some((filename, field_bytes(field, limit, 413).await?));
                }
            } else {
                if metadata.len() >= 32 || metadata.contains_key(&name) {
                    return Err(error(400, "上传参数重复或超过容量。"));
                }
                let value = String::from_utf8(field_bytes(field, MAX_FIELD, 400).await?)
                    .map_err(|_| error(400, "上传参数编码无效。"))?;
                let schema = &contract["requestBody"]["content"]["multipart/form-data"]["schema"];
                metadata.insert(
                    name.clone(),
                    parameter(value, &contracts::resolve(schema)["properties"][&name])?,
                );
            }
        }
        result.body = Some(Value::Object(metadata));
    } else if binary {
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
        let limit = if backup_upload(operation) {
            257
        } else if catalog_upload(operation) {
            100
        } else if operation == UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE {
            32
        } else {
            25
        } * MIB;
        result.upload = Some((filename, bytes(request.into_body(), limit).await?.to_vec()));
        let mut metadata = Map::new();
        for property in contract["parameters"].as_array().into_iter().flatten() {
            let Some(name) = property["name"].as_str().filter(|name| *name != "fileName") else {
                continue;
            };
            if property["in"] != "query" {
                continue;
            }
            if let Some((_, value)) = query.iter().find(|(key, _)| key == name) {
                metadata.insert(name.into(), parameter(value.clone(), &property["schema"])?);
            }
        }
        result.body = Some(Value::Object(metadata));
    } else {
        let bytes = bytes(request.into_body(), 16 * MIB).await?;
        if !bytes.is_empty() {
            result.body = Some(
                serde_json::from_slice(&bytes).map_err(|_| error(400, "请求必须是有效 JSON。"))?,
            );
        }
    }
    Ok(result)
}
