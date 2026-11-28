//! The maintenance host has separate credentials and no business database session.
use super::model::*;
pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    vec![
        (
            "MaintenanceLogin",
            o(vec![("token", s().min_length(32).max_length(256))])
                .required(vec!["token"])
                .additional(Additional::Allowed(false)),
        ),
        (
            "MaintenanceSession",
            o(vec![("csrfToken", s()), ("expiresInSeconds", i())])
                .required(vec!["csrfToken", "expiresInSeconds"])
                .additional(Additional::Allowed(false)),
        ),
        (
            "MaintenanceUnlock",
            o(vec![
                ("repositoryId", s().pattern("^([a-fA-F0-9]{32})?$")),
                ("locationId", s().max_length(64)),
                ("password", s().min_length(8).max_length(1024)),
                ("portableFile", s().max_length(128)),
                (
                    "sourceKind",
                    s().default_value(serde_json::json!("Directory"))
                        .enumeration(Enumeration::Values(vec![
                            serde_json::json!("Directory"),
                            serde_json::json!("S3"),
                            serde_json::json!("WebDav"),
                        ])),
                ),
                ("s3", r("ApiBackupS3Settings")),
                ("secretAccessKey", s().max_length(4096)),
                ("sessionToken", s().max_length(16384)),
                ("webDav", r("ApiBackupWebDavSettings")),
                ("webDavPassword", s().max_length(4096)),
                ("oldInstanceIsolated", b()),
            ])
            .required(vec![
                "repositoryId",
                "locationId",
                "password",
                "oldInstanceIsolated",
            ])
            .additional(Additional::Allowed(false)),
        ),
        (
            "MaintenanceRestore",
            o(vec![
                ("pointId", s().pattern("^[a-fA-F0-9]{32}$")),
                (
                    "fileMode",
                    s().enumeration(Enumeration::Values(vec![
                        serde_json::json!("Local"),
                        serde_json::json!("Directory"),
                        serde_json::json!("Reconnect"),
                        serde_json::json!("S3"),
                    ])),
                ),
                ("targetLocation", s().max_length(64)),
                ("s3", r("ApiBackupS3Settings")),
                ("secretAccessKey", s().max_length(4096)),
                ("sessionToken", s().max_length(16384)),
            ])
            .required(vec!["pointId", "fileMode", "targetLocation"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "MaintenanceApply",
            o(vec![("planId", s().min_length(32).max_length(64))])
                .required(vec!["planId"])
                .additional(Additional::Allowed(false)),
        ),
        (
            "MaintenanceDiscard",
            o(vec![
                ("id", s().pattern("^[a-f0-9]{32}$")),
                ("secretAccessKey", s().max_length(4096)),
                ("sessionToken", s().max_length(4096)),
            ])
            .required(vec!["id"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "MaintenancePointQuery",
            o(vec![("after", s().max_length(128))]).additional(Additional::Allowed(false)),
        ),
        (
            "MaintenanceStatus",
            o(vec![
                (
                    "artifacts",
                    a(o(vec![("id", s()), ("location", s()), ("state", s())])
                        .required(vec!["id", "location", "state"])
                        .additional(Additional::Allowed(false))),
                ),
                ("nextCursor", s()),
                ("state", s()),
                ("busy", b()),
                ("message", s()),
                ("planId", s()),
                ("failure", r("ApiErrorResponse")),
                ("artifactFailure", r("ApiErrorResponse")),
                (
                    "locations",
                    a(o(vec![("id", s()), ("name", s())])
                        .required(vec!["id", "name"])
                        .additional(Additional::Allowed(false))),
                ),
                (
                    "points",
                    a(o(vec![
                        ("id", s()),
                        ("createdAt", s()),
                        ("provider", s()),
                        ("sizeBytes", i().format("int64")),
                    ])
                    .required(vec!["id", "createdAt", "provider", "sizeBytes"])
                    .additional(Additional::Allowed(false))),
                ),
            ])
            .required(vec![
                "state",
                "busy",
                "message",
                "planId",
                "locations",
                "points",
            ])
            .additional(Additional::Allowed(false)),
        ),
    ]
}
pub(super) fn endpoints() -> Vec<Endpoint> {
    [
        (
            "post",
            "/maintenance-api/session",
            "OpenMaintenanceSession",
            Some("MaintenanceLogin"),
            "MaintenanceSession",
            false,
        ),
        (
            "get",
            "/maintenance-api/session",
            "ResumeMaintenanceSession",
            None,
            "MaintenanceSession",
            true,
        ),
        (
            "get",
            "/maintenance-api/status",
            "GetMaintenanceStatus",
            None,
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/unlock",
            "UnlockRecoveryRepository",
            Some("MaintenanceUnlock"),
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/preflight",
            "PreflightRecoveryPoint",
            Some("MaintenanceRestore"),
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/apply",
            "ApplyRecoveryPoint",
            Some("MaintenanceApply"),
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/points",
            "ListMaintenancePoints",
            Some("MaintenancePointQuery"),
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/artifacts/discard",
            "DiscardRecoveryArtifacts",
            Some("MaintenanceDiscard"),
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/abandon",
            "AbandonMaintenance",
            None,
            "MaintenanceStatus",
            true,
        ),
        (
            "post",
            "/maintenance-api/cancel",
            "CancelMaintenanceOperation",
            None,
            "MaintenanceStatus",
            true,
        ),
    ]
    .into_iter()
    .map(|(method, path, id, request, response, auth)| {
        let mut policy =
            super::policies::rule((auth, false, false), Access::Bypass { bypass: true });
        policy.authentication_context = Some("Maintenance");
        route(
            method,
            path,
            id,
            super::responses::json(
                r(response),
                &[],
                &["400", "401", "403", "409", "429", "503"],
            ),
            policy,
            None,
            request.map(r),
        )
    })
    .collect()
}
