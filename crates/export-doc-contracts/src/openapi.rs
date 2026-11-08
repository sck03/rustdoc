//! The served API and both generated clients share the native declarations.
#[path = "native_contract/mod.rs"]
mod native;
pub use native::{DETAIL_FIELDS as OA_SHEET_DETAIL_FIELDS, SHEET_FIELDS as OA_SHEET_FIELDS};
use serde_json::Value;
use std::sync::OnceLock;

pub fn document() -> Value {
    static DOCUMENT: OnceLock<Value> = OnceLock::new();
    DOCUMENT.get_or_init(native::document).clone()
}

#[cfg(test)]
mod tests {
    use super::*;
    fn difference(path: &str, actual: &Value, expected: &Value) -> Option<String> {
        if actual == expected {
            return None;
        }
        match (actual, expected) {
            (Value::Object(a), Value::Object(b)) => {
                for key in a.keys().chain(b.keys()) {
                    match (a.get(key), b.get(key)) {
                        (Some(a), Some(b)) => {
                            if let Some(result) = difference(&format!("{path}.{key}"), a, b) {
                                return Some(result);
                            }
                        }
                        _ => return Some(format!("{path}.{key}: field presence changed")),
                    }
                }
            }
            (Value::Array(a), Value::Array(b)) if a.len() == b.len() => {
                for (i, (a, b)) in a.iter().zip(b).enumerate() {
                    if let Some(result) = difference(&format!("{path}[{i}]"), a, b) {
                        return Some(result);
                    }
                }
            }
            _ => {}
        }
        Some(format!("{path}: {actual} != {expected}"))
    }
    #[test]
    fn native_definition_preserves_the_reviewed_served_contract_and_all_metadata() {
        let mut baseline: Value = serde_json::from_str(include_str!(
            "../tests/fixtures/native-contract-baseline.json"
        ))
        .unwrap();
        let login = &mut baseline["paths"]["/api/auth/login"]["post"];
        login["requestBody"]["x-exportdoc-max-bytes"] = serde_json::json!(4096);
        login["responses"]["413"] = serde_json::json!({
            "description":"Payload Too Large",
            "content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiErrorResponse"}}}
        });
        for path in baseline["paths"].as_object_mut().unwrap().values_mut() {
            for operation in path.as_object_mut().unwrap().values_mut() {
                if let Some(id) = operation["operationId"].as_str() {
                    operation["x-exportdoc-timeout-seconds"] =
                        serde_json::json!(crate::request_limits::seconds(id));
                }
            }
        }
        // Ordinary backup directories only know file modification time, not snapshot time.
        for name in ["ApiBackupItemDto", "ApiSharedDatabaseBackupItemDto"] {
            let schema = &mut baseline["components"]["schemas"][name];
            schema["properties"]
                .as_object_mut()
                .unwrap()
                .remove("createdAt");
            schema["required"]
                .as_array_mut()
                .unwrap()
                .retain(|field| field != "createdAt");
        }
        // Declared metadata corrections and additive schedule observability.
        baseline["info"]["description"] =
            serde_json::json!("Shared Rust API for ExportDocManager desktop and browser clients.");
        baseline["components"]["securitySchemes"]["DesktopAccess"]["description"] =
            serde_json::json!("Internal desktop token used by lifecycle and local-file endpoints.");
        let schedule = &mut baseline["components"]["schemas"]["ApiRuntimeMetricsResponse"]["properties"]
            ["workflowSchedule"];
        let properties = schedule["properties"].as_object_mut().unwrap();
        properties.extend(
            serde_json::json!({
                "lastSuccessAt":{"type":["string","null"],"format":"date-time"},
                "panics":{"type":"integer","minimum":0,"format":"int64"},
                "consecutivePanics":{"type":"integer","minimum":0,"format":"int64"},
                "blocked":{"type":"boolean"}, "threadAlive":{"type":"boolean"}
            })
            .as_object()
            .unwrap()
            .clone(),
        );
        let exchange = schedule.clone();
        schedule["properties"]["exchange"] = exchange;
        // Reviewed desktop archive export: retain the backup administrator policy,
        // bearer/desktop authentication and all other frozen endpoint metadata.
        let mut save = baseline["paths"]["/api/disaster-recovery/plan/run"]["post"].clone();
        save["operationId"] = serde_json::json!("StartRecoveryArchiveSaveToPathJob");
        save["parameters"] = baseline["paths"]["/api/disaster-recovery/archives/{id}/download-ticket"]
            ["post"]["parameters"].clone();
        save["requestBody"] = serde_json::json!({"required":true,"content":{
            "application/json":{"schema":{"$ref":"#/components/schemas/ApiRecoveryArchiveSaveRequest"}}
        }});
        save["responses"].as_object_mut().unwrap().remove("409");
        for (code, description) in [
            ("400", "Invalid destination"),
            ("403", "Desktop administrator required"),
            ("429", "Busy"),
            ("503", "Storage unavailable"),
        ] {
            save["responses"][code]["description"] = serde_json::json!(description);
        }
        baseline["paths"]["/api/disaster-recovery/archives/{id}/save-to-path-jobs"] =
            serde_json::json!({"post":save});
        baseline["components"]["schemas"]["ApiRecoveryArchiveSaveRequest"] = serde_json::json!({
            "type":"object", "properties":{"destinationPath":{"type":"string","minLength":1,"maxLength":4096}},
            "required":["destinationPath"], "additionalProperties":false
        });
        if let Some(change) = difference("contract", &document(), &baseline) {
            panic!("undeclared contract change: {change}");
        }
    }
    #[test]
    fn generated_document_matches_the_native_definition() {
        let published: Value = serde_json::from_str(include_str!("openapi.json")).unwrap();
        if let Some(change) = difference("projection", &document(), &published) {
            panic!("regenerate both clients: {change}");
        }
    }
}
