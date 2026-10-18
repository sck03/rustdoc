use serde_json::{Value, json};
#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[("ApiHsCodeDto", &["requiresAnnualReview"])];

pub(super) fn extend(doc: &mut Value) {
    doc["components"]["schemas"]["ApiHsCodeDto"]["properties"]["requiresAnnualReview"] = json!({"type":"boolean","default":false,"readOnly":true,"description":"Active evidence belongs to another business year; it cannot be used as a current tariff."});
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
}
