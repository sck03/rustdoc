//! Immutable identity of the compiled application, shared by diagnostics and exports.
pub const PRODUCT_VERSION: &str = env!("CARGO_PKG_VERSION");
pub const INFORMATIONAL_VERSION: &str = env!("EXPORTDOCMANAGER_INFORMATIONAL_VERSION");
