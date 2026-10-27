use serde_json::{Value, json};
#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "ApiHsCodeDto",
        &["requiresAnnualReview", "obsoleteReason", "obsoletedAt"],
    ),
    ("ApiHsCodeBatchDeleteRequest", &["expectedVersions"]),
];

pub(super) fn extend(doc: &mut Value) {
    doc["components"]["schemas"]["ApiHsCodeDto"]["properties"]["requiresAnnualReview"] = json!({"type":"boolean","default":false,"readOnly":true,"description":"Active evidence belongs to another business year; it cannot be used as a current tariff."});
    let schemas = &mut doc["components"]["schemas"];
    schemas["ApiHsCodeDto"]["properties"]["obsoleteReason"] =
        json!({"type":"string","readOnly":true});
    schemas["ApiHsCodeDto"]["properties"]["obsoletedAt"] =
        json!({"type":"string","format":"date-time","readOnly":true});
    schemas["ApiHsCodeVersionReference"] = json!({
        "type":"object","additionalProperties":false,"required":["id","rowVersion"],
        "properties":{"id":{"type":"integer","format":"int64","minimum":1},"rowVersion":{"type":"string","minLength":1}}
    });
    let versions = json!({"type":"array","minItems":1,"maxItems":crate::hs_maintenance::MAX_BATCH,"items":{"$ref":"#/components/schemas/ApiHsCodeVersionReference"}});
    schemas["ApiHsCodeBatchDeleteRequest"]["properties"]["expectedVersions"] = versions.clone();
    schemas["ApiHsCodeBatchDeleteRequest"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("expectedVersions"));
    schemas["ApiHsCodeObsoleteRequest"] = json!({
        "type":"object","additionalProperties":false,"required":["items","reason"],
        "properties":{"items":versions,"reason":{"type":"string","minLength":1,"maxLength":1000}}
    });
    schemas["ApiHsCodeMaintenanceItem"] = json!({"type":"object","additionalProperties":false,"required":["id","code","name","status","rowVersion"],"properties":{
        "id":{"type":"integer","format":"int64"},"code":{"type":"string"},"name":{"type":"string"},"status":{"type":"string"},"rowVersion":{"type":"string"},"updateTime":{"type":["string","null"],"format":"date-time"}
    }});
    schemas["ApiHsCodeObsoletePreview"] = json!({"type":"object","additionalProperties":false,"required":["items","totalCount","maxBatchCount","policy"],"properties":{
        "items":{"type":"array","maxItems":crate::hs_maintenance::MAX_BATCH,"items":{"$ref":"#/components/schemas/ApiHsCodeMaintenanceItem"}},
        "totalCount":{"type":"integer","format":"int64"},"maxBatchCount":{"type":"integer","default":crate::hs_maintenance::MAX_BATCH},"policy":{"type":"string"}
    }});
    doc["paths"]["/api/master-data/hs-codes/by-id/{id}"]["delete"]["parameters"]
        .as_array_mut().unwrap().push(json!({"name":"expectedRowVersion","in":"query","required":true,"schema":{"type":"string","minLength":1}}));
    doc["components"]["schemas"]["ApiHsCodeCleanupPreview"] = json!({
        "type":"object", "additionalProperties":false,
        "required":["token","items","eligibleCount","retainedCount","policy"],
        "properties":{
            "token":{"type":"string"}, "items":{"type":"array","items":{"$ref":"#/components/schemas/ApiHsCodeDto"}},
            "eligibleCount":{"type":"integer","format":"int32"}, "retainedCount":{"type":"integer","format":"int32"}, "policy":{"type":"string"}
        }
    });
    for (path, id, response, request) in [
        (
            "preview",
            "PreviewHsCodeCleanup",
            "ApiHsCodeCleanupPreview",
            None,
        ),
        (
            "commit",
            "CommitHsCodeCleanup",
            "ApiCommandResponse",
            Some("ApiHsCodeImportCommitRequest"),
        ),
    ] {
        let mut operation = doc["paths"]["/api/master-data/hs-codes/clear-all"]["post"].clone();
        operation["operationId"] = json!(id);
        operation["responses"]["200"]["content"]["application/json"]["schema"] =
            json!({"$ref":format!("#/components/schemas/{response}")});
        operation.as_object_mut().unwrap().remove("requestBody");
        if let Some(request) = request {
            operation["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":{"$ref":format!("#/components/schemas/{request}")}}}});
        }
        doc["paths"][format!("/api/master-data/hs-codes/cleanup/{path}")] =
            json!({"post":operation});
    }
    let mut confirm = doc["paths"]["/api/master-data/hs-codes/clear-all"]["post"].clone();
    confirm["operationId"] = json!("ConfirmHsCodesObsolete");
    confirm["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiHsCodeObsoleteRequest"}}}});
    doc["paths"]["/api/master-data/hs-codes/confirm-obsolete"] = json!({"post":confirm});
    let mut preview = doc["paths"]["/api/master-data/hs-codes/cleanup/preview"]["post"].clone();
    preview["operationId"] = json!("PreviewHsCodesObsolete");
    preview["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/ApiHsCodeObsoletePreview"});
    doc["paths"]["/api/master-data/hs-codes/obsolete/preview"] = json!({"post":preview});
}
