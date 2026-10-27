//! Manual history retention bounds shared by OpenAPI and application use cases.
pub const DEFAULT_RETAIN: i64 = 20;
pub const MIN_RETAIN: i64 = 5;
pub const MAX_RETAIN: i64 = 200;
pub const MAX_CLEANUP: i64 = 5000;
