//! The served API and both generated clients share the native declarations.
#[path = "native_contract/mod.rs"]
mod native;
pub use native::{DETAIL_FIELDS as OA_SHEET_DETAIL_FIELDS, SHEET_FIELDS as OA_SHEET_FIELDS};
use serde_json::Value;
#[cfg(test)]
#[path = "../tests/fixtures/account_security.rs"]
mod account_security;
#[cfg(test)]
#[path = "contract_audit_fixes.rs"]
mod audit_fixes;
#[cfg(test)]
#[path = "contract_browser_downloads.rs"]
mod browser_downloads;
#[cfg(test)]
#[path = "../tests/fixtures/oa_payment_labels.rs"]
mod oa_payment_labels;
#[cfg(test)]
#[path = "contract_organization_quality.rs"]
mod organization_quality;
use std::sync::OnceLock;

pub fn document() -> Value {
    static DOCUMENT: OnceLock<Value> = OnceLock::new();
    DOCUMENT.get_or_init(native::document).clone()
}

#[cfg(test)]
#[path = "contract_business_consistency.rs"]
mod business_consistency;
#[cfg(test)]
#[path = "contract_import_preview.rs"]
mod contract_import_preview;
#[cfg(test)]
#[path = "contract_custom_oa.rs"]
mod custom_oa;
#[cfg(test)]
#[path = "contract_evolution.rs"]
mod evolution;
#[cfg(test)]
#[path = "contract_file_storage.rs"]
mod file_storage;
#[cfg(test)]
#[path = "contract_payment_goods.rs"]
mod payment_goods;
#[cfg(test)]
#[path = "contract_precision_stability.rs"]
mod precision_stability;
#[cfg(test)]
#[path = "contract_review_cleanup.rs"]
mod review_cleanup;
#[cfg(test)]
#[path = "contract_supplier_resources.rs"]
mod supplier_resources;
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
        // Reviewed removal: database-only WebDAV snapshots are replaced by
        // complete encrypted recovery replicas. No deployed legacy clients.
        for path in [
            "status",
            "test-connection",
            "upload-latest",
            "backups",
            "download",
        ] {
            assert!(
                baseline["paths"]
                    .as_object_mut()
                    .unwrap()
                    .remove(&format!("/api/backup/cloud/{path}"))
                    .is_some()
            );
        }
        for schema in [
            "ApiCloudBackupCommandResponse",
            "ApiCloudBackupDownloadRequest",
            "ApiCloudBackupItemDto",
            "ApiCloudBackupListResponse",
            "ApiCloudBackupStatusResponse",
        ] {
            assert!(
                baseline["components"]["schemas"]
                    .as_object_mut()
                    .unwrap()
                    .remove(schema)
                    .is_some()
            );
        }
        let login = &mut baseline["paths"]["/api/auth/login"]["post"];
        login["requestBody"]["x-exportdoc-max-bytes"] = serde_json::json!(4096);
        login["responses"]["413"] = serde_json::json!({
            "description":"Payload Too Large",
            "content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiErrorResponse"}}}
        });
        for path in baseline["paths"].as_object_mut().unwrap().values_mut() {
            for operation in path.as_object_mut().unwrap().values_mut() {
                if let Some(id) = operation["operationId"].as_str().map(str::to_owned) {
                    operation["x-exportdoc-timeout-seconds"] =
                        serde_json::json!(crate::request_limits::seconds(&id));
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
        // Master-data deletes require the row version already enforced by storage.
        for kind in [
            "customers",
            "exporters",
            "payees",
            "products",
            "ports",
            "units",
        ] {
            baseline["paths"][format!("/api/master-data/{kind}/{{id}}")]["delete"]["parameters"]
                .as_array_mut()
                .unwrap()
                .push(serde_json::json!({
                    "name":"rowVersion", "in":"query", "required":true,
                    "schema":{"type":"string", "minLength":1}
                }));
        }
        evolution::reviewed_versioning(&mut baseline);
        evolution::reviewed_independent_declarations(&mut baseline);
        custom_oa::apply(&mut baseline);
        file_storage::apply(&mut baseline);
        business_consistency::apply(&mut baseline);
        supplier_resources::apply(&mut baseline);
        precision_stability::apply(&mut baseline);
        review_cleanup::apply(&mut baseline);
        organization_quality::apply(&mut baseline);
        // Reviewed retirement: access is the sole authorization definition.
        // Keep every access rule, authentication flag, schema and error in the comparison.
        for path in baseline["paths"].as_object_mut().unwrap().values_mut() {
            for operation in path.as_object_mut().unwrap().values_mut() {
                if let Some(policy) = operation["x-exportdoc-policy"].as_object_mut() {
                    for key in ["permissions", "requirements", "permissionBypass"] {
                        policy.remove(key);
                    }
                }
            }
        }
        // Apply resource metadata after the reviewed endpoint/body changes, so
        // newly added binary and multipart inputs receive the same admission rule.
        for path in baseline["paths"].as_object_mut().unwrap().values_mut() {
            for operation in path.as_object_mut().unwrap().values_mut() {
                if let Some(id) = operation["operationId"].as_str() {
                    operation["x-exportdoc-processing-slot"] = serde_json::json!(
                        matches!(
                            id,
                            "RecognizeOcrImage"
                                | "SaveShippingMarkImage"
                                | "SaveInvoiceSealImage"
                                | "PreviewShippingMarkImage"
                        ) || operation["requestBody"]["content"].as_object().is_some_and(
                            |content| content.keys().any(|mime| !mime.contains("json"))
                        )
                    );
                }
            }
        }
        // A delivery attempt key distinguishes HTTP retries from intentional resends;
        // the path parameter, body, authentication, errors and permissions stay frozen.
        baseline["paths"]["/api/reports/invoices/{invoiceId}/document-email"]["post"]["parameters"]
            .as_array_mut()
            .unwrap()
            .push(serde_json::json!({
                "name":"Idempotency-Key", "in":"header", "schema":{"type":"string"}
            }));
        // Reviewed replacement of the truncated packing directory with searchable pages.
        baseline["paths"]["/api/tools/container-packing/projects"]["get"]["parameters"] = serde_json::json!([
            {"name":"keyword","in":"query","schema":{"type":"string"}},
            {"name":"pageNumber","in":"query","schema":{"type":["integer","string"],"format":"int32","pattern":"^-?(?:0|[1-9]\\d*)$"}},
            {"name":"pageSize","in":"query","schema":{"type":["integer","string"],"format":"int32","pattern":"^-?(?:0|[1-9]\\d*)$"}}
        ]);
        let packing_page =
            &mut baseline["components"]["schemas"]["ApiContainerPackingProjectListResponse"];
        for (name, minimum) in [("totalCount", 0), ("pageNumber", 1), ("pageSize", 1)] {
            packing_page["properties"][name] =
                serde_json::json!({"type":"integer","minimum":minimum});
            packing_page["required"]
                .as_array_mut()
                .unwrap()
                .push(serde_json::json!(name));
        }
        audit_fixes::apply(&mut baseline);
        contract_import_preview::apply(&mut baseline);
        browser_downloads::apply(&mut baseline);
        account_security::apply(&mut baseline);
        custom_oa::expense_categories(&mut baseline);
        payment_goods::apply(&mut baseline);
        oa_payment_labels::apply(&mut baseline);
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
