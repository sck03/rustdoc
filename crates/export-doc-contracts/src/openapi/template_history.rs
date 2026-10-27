use crate::template_history as limits;
use serde_json::{Value, json};

pub(super) const FIELDS: &[(&str, &[&str])] = &[
    ("ApiUserReportTemplateDto", &["canManageHistory"]),
    ("ApiUserReportTemplateListItemDto", &["canManageHistory"]),
];

pub(super) fn extend(doc: &mut Value) {
    let schemas = &mut doc["components"]["schemas"];
    for (name, _) in FIELDS {
        schemas[name]["properties"]["canManageHistory"] = json!({"type":"boolean","readOnly":true});
    }
    let retain = json!({"type":"integer","format":"int32","default":limits::DEFAULT_RETAIN,"minimum":limits::MIN_RETAIN,"maximum":limits::MAX_RETAIN});
    schemas["ApiTemplateHistoryRetention"] = json!({"type":"object","additionalProperties":false,
    "required":["totalCount","totalBytes","eligibleCount","eligibleBytes","retainLatest","maxCleanupCount","policy","canClean"],
    "properties":{
        "totalCount":{"type":"integer","format":"int64"},"totalBytes":{"type":"integer","format":"int64"},
        "eligibleCount":{"type":"integer","format":"int64"},"eligibleBytes":{"type":"integer","format":"int64"},
        "retainLatest":retain,"maxCleanupCount":{"type":"integer","format":"int32","default":limits::MAX_CLEANUP},
        "policy":{"type":"string"},"canClean":{"type":"boolean"}
    }});
    schemas["ApiTemplateHistoryCleanupPreviewRequest"] =
        json!({"type":"object","additionalProperties":false,"properties":{"retainLatest":retain}});
    schemas["ApiTemplateHistoryCleanupItem"] = json!({"type":"object","additionalProperties":false,"required":["id","recordVersion","versionNumber","byteLength"],"properties":{
        "id":{"type":"integer","format":"int64"},"recordVersion":{"type":"integer","format":"int64"},
        "versionNumber":{"type":"integer","format":"int64"},"byteLength":{"type":"integer","format":"int64"}
    }});
    schemas["ApiTemplateHistoryCleanupPreview"] = json!({"type":"object","additionalProperties":false,"required":["token","templateVersion","retention","items"],"properties":{
        "token":{"type":"string"},"templateVersion":{"type":"integer","format":"int64"},
        "retention":{"$ref":"#/components/schemas/ApiTemplateHistoryRetention"},
        "items":{"type":"array","maxItems":limits::MAX_CLEANUP,"items":{"$ref":"#/components/schemas/ApiTemplateHistoryCleanupItem"}}
    }});
    schemas["ApiTemplateHistoryCleanupCommitRequest"] = json!({"type":"object","additionalProperties":false,"required":["token","reason"],"properties":{
        "token":{"type":"string"},"reason":{"type":"string","minLength":1,"maxLength":1000}
    }});
    let path = "/api/reports/user-templates/{id}/versions";
    let mut read = doc["paths"][path]["get"].clone();
    read["operationId"] = json!("GetUserReportTemplateHistoryRetention");
    read["parameters"] = json!([{"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int64"}},
        {"name":"retainLatest","in":"query","schema":retain}]);
    read["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/ApiTemplateHistoryRetention"});
    doc["paths"][format!("{path}/retention")] = json!({"get":read});
    for (suffix, id, request, response) in [
        (
            "preview",
            "PreviewUserReportTemplateHistoryCleanup",
            "ApiTemplateHistoryCleanupPreviewRequest",
            "ApiTemplateHistoryCleanupPreview",
        ),
        (
            "commit",
            "CommitUserReportTemplateHistoryCleanup",
            "ApiTemplateHistoryCleanupCommitRequest",
            "ApiCommandResponse",
        ),
    ] {
        let mut action = doc["paths"]["/api/reports/user-templates/{id}"]["delete"].clone();
        action["operationId"] = json!(id);
        action["parameters"] = json!([{"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int64"}}]);
        action["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":{"$ref":format!("#/components/schemas/{request}")}}}});
        action["responses"]["200"]["content"]["application/json"]["schema"] =
            json!({"$ref":format!("#/components/schemas/{response}")});
        doc["paths"][format!("{path}/cleanup/{suffix}")] = json!({"post":action});
    }
}
