//! Optional quotation retrieval. Hosts supply configuration, time and cancellation.
mod banks;
mod cache;
mod configuration;
mod endpoint;
mod parser;
mod transport;
pub use cache::{ExchangeRates, Quotation};
pub use configuration::{Configuration, Format, Source};
pub use endpoint::{DEFAULT_URL, normalize, public_address};
pub use parser::parse;
pub use transport::{Fetch, Https};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ErrorKind {
    Invalid,
    Unavailable,
    Timeout,
    Cancelled,
}
#[derive(Clone, Debug)]
pub struct Error {
    pub kind: ErrorKind,
    pub message: String,
    pub retry_after: Option<std::time::Duration>,
}
impl Error {
    pub fn new(kind: ErrorKind, message: impl Into<String>) -> Self {
        Self {
            kind,
            message: message.into(),
            retry_after: None,
        }
    }
}
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}
impl std::error::Error for Error {}
pub type Result<T> = std::result::Result<T, Error>;
pub type Check<'a> = &'a dyn Fn() -> Result<()>;

#[cfg(test)]
mod tests;
