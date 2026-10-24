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
    pub stream: Option<(std::fs::File, u64)>,
    pub download_permit: Option<tokio::sync::OwnedSemaphorePermit>,
    pub memory_reservation: Option<export_doc_engine::engine::backup_io::Reservation>,
}
impl Reply {
    pub fn bytes(bytes: Vec<u8>) -> Self {
        Self {
            bytes,
            file: None,
            cookie: None,
            stream: None,
            download_permit: None,
            memory_reservation: None,
        }
    }
    pub fn file(file: FileOutput) -> Self {
        Self {
            bytes: file.content,
            file: Some((file.file_name, file.media_type)),
            cookie: None,
            stream: None,
            download_permit: None,
            memory_reservation: None,
        }
    }
    pub fn download(download: export_doc_engine::engine::backup_io::Download) -> Self {
        match download {
            export_doc_engine::engine::backup_io::Download::Memory {
                output,
                reservation,
            } => {
                let mut reply = Self::file(output);
                reply.memory_reservation = Some(reservation);
                reply
            }
            export_doc_engine::engine::backup_io::Download::File {
                file_name,
                media_type,
                file,
                size,
            } => Self {
                bytes: vec![],
                file: Some((file_name, media_type)),
                cookie: None,
                stream: Some((file, size)),
                download_permit: None,
                memory_reservation: None,
            },
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

pub fn is_download(operation: Operation) -> bool {
    contracts::contract()["operations"][operation.id]["responses"]
        .as_object()
        .into_iter()
        .flat_map(|m| m.iter())
        .filter(|(code, _)| code.starts_with('2'))
        .any(|(_, response)| {
            response["content"]
                .as_object()
                .is_some_and(|m| m.keys().any(|mime| !mime.contains("json")))
        })
}

pub fn success(
    operation: Operation,
    mut reply: Reply,
    state: &crate::ServerState,
    request_id: &str,
) -> Response<Body> {
    let responses = &contracts::contract()["operations"][operation.id]["responses"];
    let success = responses
        .as_object()
        .and_then(|values| values.iter().find(|(code, _)| code.starts_with('2')));
    let content_type = success
        .and_then(|(_, r)| r["content"].as_object())
        .and_then(|m| m.keys().next())
        .map(String::as_str)
        .unwrap_or("application/json");
    let mut response = if reply.stream.is_some() || reply.file.is_some() {
        use tokio::io::AsyncReadExt;
        let permit = match reply
            .download_permit
            .take()
            .or_else(|| state.downloads.clone().try_acquire_owned().ok())
        {
            Some(permit) => permit,
            None => return error(crate::request::error(429, "下载正在处理，请稍后重试。")),
        };
        let (source, size): (Box<dyn tokio::io::AsyncRead + Unpin + Send>, u64) =
            if let Some((file, size)) = reply.stream {
                (Box::new(tokio::fs::File::from_std(file).take(size)), size)
            } else {
                let size = reply.bytes.len() as u64;
                (
                    crate::download_body::memory_source(reply.bytes, reply.memory_reservation),
                    size,
                )
            };
        let mut response = Response::new(crate::download_body::body(
            source,
            size,
            permit,
            state.telemetry.clone(),
            request_id.to_owned(),
        ));
        response
            .headers_mut()
            .insert(header::CONTENT_LENGTH, size.into());
        response
    } else {
        Response::new(Body::from(reply.bytes))
    };
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
