//! Reviewed contract changes, not a runtime compatibility path.
use serde_json::{Value, json};
pub(super) fn apply(baseline: &mut Value) {
    remove_attachment_classification(baseline);
    let schemas = &mut baseline["components"]["schemas"];
    schemas
        .as_object_mut()
        .unwrap()
        .remove("IdentityDepartmentOption");
    let candidates = &mut schemas["IdentityCandidatePage"];
    for key in ["departments", "departmentCount"] {
        assert!(
            candidates["properties"]
                .as_object_mut()
                .unwrap()
                .remove(key)
                .is_some()
        );
        candidates["required"]
            .as_array_mut()
            .unwrap()
            .retain(|value| value != key);
    }
    schemas["IdentityAssignmentOption"] = json!({"type":"object","properties":{
        "id":{"type":"string","pattern":"^[1-9][0-9]{0,18}$"},"code":{"type":"string"},"name":{"type":"string"},"isActive":{"type":"boolean"}
    },"required":["id","code","name","isActive"],"additionalProperties":false});
    schemas["IdentityAssignmentOptionPage"] = json!({"type":"object","properties":{
        "items":{"type":"array","items":{"$ref":"#/components/schemas/IdentityAssignmentOption"}},
        "selected":{"type":"array","items":{"$ref":"#/components/schemas/IdentityAssignmentOption"}},
        "totalCount":{"type":"integer","minimum":0,"format":"int64"}
    },"required":["items","selected","totalCount"],"additionalProperties":false});
    let mut endpoint = baseline["paths"]["/api/organization/positions"]["get"].clone();
    endpoint["operationId"] = json!("GetIdentityAssignmentOptions");
    endpoint["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/IdentityAssignmentOptionPage"});
    endpoint["x-exportdoc-foundation"] = json!({"kind":"assignment-options","action":"list"});
    endpoint["parameters"] = json!([
        {"name":"unitId","in":"query","schema":{"type":"string","pattern":"^[1-9][0-9]{0,18}$"}},
        {"name":"kind","in":"query","required":true,"schema":{"type":"string","enum":["projects","departments"]}},
        {"name":"keyword","in":"query","schema":{"type":"string","maxLength":100}},
        {"name":"selectedIds","in":"query","schema":{"type":"string","maxLength":4000}},
        {"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},
        {"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}
    ]);
    baseline["paths"]["/api/organization/assignment-options"] = json!({"get":endpoint});
    for suffix in ["import", "import-upload", "review"] {
        let access = &mut baseline["paths"][format!("/api/tools/letter-of-credit/{suffix}")]["post"]
            ["x-exportdoc-policy"]["access"];
        assert_eq!(
            access["resources"][0]["resourceKey"],
            "document.report-templates"
        );
        *access = json!({"match":"All","resources":[{"resourceKey":"document.invoices","actions":["operate"],"administratorOnly":false}]});
    }
    for path in baseline["paths"].as_object_mut().unwrap().values_mut() {
        for op in path.as_object_mut().unwrap().values_mut() {
            let schema = op["responses"]
                .as_object()
                .into_iter()
                .flatten()
                .find_map(|(status, response)| {
                    status
                        .starts_with('2')
                        .then(|| response["content"]["application/json"]["schema"]["$ref"].as_str())
                        .flatten()
                })
                .unwrap_or("")
                .rsplit('/')
                .next()
                .unwrap_or("");
            let bytes = match schema {
                "ApiLetterOfCreditImportResponse" => 4 * 1024 * 1024,
                "ApiOcrRecognizeImageResponse" | "ApiLoginResponse" => 8 * 1024 * 1024,
                _ => continue,
            };
            op["x-exportdoc-json-response-limit-bytes"] = json!(bytes);
        }
    }
}

fn remove_attachment_classification(baseline: &mut Value) {
    for (schema, field) in [
        ("OaAttachment", "classification"),
        ("OaMaterialPolicy", "allowRestrictedAttachments"),
    ] {
        assert!(
            baseline["components"]["schemas"][schema]["properties"]
                .as_object_mut()
                .unwrap()
                .remove(field)
                .is_some()
        );
    }
    for path in [
        "/api/office/personnel-requests/{id}/attachments",
        "/api/office/leave-requests/{id}/attachments",
        "/api/office/travel-requests/{id}/attachments",
        "/api/office/overtime-requests/{id}/attachments",
        "/api/office/expense-requests/{id}/attachments",
        "/api/office/purchase-requests/{id}/attachments",
        "/api/office/general-requests/{id}/attachments",
        "/api/office/bookings/{id}/attachments",
        "/api/office/supply-requests/{id}/attachments",
    ] {
        let schema = &mut baseline["paths"][path]["post"]["requestBody"]["content"]["multipart/form-data"]
            ["schema"];
        assert!(
            schema["properties"]
                .as_object_mut()
                .unwrap()
                .remove("classification")
                .is_some()
        );
        schema["additionalProperties"] = json!(false);
    }
}
