use axum::{
    Json,
    body::Body,
    http::{Response, StatusCode, header},
    response::IntoResponse,
};
use export_doc_contracts::{contracts, generated_api::Operation};
use export_doc_engine::api::ApiError;
use export_doc_engine::engine::tasks::FileOutput;
use serde_json::json;

pub struct Reply {
    pub bytes: Vec<u8>,
    pub file: Option<(String, String)>,
    pub cookie: Option<String>,
}
impl Reply {
    pub fn bytes(bytes: Vec<u8>) -> Self {
        Self {
            bytes,
            file: None,
            cookie: None,
        }
    }
    pub fn file(file: FileOutput) -> Self {
        Self {
            bytes: file.content,
            file: Some((file.file_name, file.media_type)),
            cookie: None,
        }
    }
}

pub fn error(error: ApiError) -> Response<Body> {
    let status = StatusCode::from_u16(error.status.unwrap_or(503))
        .unwrap_or(StatusCode::SERVICE_UNAVAILABLE);
    let message = if status.is_server_error()
        && status != StatusCode::NOT_IMPLEMENTED
        && status != StatusCode::GATEWAY_TIMEOUT
    {
        "服务依赖暂不可用，请稍后重试或联系管理员。".to_owned()
    } else {
        error.message
    };
    let mut response = (status, Json(json!({"message":message}))).into_response();
    response
        .headers_mut()
        .insert(header::CACHE_CONTROL, "no-store".parse().unwrap());
    if status == StatusCode::TOO_MANY_REQUESTS {
        response
            .headers_mut()
            .insert(header::RETRY_AFTER, "5".parse().unwrap());
    }
    response
}

pub fn success(operation: Operation, reply: Reply) -> Response<Body> {
    let responses = &contracts::contract()["operations"][operation.id]["responses"];
    let success = responses
        .as_object()
        .and_then(|values| values.iter().find(|(code, _)| code.starts_with('2')));
    let content_type = success
        .and_then(|(_, r)| r["content"].as_object())
        .and_then(|m| m.keys().next())
        .map(String::as_str)
        .unwrap_or("application/json");
    let mut response = Response::new(Body::from(reply.bytes));
    if let Some((status, _)) = success {
        if let Ok(status) = status.parse::<u16>() {
            *response.status_mut() = StatusCode::from_u16(status).unwrap_or(StatusCode::OK);
        }
    }
    let content_type = reply
        .file
        .as_ref()
        .map(|(_, content_type)| content_type.as_str())
        .unwrap_or(content_type);
    response.headers_mut().insert(
        header::CONTENT_TYPE,
        content_type
            .parse()
            .unwrap_or_else(|_| "application/octet-stream".parse().unwrap()),
    );
    response
        .headers_mut()
        .insert(header::CACHE_CONTROL, "no-store".parse().unwrap());
    response
        .headers_mut()
        .insert(header::X_CONTENT_TYPE_OPTIONS, "nosniff".parse().unwrap());
    if let Some((filename, _)) = reply.file {
        let encoded: String = filename
            .as_bytes()
            .iter()
            .map(|byte| format!("%{byte:02X}"))
            .collect();
        let value = format!("attachment; filename=\"download\"; filename*=UTF-8''{encoded}");
        if let Ok(value) = value.parse() {
            response
                .headers_mut()
                .insert(header::CONTENT_DISPOSITION, value);
        }
    }
    if let Some(cookie) = reply.cookie {
        if let Ok(cookie) = cookie.parse() {
            response.headers_mut().insert(header::SET_COOKIE, cookie);
        }
    }
    response
}
