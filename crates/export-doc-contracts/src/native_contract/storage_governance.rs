use super::model::*;
fn object(fields: Vec<(&'static str, Schema)>) -> Schema {
    let required = fields.iter().map(|f| f.0).collect();
    o(fields)
        .required(required)
        .additional(Additional::Allowed(false))
}
fn bytes() -> Schema {
    i().format("int64").minimum(num("0"))
}
pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    vec![
        (
            "ApiArchiveFiles",
            object(vec![
                (
                    "ids",
                    a(s().max_length(1024))
                        .min_items(1)
                        .max_items(100)
                        .unique_items(true),
                ),
                ("location", s().min_length(1).max_length(64)),
                ("expectedVersion", i()),
                ("reason", s().min_length(1).max_length(500)),
            ]),
        ),
        (
            "ApiRetentionQuery",
            o(vec![("after", s().max_length(1024))]).additional(Additional::Allowed(false)),
        ),
        (
            "ApiRetentionRow",
            object(vec![
                ("id", s()),
                ("recordId", i()),
                ("fileKind", s()),
                ("archiveClass", s()),
                ("archiveIdentity", s()),
                ("fileName", s()),
                ("company", s()),
                ("digest", s()),
                ("basisDate", s()),
                ("revision", i()),
                ("size", bytes()),
                ("days", i()),
                ("policyRevision", i()),
                ("held", b()),
                ("holdReason", s()),
                ("holdRevision", i()),
                ("activeReferences", i()),
                ("expiresOn", s()),
                ("eligible", b()),
                ("canArchive", b()),
                ("archiveBlocker", s()),
            ]),
        ),
        (
            "ApiRetentionPlan",
            object(vec![
                ("token", s()),
                ("count", i()),
                ("bytes", bytes()),
                ("expiresAt", s()),
                ("requestedBy", i()),
                ("canApprove", b()),
                ("approvalBlocker", s()),
                ("reason", s().max_length(500)),
                ("items", a(r("ApiRetentionRow")).max_items(100)),
            ]),
        ),
        (
            "ApiRetentionPage",
            o(vec![
                ("items", a(r("ApiRetentionRow"))),
                ("nextCursor", s()),
                ("plan", r("ApiRetentionPlan")),
            ])
            .required(vec!["items", "nextCursor"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "ApiRetentionPolicyRequest",
            object(vec![
                ("archiveClass", s().min_length(1).max_length(64)),
                ("days", i().minimum(num("0")).maximum(num("36500"))),
                ("expectedVersion", i().minimum(num("0"))),
                ("reason", s().min_length(1).max_length(500)),
            ]),
        ),
        (
            "ApiRetentionClassification",
            object(vec![
                ("id", s().max_length(1024)),
                ("archiveClass", s().min_length(1).max_length(64)),
                ("basisDate", s().format("date")),
                ("expectedVersion", i()),
                ("reason", s().min_length(1).max_length(500)),
            ]),
        ),
        (
            "ApiRetentionHold",
            object(vec![
                ("digest", s().pattern("^[a-f0-9]{64}$")),
                ("active", b()),
                ("reason", s().min_length(1).max_length(500)),
                ("expectedVersion", i()),
            ]),
        ),
        (
            "ApiRetentionPreviewRequest",
            object(vec![
                (
                    "ids",
                    a(s().max_length(1024))
                        .min_items(1)
                        .max_items(100)
                        .unique_items(true),
                ),
                ("reason", s().min_length(1).max_length(500)),
            ]),
        ),
        (
            "ApiStorageApproval",
            object(vec![("token", s().pattern("^[a-f0-9]{32}$"))]),
        ),
        (
            "ApiStorageActionResult",
            object(vec![("message", s()), ("count", i()), ("bytes", bytes())]),
        ),
        (
            "ApiCapacityPolicy",
            object(vec![
                ("warningPercent", i().minimum(num("50")).maximum(num("99"))),
                (
                    "criticalPercent",
                    i().minimum(num("51")).maximum(num("100")),
                ),
                ("minimumFreeBytes", bytes()),
                ("growthDays", i().minimum(num("1")).maximum(num("90"))),
            ]),
        ),
        (
            "ApiCapacityPolicyReceipt",
            object(vec![("revision", i()), ("policy", r("ApiCapacityPolicy"))]),
        ),
        (
            "ApiCapacityPolicyRequest",
            object(vec![
                ("expectedVersion", i()),
                ("policy", r("ApiCapacityPolicy")),
            ]),
        ),
        (
            "ApiCapacityArea",
            o(vec![
                ("id", s()),
                ("name", s()),
                ("physicalBytes", bytes()),
                ("logicalBytes", bytes()),
                ("reclaimableBytes", bytes()),
                ("protectedBytes", bytes()),
                ("availableBytes", bytes()),
                ("totalBytes", bytes()),
                ("state", s()),
                ("message", s()),
            ])
            .required(vec!["id", "name", "state", "message"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "ApiCapacitySample",
            object(vec![
                ("day", s()),
                ("location", s()),
                ("physicalBytes", bytes()),
                ("logicalBytes", bytes()),
                ("reclaimableBytes", bytes()),
            ]),
        ),
        (
            "ApiCapacityAlert",
            object(vec![
                ("area", s()),
                ("state", s()),
                ("message", s()),
                ("at", s()),
            ]),
        ),
        (
            "ApiStorageCapacityStatus",
            object(vec![
                ("revision", i()),
                ("policy", r("ApiCapacityPolicy")),
                ("areas", a(r("ApiCapacityArea"))),
                ("samples", a(r("ApiCapacitySample"))),
                ("alerts", a(r("ApiCapacityAlert"))),
                ("updatedAt", s()),
            ]),
        ),
        (
            "ApiRepositoryMaintenanceRequest",
            o(vec![
                (
                    "action",
                    s().enumeration(Enumeration::Values(
                        [
                            "Preview",
                            "Collect",
                            "Hold",
                            "Release",
                            "Verify",
                            "RecoverLock",
                            "RotatePassword",
                        ]
                        .map(|v| serde_json::json!(v))
                        .to_vec(),
                    )),
                ),
                ("token", s().max_length(32)),
                ("pointId", s().max_length(32)),
                ("reason", s().max_length(500)),
                ("oldInstanceIsolated", b()),
                ("newPassword", s().min_length(12).max_length(1024)),
            ])
            .required(vec!["action"])
            .additional(Additional::Allowed(false)),
        ),
        (
            "ApiRepositoryMaintenanceStatus",
            o(vec![
                ("token", s()),
                ("expiresAt", s()),
                ("count", i()),
                ("bytes", bytes()),
                ("protectedObjects", i()),
                ("waitingObjects", i()),
                ("archiveCleanupCount", i()),
                ("message", s()),
                ("verifiedAt", s()),
            ])
            .additional(Additional::Allowed(false)),
        ),
    ]
}
pub(super) fn endpoints() -> Vec<Endpoint> {
    let mut endpoints: Vec<_> = [
        (
            "post",
            "/api/file-storage/retention/archive",
            "ArchiveRetainedFiles",
            Some("ApiArchiveFiles"),
            "BackgroundJobSnapshot",
        ),
        (
            "post",
            "/api/file-storage/collection/preview",
            "PreviewFileCollection",
            None,
            "ApiRepositoryMaintenanceStatus",
        ),
        (
            "post",
            "/api/file-storage/retention/query",
            "QueryFileRetention",
            Some("ApiRetentionQuery"),
            "ApiRetentionPage",
        ),
        (
            "put",
            "/api/file-storage/retention/policy",
            "SaveRetentionPolicy",
            Some("ApiRetentionPolicyRequest"),
            "ApiStorageActionResult",
        ),
        (
            "put",
            "/api/file-storage/retention/classification",
            "ClassifyRetainedFile",
            Some("ApiRetentionClassification"),
            "ApiStorageActionResult",
        ),
        (
            "put",
            "/api/file-storage/retention/hold",
            "HoldRetainedFile",
            Some("ApiRetentionHold"),
            "ApiStorageActionResult",
        ),
        (
            "post",
            "/api/file-storage/retention/preview",
            "PreviewRetentionDeletion",
            Some("ApiRetentionPreviewRequest"),
            "ApiRetentionPlan",
        ),
        (
            "post",
            "/api/file-storage/retention/approve",
            "ApproveRetentionDeletion",
            Some("ApiStorageApproval"),
            "ApiStorageActionResult",
        ),
        (
            "get",
            "/api/file-storage/capacity",
            "GetStorageCapacity",
            None,
            "ApiStorageCapacityStatus",
        ),
        (
            "put",
            "/api/file-storage/capacity/policy",
            "SaveStorageCapacityPolicy",
            Some("ApiCapacityPolicyRequest"),
            "ApiCapacityPolicyReceipt",
        ),
        (
            "post",
            "/api/maintenance/recovery-repository",
            "MaintainRecoveryRepository",
            Some("ApiRepositoryMaintenanceRequest"),
            "BackgroundJobSnapshot",
        ),
        (
            "get",
            "/api/maintenance/recovery-repository",
            "GetRecoveryRepositoryMaintenance",
            None,
            "ApiRepositoryMaintenanceStatus",
        ),
    ]
    .into_iter()
    .map(|(method, path, id, body, response)| {
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
                super::policies::access(
                    "All",
                    &[(
                        if id.contains("Repository") {
                            "system.disaster-recovery"
                        } else {
                            "system.settings"
                        },
                        &["manage"],
                        true,
                    )],
                ),
            ),
            None,
            body.map(r),
        )
    })
    .collect();
    let permission = super::policies::rule(
        (true, true, false),
        super::policies::access("All", &[("system.settings", &["manage"], true)]),
    );
    let parameter = || {
        vec![p(
            "id",
            "path",
            Some(true),
            s().min_length(1).max_length(1024),
        )]
    };
    endpoints.push(route(
        "post",
        "/api/file-storage/retention/{id}/download-ticket",
        "CreateRetainedFileDownloadTicket",
        super::responses::secured_json(r("ApiDownloadTicket")),
        permission.clone(),
        Some(parameter()),
        None,
    ));
    endpoints.push(route(
        "get",
        "/api/file-storage/retention/{id}/download",
        "DownloadRetainedFile",
        super::responses::reply(
            "200",
            "application/octet-stream",
            Some(s().format("byte")),
            &["401", "403", "404", "409", "503"],
            &[],
        ),
        permission,
        Some(parameter()),
        None,
    ));
    endpoints.push(
        ep(
            "get",
            "/downloads/retained-files/{token}",
            "DownloadRetainedFileWithTicket",
            super::responses::reply(
                "200",
                "application/octet-stream",
                Some(s().format("byte")),
                &["401", "403", "404", "503"],
                &[],
            ),
            super::policies::rule(
                (false, true, false),
                super::policies::access("All", &[("system.settings", &["manage"], true)]),
            ),
        )
        .security(vec![std::collections::BTreeMap::from([(
            "DesktopAccess",
            vec![],
        )])])
        .parameters(super::route_parameters::download_token()),
    );
    endpoints
}
