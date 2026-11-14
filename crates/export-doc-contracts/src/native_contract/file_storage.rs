use super::model::*;

pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    let bytes = || i().format("int64").minimum(num("0"));
    vec![
        (
            "ApiFileStoragePolicy",
            o(vec![
                ("fileBytes", bytes()),
                ("requestBytes", bytes()),
                ("requestFiles", i()),
                ("quotaBytes", bytes()),
            ])
            .required(vec![
                "fileBytes",
                "requestBytes",
                "requestFiles",
                "quotaBytes",
            ])
            .additional(Additional::Allowed(false)),
        ),
        (
            "ApiFileStorageStatus",
            o(vec![
                ("versionNumber", bytes()),
                ("credentialVersion", bytes()),
                ("kind", s()),
                ("directory", s()),
                ("registeredRoots", a(s())),
                ("s3", r("ApiBackupS3Settings")),
                ("policy", r("ApiFileStoragePolicy")),
                ("fileCount", bytes()),
                ("physicalBytes", bytes()),
                ("logicalBytes", bytes()),
                ("unreferencedBytes", bytes()),
                ("migrationPending", b()),
                ("healthy", b()),
                ("healthMessage", s()),
            ]),
        ),
        (
            "ApiFileStoragePolicyRequest",
            o(vec![
                ("expectedVersion", bytes()),
                ("policy", r("ApiFileStoragePolicy")),
            ])
            .required(vec!["expectedVersion", "policy"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "ApiFileStorageCredentialsRequest",
            o(vec![
                ("expectedVersion", bytes()),
                ("accessKeyId", s().max_length(256)),
                ("secret", s().max_length(4096)),
                ("token", s().max_length(4096)),
            ])
            .required(vec!["expectedVersion", "accessKeyId", "secret", "token"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "ApiFileStorageMigrationRequest",
            o(vec![
                ("expectedVersion", bytes()),
                ("kind", s()),
                ("directory", s()),
                ("s3", r("ApiBackupS3Settings")),
                ("secret", s().max_length(4096)),
                ("token", s().max_length(4096)),
                ("resume", b()),
                ("cancel", b()),
            ])
            .additional(Additional::Allowed(false)),
        ),
    ]
}
pub(super) fn endpoints() -> Vec<Endpoint> {
    [
        (
            "put",
            "/api/file-storage/credentials",
            "UpdateFileStorageCredentials",
            "ApiFileStorageStatus",
            Some("ApiFileStorageCredentialsRequest"),
        ),
        (
            "get",
            "/api/file-storage",
            "GetFileStorage",
            "ApiFileStorageStatus",
            None,
        ),
        (
            "put",
            "/api/file-storage/policy",
            "UpdateFileStoragePolicy",
            "ApiFileStorageStatus",
            Some("ApiFileStoragePolicyRequest"),
        ),
        (
            "get",
            "/api/file-storage/policy",
            "GetFileStoragePolicy",
            "ApiFileStoragePolicy",
            None,
        ),
        (
            "post",
            "/api/file-storage/reconcile",
            "ReconcileFileStorage",
            "BackgroundJobSnapshot",
            None,
        ),
        (
            "post",
            "/api/file-storage/migrations",
            "StartFileStorageMigration",
            "BackgroundJobSnapshot",
            Some("ApiFileStorageMigrationRequest"),
        ),
        (
            "post",
            "/api/file-storage/verify",
            "VerifyFileStorage",
            "BackgroundJobSnapshot",
            None,
        ),
    ]
    .into_iter()
    .map(|(method, path, id, response, body)| {
        route(
            method,
            path,
            id,
            super::responses::json(
                r(response),
                &["400", "401", "403", "409", "429", "503"],
                &[],
            ),
            super::policies::rule(
                (true, true, false),
                None,
                if id == "GetFileStoragePolicy" {
                    &[]
                } else {
                    &[("system.settings", "manage")]
                },
                if id == "GetFileStoragePolicy" {
                    Access::Bypass { bypass: true }
                } else {
                    super::policies::access("All", &[("system.settings", &["manage"], true)])
                },
                id == "GetFileStoragePolicy",
            ),
            None,
            body.map(r),
        )
    })
    .collect()
}
