use std::fmt;

pub use export_doc_contracts::native_contract::error_catalog::ErrorCode;

#[derive(Debug, Default)]
pub struct ApiError {
    pub status: Option<u16>,
    pub message: String,
    pub code: Option<ErrorCode>,
    /// Reviewed, value-free category for internal logs; never copy raw causes.
    pub diagnostic: Option<&'static str>,
    pub field: Option<&'static str>,
    pub suggestion: Option<&'static str>,
    /// A failed transport or commit cannot establish that a write was rolled back.
    pub outcome_unknown: bool,
}

impl ApiError {
    pub fn local(message: impl Into<String>) -> Self {
        Self {
            status: None,
            message: message.into(),
            code: None,
            diagnostic: Some("local"),
            ..Self::default()
        }
    }

    pub fn coded(mut self, code: ErrorCode) -> Self {
        self.code = Some(code);
        self
    }

    pub fn at(mut self, field: &'static str, suggestion: &'static str) -> Self {
        self.field = Some(field);
        self.suggestion = Some(suggestion);
        self
    }

    pub fn public_body(&self, request_id: &str) -> serde_json::Value {
        let status = self.status.unwrap_or(503);
        let code = self.code.unwrap_or_else(|| {
            if status >= 500 {
                ErrorCode::Unknown
            } else {
                ErrorCode::for_status(status)
            }
        });
        let unknown =
            self.outcome_unknown || code == ErrorCode::Unknown || matches!(status, 500 | 504);
        let (retryability, action) = if unknown {
            ("CheckReceipt", "QueryReceipt")
        } else {
            match status {
                400 | 413 | 415 | 422 => ("AfterCorrection", "Edit"),
                401 => ("AfterCorrection", "SignIn"),
                409 => ("AfterCorrection", "Refresh"),
                429 | 503 => ("ReadOnly", "RetryRead"),
                _ => ("Never", "ContactAdministrator"),
            }
        };
        let message = if code == ErrorCode::Unknown {
            "处理过程中发生未分类错误，请凭追踪编号查询处理结果。"
        } else {
            &self.message
        };
        serde_json::json!({
            "code": code.as_str(), "message": message,
            "issues": [{"code": code.as_str(), "field": self.field.unwrap_or(""),
                "message": message, "suggestion": self.suggestion.unwrap_or("")}],
            "actions": [action], "retryability": retryability,
            "outcome": if unknown { "Unknown" } else { "NotCommitted" },
            "requestId": request_id,
        })
    }
}

impl fmt::Display for ApiError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        if let Some(code) = self.status {
            write!(formatter, "HTTP {code}：{}", self.message)?;
            if code == 409 {
                write!(formatter, " 草稿已保留，请重新读取并核对版本。")?;
            }
            if code == 401 {
                write!(formatter, " 请重新登录。")?;
            }
            Ok(())
        } else {
            formatter.write_str(&self.message)
        }
    }
}
impl std::error::Error for ApiError {}
