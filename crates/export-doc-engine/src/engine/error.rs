use crate::api::ApiError;

pub type Result<T> = std::result::Result<T, ApiError>;
pub fn error(status: u16, message: impl Into<String>) -> ApiError {
    ApiError {
        status: Some(status),
        message: message.into(),
        code: (status < 500 || matches!(status, 501 | 504))
            .then(|| crate::api::ErrorCode::for_status(status)),
        diagnostic: (status >= 500).then_some("dependency"),
        ..ApiError::default()
    }
}
pub fn invalid(message: impl Into<String>) -> ApiError {
    error(400, message)
}
pub fn conflict(message: impl Into<String>) -> ApiError {
    error(409, message)
}
pub fn concurrency(message: impl Into<String>) -> ApiError {
    let mut cause = conflict(message);
    cause.code = Some(crate::api::ErrorCode::ConcurrencyConflict);
    cause
}
/// Static application diagnostics have been authored for users. Dynamically
/// constructed dependency failures stay internal until an adapter classifies them.
pub enum DependencyDetail {
    Public(&'static str),
    Internal(String),
}
impl From<&'static str> for DependencyDetail {
    fn from(value: &'static str) -> Self {
        Self::Public(value)
    }
}
impl From<String> for DependencyDetail {
    fn from(value: String) -> Self {
        Self::Internal(value)
    }
}
pub fn unavailable(message: impl Into<DependencyDetail>) -> ApiError {
    match message.into() {
        DependencyDetail::Public(message) => {
            error(503, message).coded(crate::api::ErrorCode::DependencyUnavailable)
        }
        DependencyDetail::Internal(message) => error(503, message),
    }
}
pub fn unsupported(message: impl Into<String>) -> ApiError {
    error(501, message)
}

impl From<export_doc_report::Error> for ApiError {
    fn from(value: export_doc_report::Error) -> Self {
        use export_doc_report::ErrorKind;
        let mut cause = error(
            match value.kind {
                ErrorKind::Invalid => 400,
                ErrorKind::Unavailable => 503,
                ErrorKind::Cancelled => 499,
                ErrorKind::Timeout => 504,
            },
            value.message,
        );
        cause.diagnostic = Some("report");
        cause
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
        let mut cause = error(status, value.message);
        cause.code = Some(if status == 503 {
            crate::api::ErrorCode::DatabaseUnavailable
        } else {
            crate::api::ErrorCode::for_status(status)
        });
        cause.outcome_unknown = matches!(value.kind, ErrorKind::Unavailable | ErrorKind::Timeout);
        cause.diagnostic = Some("storage");
        cause
    }
}
impl From<std::io::Error> for ApiError {
    fn from(value: std::io::Error) -> Self {
        use crate::api::ErrorCode;
        let (code, message) = match value.kind() {
            std::io::ErrorKind::NotFound => (
                ErrorCode::FileMissing,
                "文件或存储位置不存在，请核对文件及挂载状态。",
            ),
            std::io::ErrorKind::PermissionDenied => (
                ErrorCode::FilePermission,
                "当前服务没有文件读写权限，请检查目录授权或介质只读状态。",
            ),
            std::io::ErrorKind::StorageFull => (
                ErrorCode::FileSpace,
                "存储空间已用尽，请扩容或清理已确认无用的临时文件后重新执行。",
            ),
            _ => (
                ErrorCode::FileUnavailable,
                "文件读写未完成，请核对存储连接、介质状态和服务目录权限。",
            ),
        };
        let mut cause = unavailable(message).coded(code);
        cause.diagnostic = Some("filesystem");
        cause
    }
}
impl From<serde_json::Error> for ApiError {
    fn from(_value: serde_json::Error) -> Self {
        let mut cause =
            unavailable("已保存的数据不符合当前契约，请联系管理员核验数据版本和完整性。")
                .coded(crate::api::ErrorCode::DataInvalid);
        cause.diagnostic = Some("serialization");
        cause
    }
}
