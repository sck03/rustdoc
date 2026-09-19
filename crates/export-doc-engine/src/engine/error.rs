use crate::api::ApiError;

pub type Result<T> = std::result::Result<T, ApiError>;
pub fn error(status: u16, message: impl Into<String>) -> ApiError {
    ApiError {
        status: Some(status),
        message: message.into(),
    }
}
pub fn invalid(message: impl Into<String>) -> ApiError {
    error(400, message)
}
pub fn conflict(message: impl Into<String>) -> ApiError {
    error(409, message)
}
pub fn unavailable(message: impl Into<String>) -> ApiError {
    error(503, message)
}
pub fn unsupported(message: impl Into<String>) -> ApiError {
    error(501, message)
}

impl From<export_doc_report::Error> for ApiError {
    fn from(value: export_doc_report::Error) -> Self {
        use export_doc_report::ErrorKind;
        error(
            match value.kind {
                ErrorKind::Invalid => 400,
                ErrorKind::Unavailable => 503,
                ErrorKind::Cancelled => 499,
                ErrorKind::Timeout => 504,
            },
            value.message,
        )
    }
}

impl From<export_doc_storage::Error> for ApiError {
    fn from(value: export_doc_storage::Error) -> Self {
        use export_doc_storage::ErrorKind;
        let status = match value.kind {
            ErrorKind::Conflict => 409,
            ErrorKind::Busy => 429,
            ErrorKind::Unavailable => 503,
            ErrorKind::Timeout => 504,
            ErrorKind::Unsupported => 501,
        };
        error(status, value.message)
    }
}
impl From<std::io::Error> for ApiError {
    fn from(value: std::io::Error) -> Self {
        unavailable(format!("文件操作失败：{value}"))
    }
}
impl From<serde_json::Error> for ApiError {
    fn from(value: serde_json::Error) -> Self {
        unavailable(format!("数据不符合保存契约：{value}"))
    }
}
