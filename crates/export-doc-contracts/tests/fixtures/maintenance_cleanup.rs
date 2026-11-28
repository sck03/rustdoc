//! Explicitly reviewed removal of inert configuration and unimplemented diagnostic options.
use serde_json::{Value, json};

/// Reviewed current-contract cleanup; this is a test projection, not runtime compatibility.
pub(super) fn recovery_receipts(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    assert!(
        schemas["ApiBackupPlan"]["properties"]
            .as_object_mut()
            .unwrap()
            .remove("verifyReplica")
            .is_some()
    );
    schemas["ApiBackupPlan"]["required"]
        .as_array_mut()
        .unwrap()
        .retain(|field| field != "verifyReplica");
    schemas["ApiCapacityPolicyReceipt"] = json!({
        "type":"object", "properties":{"revision":{"type":"integer"},"policy":{"$ref":"#/components/schemas/ApiCapacityPolicy"}},
        "required":["revision","policy"], "additionalProperties":false
    });
    schemas["MaintenanceStatus"]["properties"]["artifactFailure"] =
        json!({"$ref":"#/components/schemas/ApiErrorResponse"});
    let response = &mut baseline["paths"]["/api/file-storage/capacity/policy"]["put"]["responses"]
        ["200"]["content"]["application/json"]["schema"]["$ref"];
    assert_eq!(response, "#/components/schemas/ApiStorageCapacityStatus");
    *response = json!("#/components/schemas/ApiCapacityPolicyReceipt");
}

pub(super) fn apply(baseline: &mut Value) {
    let fields = [
        "backupRetentionDays",
        "postgreSqlAutoBackupEnabled",
        "postgreSqlAutoBackupSchedule",
        "postgreSqlAutoBackupTime",
        "postgreSqlAutoBackupDayOfWeek",
        "postgreSqlAutoBackupRetentionCount",
        "auditLogRetentionDays",
        "logFileSizeLimitMB",
        "postgreSqlHost",
        "postgreSqlPort",
        "postgreSqlDatabase",
        "postgreSqlUsername",
        "postgreSqlPassword",
        "postgreSqlAdditionalOptions",
    ];
    for key in fields {
        let schema = &mut baseline["components"]["schemas"]["SystemSettings"];
        schema["properties"].as_object_mut().unwrap().remove(key);
        schema["required"]
            .as_array_mut()
            .unwrap()
            .retain(|value| value != key);
        baseline["x-exportdoc-configuration"]["defaults"]["system"]
            .as_object_mut()
            .unwrap()
            .remove(key);
        baseline["x-exportdoc-configuration"]["labels"]
            .as_object_mut()
            .unwrap()
            .remove(&format!("system.{key}"));
    }
    baseline["components"]["schemas"]["SystemSettings"]["additionalProperties"] = json!(false);
    let properties = &mut baseline["components"]["schemas"]["SystemSettings"]["properties"];
    properties["databaseProvider"]["readOnly"] = json!(true);
    properties["databaseProvider"]["description"] = json!("启动时确定的数据库类型");
    properties["sqliteDatabaseFileName"]["readOnly"] = json!(true);
    properties["sqliteDatabaseFileName"]["description"] = json!("运行时固定的 SQLite 文件名");
    properties["defaultExportDirectory"]["description"] =
        json!("相对于运行数据根的默认导出目录，留空使用系统保存对话框默认位置");
    properties["logRetentionDays"]["description"] =
        json!("手动清理时的文本日志保留天数（默认30，0表示不按天数清理）");
    properties["logRetainedFileCount"]["description"] =
        json!("手动清理时保留最近的文本日志文件数（默认14）");
    baseline["x-exportdoc-configuration"]["defaults"]["system"]["sqliteDatabaseFileName"] =
        json!("exportdoc-native.db");
    for (schema, field) in [
        ("ApiSettingsSecretsDto", "postgreSqlPasswordSet"),
        ("ApiSystemLogCleanupResponse", "deletedAuditLogs"),
    ] {
        let schema = &mut baseline["components"]["schemas"][schema];
        schema["properties"].as_object_mut().unwrap().remove(field);
        schema["required"]
            .as_array_mut()
            .unwrap()
            .retain(|value| value != field);
    }
    baseline["components"]["schemas"]
        .as_object_mut()
        .unwrap()
        .remove("ApiSupportPackageRequest");
    let access = baseline["paths"]["/api/settings"]["put"]["x-exportdoc-policy"].clone();
    for path in [
        "/api/system/logs/cleanup",
        "/api/support-package/save-to-runtime",
        "/api/support-package/download",
    ] {
        let endpoint = &mut baseline["paths"][path]["post"];
        endpoint["x-exportdoc-policy"] = access.clone();
        endpoint.as_object_mut().unwrap().remove("requestBody");
    }
    let responses = &mut baseline["paths"]["/api/system/logs/cleanup"]["post"]["responses"];
    responses.as_object_mut().unwrap().remove("409");
    for (status, description) in [("503", "Service Unavailable"), ("504", "Gateway Timeout")] {
        responses[status] = json!({"description":description,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiErrorResponse"}}}});
    }
}
