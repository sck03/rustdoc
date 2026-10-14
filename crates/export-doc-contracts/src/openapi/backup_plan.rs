//! Explicit opt-in scheduled recovery packages; legacy inactive backup settings stay inert.
use serde_json::{Value, json};
fn object(properties: Value) -> Value {
    let required: Vec<_> = properties.as_object().unwrap().keys().cloned().collect();
    json!({"type":"object","properties":properties,"required":required,"additionalProperties":false})
}
fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
pub(super) fn extend(document: &mut Value) {
    let schemas = &mut document["components"]["schemas"];
    schemas["ApiBackupPlan"] = object(json!({
        "enabled":{"type":"boolean","default":false},
        "frequency":{"type":"string","enum":["Daily","Weekly"],"default":"Daily"},
        "time":{"type":"string","default":"02:00","pattern":"^([01][0-9]|2[0-3]):[0-5][0-9]$"},
        "weekday":{"type":"integer","minimum":0,"maximum":6,"default":1},
        "retentionCount":{"type":"integer","minimum":1,"maximum":365,"default":14},
        "retentionDays":{"type":"integer","minimum":0,"maximum":3650,"default":0},
        "replicaMode":{"type":"string","enum":["None","WebDav","S3"],"default":"None"},
        "verifyReplica":{"type":"boolean","default":true},"s3":reference("ApiBackupS3Settings")
    }));
    schemas["ApiBackupS3Settings"] = object(
        json!({"endpoint":{"type":"string","maxLength":2048},"region":{"type":"string","maxLength":64,"default":"us-east-1"},"bucket":{"type":"string","maxLength":63},"prefix":{"type":"string","maxLength":512,"default":"ExportDocManager"},"accessKeyId":{"type":"string","maxLength":256},"pathStyle":{"type":"boolean","default":true}}),
    );
    schemas["ApiBackupWebDavSettings"] = object(
        json!({"enabled":{"type":"boolean"},"url":{"type":"string","maxLength":2048},"userName":{"type":"string","maxLength":256}}),
    );
    schemas["ApiBackupPlanSaveRequest"] = object(
        json!({"expectedRevision":{"type":"integer","format":"int64","minimum":0},"expectedSettingsRevision":{"type":"integer","format":"int64","minimum":0},"plan":reference("ApiBackupPlan"),"webDav":reference("ApiBackupWebDavSettings"),"webDavPassword":{"type":["string","null"],"maxLength":4096},"s3SecretAccessKey":{"type":["string","null"],"maxLength":4096},"s3SessionToken":{"type":["string","null"],"maxLength":16384},"encryptionPassword":{"type":"string","maxLength":1024,"description":"空字符串保留已保存密码；密码仅加密存储，不回传。"}}),
    );
    schemas["ApiRecoveryArchive"] = object(
        json!({"id":{"type":"string"},"fileName":{"type":"string"},"createdAt":{"type":"string","format":"date-time"},"sizeBytes":{"type":"integer","format":"int64"},"replicaStatus":{"type":"string"},"message":{"type":"string"}}),
    );
    schemas["ApiRecoveryArchiveRestoreRequest"] = object(
        json!({"password":{"type":"string","maxLength":1024},"adminPassword":{"type":"string","maxLength":1024},"confirmationText":{"type":"string","enum":["MIGRATE","RECOVER"]}}),
    );
    schemas["ApiBackupPlanStatus"] = object(
        json!({"revision":{"type":"integer","format":"int64"},"plan":reference("ApiBackupPlan"),"encryptionPasswordSet":{"type":"boolean"},"timeZone":{"type":"string"},"nextRunAt":{"type":["string","null"],"format":"date-time"},"runningJobId":{"type":["string","null"]},"lastRunAt":{"type":["string","null"],"format":"date-time"},"lastResult":{"type":"string"},"lastError":{"type":"string"},"archives":{"type":"array","items":reference("ApiRecoveryArchive")}}),
    );
    for (name, definition) in [
        (
            "settingsRevision",
            json!({"type":"integer","format":"int64"}),
        ),
        ("webDav", reference("ApiBackupWebDavSettings")),
        ("webDavPasswordSet", json!({"type":"boolean"})),
        ("s3SecretAccessKeySet", json!({"type":"boolean"})),
        ("s3SessionTokenSet", json!({"type":"boolean"})),
        ("s3Available", json!({"type":"boolean"})),
    ] {
        schemas["ApiBackupPlanStatus"]["properties"][name] = definition;
        schemas["ApiBackupPlanStatus"]["required"]
            .as_array_mut()
            .unwrap()
            .push(json!(name));
    }
    for (path, method, id, request, response) in [
        (
            "/api/disaster-recovery/plan",
            "get",
            "GetBackupPlan",
            None,
            "ApiBackupPlanStatus",
        ),
        (
            "/api/disaster-recovery/plan",
            "put",
            "SaveBackupPlan",
            Some("ApiBackupPlanSaveRequest"),
            "ApiBackupPlanStatus",
        ),
        (
            "/api/disaster-recovery/plan/run",
            "post",
            "RunBackupPlan",
            None,
            "BackgroundJobSnapshot",
        ),
        (
            "/api/disaster-recovery/plan/test-replica",
            "post",
            "TestBackupReplica",
            None,
            "BackgroundJobSnapshot",
        ),
        (
            "/api/disaster-recovery/archives/{id}/download-ticket",
            "post",
            "CreateRecoveryArchiveDownloadTicket",
            None,
            "ApiDownloadTicket",
        ),
        (
            "/api/disaster-recovery/archives/{id}/restore",
            "post",
            "StageRecoveryArchiveRestore",
            Some("ApiRecoveryArchiveRestoreRequest"),
            "BackgroundJobSnapshot",
        ),
    ] {
        let mut endpoint = json!({"tags":["ExportDocManager.Api"],"operationId":id,
            "responses":{"200":{"description":"Success","content":{"application/json":{"schema":reference(response)}}},"400":{"description":"Invalid configuration"},"401":{"description":"Authentication required"},"403":{"description":"Administrator required"},"404":{"description":"Archive missing"},"409":{"description":"Configuration changed"},"429":{"description":"Backup already running"},"503":{"description":"Storage or replica unavailable"}},
            "security":[{"BearerAuth":[],"DesktopAccess":[]}],
            "x-exportdoc-policy":{"requiresAuthentication":true,"requiresDesktopAccess":true,"requiresLicense":false,"permissions":null,"requirements":[{"resourceKey":"system.backup","action":"manage"},{"resourceKey":"system.disaster-recovery","action":"manage"}],"permissionBypass":false}});
        if let Some(request) = request {
            endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference(request)}}});
        }
        if path.contains("{id}") {
            endpoint["parameters"] =
                json!([{"name":"id","in":"path","required":true,"schema":{"type":"string"}}]);
        }
        document["paths"][path][method] = endpoint;
    }
    let mut download = document["paths"]
        .as_object()
        .unwrap()
        .values()
        .find(|methods| methods["get"]["operationId"] == "DownloadJobResultWithTicket")
        .unwrap()
        .clone();
    download["get"]["operationId"] = json!("DownloadRecoveryArchiveWithTicket");
    document["paths"]["/api/disaster-recovery/archives/downloads/{token}"] = download;
}
