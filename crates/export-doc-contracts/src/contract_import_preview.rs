//! Independently reviewed paging and attachment changes to the frozen contract.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    for (schema, property) in [
        ("ApiHsCodeImportPreviewResponse", "items"),
        ("BusinessAttachmentPage", "invoiceBytesLimit"),
    ] {
        assert!(
            schemas[schema]["properties"]
                .as_object_mut()
                .unwrap()
                .remove(property)
                .is_some()
        );
        schemas[schema]["required"]
            .as_array_mut()
            .unwrap()
            .retain(|value| value != property);
    }
    let item = &mut schemas["ApiHsCodeImportPreviewItemDto"];
    assert!(
        item["properties"]
            .as_object_mut()
            .unwrap()
            .remove("changedFields")
            .is_some()
    );
    item["properties"]["changes"] = json!({"type":"array","items":{"$ref":"#/components/schemas/ApiHsCodeImportFieldChangeDto"}});
    for field in item["required"].as_array_mut().unwrap() {
        if field == "changedFields" {
            *field = json!("changes");
        }
    }
    schemas["ApiHsCodeImportFieldChangeDto"] = json!({"type":"object","properties":{"field":{"type":"string"},"before":{"type":"string"},"after":{"type":"string"}},"required":["field","before","after"]});
    let mut page = schemas["ApiPagedResponseOfApiCustomerDto"].clone();
    page["properties"]["items"]["items"]["$ref"] =
        json!("#/components/schemas/ApiHsCodeImportPreviewItemDto");
    schemas["ApiPagedResponseOfApiHsCodeImportPreviewItemDto"] = page;
    baseline["paths"]["/api/business-attachments"]["get"]["parameters"].as_array_mut().unwrap().insert(0,
        json!({"name":"categoryId","in":"query","schema":{"type":"integer","format":"int32","minimum":1}}));
    let mut responses = json!({"200":{"description":"OK","content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiPagedResponseOfApiHsCodeImportPreviewItemDto"}}}}});
    for (code, description) in [
        ("400", "Bad Request"),
        ("401", "Unauthorized"),
        ("403", "Forbidden"),
        ("404", "Not Found"),
        ("409", "Conflict"),
        ("429", "Too Many Requests"),
        ("503", "Service Unavailable"),
        ("504", "Gateway Timeout"),
    ] {
        responses[code] = json!({"description":description,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiErrorResponse"}}}});
    }
    baseline["paths"]["/api/master-data/hs-codes/import-previews/{token}/items"] = json!({"get":{
        "operationId":"ListHsCodeImportPreviewItems", "tags":["ExportDocManager.Api"],
        "x-exportdoc-json-response-limit-bytes":67108864,"x-exportdoc-timeout-seconds":30,"x-exportdoc-processing-slot":false,
        "x-exportdoc-policy":{"requiresAuthentication":true,"requiresDesktopAccess":true,"requiresLicense":true,
            "access":{"match":"All","resources":[{"resourceKey":"document.hs-knowledge","actions":["manage"],"administratorOnly":false}]}},
        "security":[{"BearerAuth":[],"DesktopAccess":[]}], "responses":responses,
        "parameters":[
            {"name":"token","in":"path","required":true,"schema":{"type":"string","pattern":"^[a-f0-9]{32}$"}},
            {"name":"changeType","in":"query","schema":{"type":"string","enum":["Add","Update","Unchanged","SuspectedObsolete","Conflict","Invalid"]}},
            {"name":"pageNumber","in":"query","schema":{"type":"integer","format":"int32","minimum":1}},
            {"name":"pageSize","in":"query","schema":{"type":"integer","format":"int32","minimum":1,"maximum":200}}
        ]
    }});
}
