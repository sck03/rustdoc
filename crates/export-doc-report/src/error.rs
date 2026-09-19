#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ErrorKind {
    Invalid,
    Unavailable,
    Cancelled,
    Timeout,
}

#[derive(Debug)]
pub struct Error {
    pub kind: ErrorKind,
    pub message: String,
}
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}
impl std::error::Error for Error {}
pub type Result<T> = std::result::Result<T, Error>;

pub(crate) fn invalid(message: impl Into<String>) -> Error {
    Error {
        kind: ErrorKind::Invalid,
        message: message.into(),
    }
}
pub(crate) fn unavailable(message: impl Into<String>) -> Error {
    Error {
        kind: ErrorKind::Unavailable,
        message: message.into(),
    }
}
impl From<serde_json::Error> for Error {
    fn from(error: serde_json::Error) -> Self {
        invalid(format!("报表数据无效：{error}"))
    }
}
