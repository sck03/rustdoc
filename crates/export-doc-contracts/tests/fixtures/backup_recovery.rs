//! Explicitly reviewed task responses and paged backup history; unrelated policy stays frozen.
use serde_json::{Value, json};
pub(super) fn apply(baseline: &mut Value) {
    for path in [
        "/api/postgresql-maintenance/backups/restore",
        "/api/postgresql-maintenance/backups/upload-restore",
        "/api/server-migration/restore",
    ] {
        let responses = baseline["paths"][path]["post"]["responses"]
            .as_object_mut()
            .unwrap();
        assert!(responses.remove("200").is_some());
        responses.insert("202".into(), json!({"description":"Accepted","content":{"application/json":{"schema":{"$ref":"#/components/schemas/BackgroundJobSnapshot"}}}}));
        responses.insert("429".into(), json!({"description":"Too Many Requests"}));
    }
    assert!(
        baseline["components"]["schemas"]
            .as_object_mut()
            .unwrap()
            .remove("ApiServerMigrationRestoreResponse")
            .is_some()
    );
    let plan = &mut baseline["components"]["schemas"]["ApiBackupPlanStatus"];
    assert!(
        plan["properties"]
            .as_object_mut()
            .unwrap()
            .remove("archives")
            .is_some()
    );
    plan["required"]
        .as_array_mut()
        .unwrap()
        .retain(|field| field != "archives");
    let archive = &mut baseline["components"]["schemas"]["ApiRecoveryArchive"];
    archive["properties"]["available"] = json!({"type":"boolean"});
    archive["properties"]["integrityMessage"] = json!({"type":"string"});
    archive["required"]
        .as_array_mut()
        .unwrap()
        .splice(0..0, [json!("available"), json!("integrityMessage")]);
    baseline["components"]["schemas"]["ApiRecoveryArchiveList"] = json!({"type":"object","properties":{
        "items":{"type":"array","items":{"$ref":"#/components/schemas/ApiRecoveryArchive"}},
        "page":{"type":"integer"},"pageSize":{"type":"integer"},"totalCount":{"type":"integer"}},
        "required":["items","page","pageSize","totalCount"],"additionalProperties":false});
    let mut list = baseline["paths"]["/api/disaster-recovery/plan"]["get"].clone();
    list["operationId"] = json!("ListRecoveryArchives");
    list["parameters"] = json!([
        {"name":"page","in":"query","schema":{"type":"integer","minimum":1,"maximum":1000000}},
        {"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}]);
    list["responses"] = json!({"200":{"description":"OK","content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiRecoveryArchiveList"}}}},
        "400":{"description":"Bad Request"},"401":{"description":"Unauthorized"},"403":{"description":"Forbidden"},"503":{"description":"Service Unavailable"}});
    baseline["paths"]["/api/disaster-recovery/archives"] = json!({"get":list});
}
