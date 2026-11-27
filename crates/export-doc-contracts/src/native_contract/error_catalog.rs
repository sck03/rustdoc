//! Public failure vocabulary shared by schemas, adapters and clients.
use super::model::*;

macro_rules! codes {
    ($($variant:ident => $wire:literal),+ $(,)?) => {
        #[derive(Clone, Copy, Debug, PartialEq, Eq)]
        pub enum ErrorCode { $($variant),+ }
        impl ErrorCode {
            pub const ALL: &'static [&'static str] = &[$($wire),+];
            pub const fn as_str(self) -> &'static str {
                match self { $(Self::$variant => $wire),+ }
            }
        }
    }
}
codes! {
    Validation => "VALIDATION_FAILED",
    Unauthenticated => "AUTHENTICATION_REQUIRED",
    Forbidden => "PERMISSION_DENIED",
    NotFound => "RESOURCE_NOT_FOUND",
    Conflict => "STATE_CONFLICT",
    ConcurrencyConflict => "ConcurrencyConflict",
    Busy => "RESOURCE_BUSY",
    Cancelled => "OPERATION_CANCELLED",
    Unsupported => "CAPABILITY_UNAVAILABLE",
    DependencyUnavailable => "DEPENDENCY_UNAVAILABLE",
    Timeout => "OPERATION_TIMEOUT",
    Unknown => "UNCLASSIFIED_FAILURE",
    DatabaseUnavailable => "DATABASE_UNAVAILABLE",
    DatabaseAuthentication => "DATABASE_AUTHENTICATION_FAILED",
    DatabaseConnection => "DATABASE_CONNECTION_FAILED",
    DatabaseModel => "DATABASE_MODEL_MISMATCH",
    DatabaseOutcomeUnknown => "DATABASE_OUTCOME_UNKNOWN",
    FileMissing => "FILE_MISSING",
    FilePermission => "FILE_PERMISSION_DENIED",
    FileSpace => "FILE_SPACE_EXHAUSTED",
    FileUnavailable => "FILE_UNAVAILABLE",
    DataInvalid => "STORED_DATA_INVALID",
    HandlingRequired => "OA_HANDLING_SERVICE_REQUIRED",
    HandlingUnconfigured => "OA_HANDLING_SERVICE_UNCONFIGURED",
    HandlingUnavailable => "OA_HANDLING_SERVICE_UNAVAILABLE",
    HandlerUnavailable => "OA_HANDLER_UNAVAILABLE",
    HandlerPermission => "OA_HANDLER_PERMISSION_MISSING",
    PublicationStale => "OA_PUBLICATION_STALE",
    StoreIdentity => "STORAGE_IDENTITY_MISMATCH",
    ObjectIntegrity => "OBJECT_INTEGRITY_FAILED",
    BackupIncomplete => "BACKUP_INCOMPLETE",
    RecoveryBlocked => "RECOVERY_BLOCKED"
}

impl ErrorCode {
    pub fn for_status(status: u16) -> Self {
        match status {
            400 | 413 | 415 | 422 => Self::Validation,
            401 => Self::Unauthenticated,
            403 => Self::Forbidden,
            404 => Self::NotFound,
            409 => Self::Conflict,
            429 => Self::Busy,
            499 => Self::Cancelled,
            501 => Self::Unsupported,
            503 => Self::DependencyUnavailable,
            504 => Self::Timeout,
            _ => Self::Unknown,
        }
    }
}

fn choices(values: &[&str]) -> Schema {
    s().enumeration(Enumeration::Values(
        values.iter().map(|s| serde_json::json!(s)).collect(),
    ))
}

pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    vec![
        ("ApiErrorCode", choices(ErrorCode::ALL)),
        (
            "ApiErrorIssue",
            o(vec![
                ("code", r("ApiErrorCode")),
                ("field", s().max_length(200)),
                ("message", s().max_length(2000)),
                ("suggestion", s().max_length(1000)),
            ])
            .required(vec!["code", "field", "message", "suggestion"]),
        ),
        (
            "ApiErrorContext",
            o(vec![
                ("stage", s().max_length(200)),
                ("node", s().max_length(200)),
                ("object", s().max_length(200)),
            ]),
        ),
        (
            "ApiErrorResponse",
            o(vec![
                ("code", r("ApiErrorCode")),
                ("message", s()),
                ("issues", a(r("ApiErrorIssue")).max_items(32)),
                ("context", r("ApiErrorContext")),
                (
                    "actions",
                    a(choices(&[
                        "Edit",
                        "SignIn",
                        "Refresh",
                        "QueryReceipt",
                        "RetryRead",
                        "ContactAdministrator",
                    ]))
                    .max_items(6),
                ),
                (
                    "retryability",
                    choices(&["Never", "AfterCorrection", "ReadOnly", "CheckReceipt"]),
                ),
                (
                    "outcome",
                    choices(&["NotCommitted", "Committed", "Unknown"]),
                ),
                ("requestId", s()),
                ("jobId", s()),
            ])
            .required(vec![
                "code",
                "message",
                "issues",
                "actions",
                "retryability",
                "outcome",
                "requestId",
            ]),
        ),
    ]
}
