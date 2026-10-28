use std::fmt;

#[derive(Debug)]
pub struct ApiError {
    pub status: Option<u16>,
    pub message: String,
    /// Reviewed, value-free category for internal logs; never copy raw causes.
    pub diagnostic: Option<&'static str>,
}

impl ApiError {
    pub fn local(message: impl Into<String>) -> Self {
        Self {
            status: None,
            message: message.into(),
            diagnostic: Some("local"),
        }
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
