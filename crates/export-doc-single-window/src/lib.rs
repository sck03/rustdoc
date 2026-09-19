pub mod authentication;
pub mod package;
pub mod receipt;
pub mod xml;
pub use package::Package;
use sha2::{Digest, Sha256};
pub type Result<T> = std::result::Result<T, String>;
pub type Check<'a> = &'a dyn Fn() -> Result<()>;
pub fn digest(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|byte| format!("{byte:02X}"))
        .collect()
}
pub fn hex(value: &str, length: usize) -> bool {
    value.len() == length && value.bytes().all(|c| c.is_ascii_hexdigit())
}
pub fn prefixed(value: &str, prefix: &str) -> bool {
    value.strip_prefix(prefix).is_some_and(|s| hex(s, 32))
}
pub fn protocol_token(value: &str, max: usize) -> bool {
    !value.is_empty()
        && value.len() <= max
        && value
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, b'-' | b'_' | b'.'))
}
