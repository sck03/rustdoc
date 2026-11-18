//! Independently stated changes to the frozen contract, used only by the regression comparison.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    baseline["x-exportdoc-json-response-limit-bytes"] = json!(67108864);
    let paths = baseline["paths"].as_object_mut().unwrap();
    for (path, method) in [
        ("/api/master-data/customers", "get"),
        ("/api/master-data/exporters", "get"),
        ("/api/master-data/payees", "get"),
        ("/api/master-data/ports", "get"),
        ("/api/master-data/hs-codes/import-path", "post"),
        ("/api/master-data/hs-codes/import-upload", "post"),
        ("/api/invoices/hs-knowledge/feedback", "post"),
    ] {
        let path_item = paths[path].as_object_mut().unwrap();
        assert!(
            path_item.remove(method).is_some(),
            "retired {method} {path}"
        );
        if path_item.is_empty() {
            paths.remove(path);
        }
    }
    paths["/api/crm/opportunities"]["get"]["parameters"]
        .as_array_mut()
        .unwrap()
        .insert(
            0,
            json!({"name":"includeArchived","in":"query","schema":{"type":"boolean"}}),
        );
    let history = &mut paths["/api/crm/opportunities/{id}/history"]["get"];
    history["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/ApiSalesOpportunityHistoryPage"});
    history["parameters"].as_array_mut().unwrap().extend([
        json!({"name":"beforeId","in":"query","schema":{"type":"integer","format":"int64","minimum":1}}),
        json!({"name":"limit","in":"query","schema":{"type":"integer","minimum":1,"maximum":200,"default":30}}),
    ]);
    let schemas = baseline["components"]["schemas"].as_object_mut().unwrap();
    for name in ["ApiHsCodeImportPathRequest", "ApiHsCodeImportResponse"] {
        assert!(schemas.remove(name).is_some());
    }
    schemas["ApiSalesOpportunityDto"]["properties"]["isArchived"] = json!({"type":"boolean"});
    let required = schemas["ApiSalesOpportunityDto"]["required"]
        .as_array_mut()
        .unwrap();
    let index = required.iter().position(|field| *field == "stage").unwrap() + 1;
    required.insert(index, json!("isArchived"));
    schemas.insert("ApiSalesOpportunityHistoryPage".into(), json!({"type":"object","properties":{
        "items":{"type":"array","items":{"$ref":"#/components/schemas/ApiSalesOpportunityHistoryDto"},"maxItems":200},
        "nextCursor":{"type":["null","integer"],"format":"int64"}
    },"required":["items","nextCursor"]}));
    let memory = schemas["ApiRuntimeJobMetrics"]["properties"]["downloadMemory"].clone();
    schemas["ApiRuntimeHttpMetrics"]["properties"]["responseMemory"] = memory;
    schemas["ApiRuntimeHttpMetrics"]["required"]
        .as_array_mut()
        .unwrap()
        .insert(0, json!("responseMemory"));
}
