//! Official contract composition. The frozen .NET export is preserved verbatim;
//! new Rust capabilities are added here and both clients consume the same output.
mod accounts;
mod backup_plan;
mod communication;
mod dashboard;
mod exchange;
mod hs_lifecycle;
mod invoice_editing;
mod observability;
mod office;
mod payment_printing;
mod personnel;
mod product_queries;
mod report_preview;
mod reports;
mod template_history;
pub use office::documents::DETAIL_FIELDS as OA_SHEET_DETAIL_FIELDS;
pub use office::documents::SHEET_FIELDS as OA_SHEET_FIELDS;
use serde_json::Value;

pub fn document() -> Value {
    let mut document: Value = serde_json::from_str(include_str!("reference_openapi.json"))
        .expect("reviewed reference OpenAPI");
    office::extend(&mut document);
    communication::extend(&mut document);
    reports::extend(&mut document);
    template_history::extend(&mut document);
    report_preview::extend(&mut document);
    observability::extend(&mut document);
    accounts::extend(&mut document);
    personnel::extend(&mut document);
    payment_printing::extend(&mut document);
    backup_plan::extend(&mut document);
    exchange::extend(&mut document);
    hs_lifecycle::extend(&mut document);
    dashboard::extend(&mut document);
    product_queries::extend(&mut document);
    invoice_editing::extend(&mut document);
    // The frozen baseline remains read-only; retired products leave the live catalog.
    document["x-exportdoc-permissions"]["editions"]
        .as_object_mut()
        .unwrap()
        .remove("Administration");
    document
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_document_matches_the_rust_composer() {
        let published: Value = serde_json::from_str(include_str!("openapi.json")).unwrap();
        assert!(
            published == document(),
            "Regenerate both clients from the Rust OpenAPI composer."
        );
    }

    #[test]
    fn preserves_every_reference_schema_endpoint_and_security_contract() {
        let baseline: Value = serde_json::from_str(include_str!("reference_openapi.json")).unwrap();
        let mut current = document();
        // OA now has the same workflow semantics on SQLite and PostgreSQL.
        // Verify this intentional removal without weakening the other frozen contracts.
        let capabilities = &mut current["components"]["schemas"]["ApiUserCapabilitiesDto"];
        assert!(
            capabilities["properties"]
                .get("usesOfficeRegister")
                .is_none()
        );
        assert!(
            !capabilities["required"]
                .as_array()
                .unwrap()
                .iter()
                .any(|f| f == "usesOfficeRegister")
        );
        capabilities["properties"]["usesOfficeRegister"] = baseline["components"]["schemas"]["ApiUserCapabilitiesDto"]["properties"]["usesOfficeRegister"].clone();
        capabilities["required"] =
            baseline["components"]["schemas"]["ApiUserCapabilitiesDto"]["required"].clone();
        for (schema, fields) in [
            ("OfficeDecisionRequest", &["taskKey"][..]),
            (
                "MeetingBookingRecord",
                &["approvalPlan", "canReview", "reviewTaskKeys"][..],
            ),
            (
                "OfficeSupplyRequestRecord",
                &["approvalPlan", "canReview", "reviewTaskKeys"][..],
            ),
            ("OfficeRequestEventRecord", &["approvalPlan"][..]),
        ] {
            for field in fields {
                assert!(
                    current["components"]["schemas"][schema]["properties"]
                        .as_object_mut()
                        .unwrap()
                        .remove(*field)
                        .is_some()
                );
            }
        }
        let parameters = current["paths"]["/api/master-data/products"]["get"]["parameters"]
            .as_array_mut()
            .unwrap();
        assert_eq!(parameters.pop().unwrap()["name"], "productCode");
        let version =
            current["paths"]["/api/master-data/hs-codes/by-id/{id}"]["delete"]["parameters"]
                .as_array_mut()
                .unwrap()
                .pop()
                .unwrap();
        assert_eq!(
            version,
            serde_json::json!({"name":"expectedRowVersion","in":"query","required":true,"schema":{"type":"string","minLength":1}})
        );
        assert_eq!(
            current["components"]["schemas"]["ApiHsCodeBatchDeleteRequest"]["required"]
                .as_array_mut()
                .unwrap()
                .pop()
                .unwrap(),
            "expectedVersions"
        );
        // The catalog now serves output users as well as template designers;
        // the use case additionally checks the requested document data domain.
        assert_eq!(
            current["paths"]["/api/reports/templates"]["get"]["x-exportdoc-policy"]["requirements"],
            serde_json::json!([{"resourceKey":payment_printing::CATALOG,"action":"view"}])
        );
        current["paths"]["/api/reports/templates"]["get"]["x-exportdoc-policy"] =
            baseline["paths"]["/api/reports/templates"]["get"]["x-exportdoc-policy"].clone();
        for methods in current["paths"].as_object_mut().unwrap().values_mut() {
            for operation in methods.as_object_mut().unwrap().values_mut() {
                if reports::MANAGED_FILES.contains(&operation["operationId"].as_str().unwrap_or(""))
                {
                    let requirements = operation["x-exportdoc-policy"]["requirements"]
                        .as_array_mut()
                        .unwrap();
                    assert_eq!(
                        requirements.pop().unwrap(),
                        serde_json::json!({"resourceKey":"system.settings","action":"manage"})
                    );
                }
            }
        }
        for (schema, fields) in reports::FIELDS
            .iter()
            .chain(accounts::FIELDS)
            .chain(personnel::FIELDS)
            .chain(exchange::FIELDS)
            .chain(hs_lifecycle::FIELDS)
            .chain(template_history::FIELDS)
            .chain(dashboard::FIELDS)
        {
            for field in *fields {
                current["components"]["schemas"][schema]["properties"]
                    .as_object_mut()
                    .unwrap()
                    .remove(*field);
            }
        }
        assert_eq!(
            current["components"]["schemas"]["PersonnelClearance"]["properties"]["approvalCount"]["type"],
            "integer"
        );
        current["components"]["schemas"]["PersonnelClearance"]["properties"]
            .as_object_mut()
            .unwrap()
            .remove("approvalCount");
        for field in ["handlingCount", "handlingServices"] {
            assert!(
                current["components"]["schemas"]["PersonnelClearance"]["properties"]
                    .as_object_mut()
                    .unwrap()
                    .remove(field)
                    .is_some()
            );
        }
        for name in [
            "OfficeSupplySaveRequest",
            "OfficeSupplyRecord",
            "OfficeSupplyRequestRecord",
            "MeetingRoomSaveRequest",
            "MeetingRoomRecord",
            "MeetingBookingRecord",
        ] {
            let properties = current["components"]["schemas"][name]["properties"]
                .as_object_mut()
                .unwrap();
            assert_eq!(properties.remove("handlingKey").unwrap()["type"], "string");
            if !name.ends_with("SaveRequest") {
                for field in ["handlingName", "handlerNames", "canHandle"] {
                    assert!(properties.remove(field).is_some());
                }
            }
        }
        for path in ["/api/office/supply-requests", "/api/office/bookings"] {
            let params = current["paths"][path]["get"]["parameters"]
                .as_array_mut()
                .unwrap();
            assert_eq!(
                params.pop().unwrap(),
                serde_json::json!({"name":"handlingOnly","in":"query","schema":{"type":"boolean"}})
            );
        }
        let parameters = current["paths"]["/api/office/people/options"]["get"]
            .as_object_mut()
            .unwrap()
            .remove("parameters")
            .unwrap();
        assert_eq!(parameters.as_array().unwrap().len(), 1);
        let employee = &parameters[0];
        assert_eq!(employee["name"], "employeeId");
        assert_eq!(employee["in"], "query");
        assert_eq!(employee["required"], false);
        assert_eq!(
            employee["schema"],
            serde_json::json!({"type":"integer","format":"int64","minimum":1})
        );
        for (path, methods) in baseline["paths"].as_object().unwrap() {
            if path == "/api/master-data/exporters/{id}/seals/{sealType}/upload" {
                let content = current["paths"][path]["post"]["requestBody"]["content"]
                    .as_object_mut()
                    .unwrap();
                for media in ["image/gif", "image/webp"] {
                    assert!(!content.contains_key(media));
                    content.insert(
                        media.into(),
                        methods["post"]["requestBody"]["content"][media].clone(),
                    );
                }
            }
            assert_eq!(&current["paths"][path], methods, "endpoint drift: {path}");
        }
        for (schemas, fields) in [
            (report_preview::REQUESTS, report_preview::REQUEST_FIELDS),
            (report_preview::RESPONSES, report_preview::RESPONSE_FIELDS),
        ] {
            for schema in schemas {
                for field in fields {
                    current["components"]["schemas"][schema]["properties"]
                        .as_object_mut()
                        .unwrap()
                        .remove(*field);
                }
            }
        }
        for (name, schema) in baseline["components"]["schemas"].as_object().unwrap() {
            assert_eq!(
                &current["components"]["schemas"][name], schema,
                "schema drift: {name}"
            );
        }
        assert_eq!(
            current["components"]["securitySchemes"],
            baseline["components"]["securitySchemes"]
        );
        for field in ["autoRefreshEnabled", "sourceFormat"] {
            assert!(
                current["x-exportdoc-configuration"]["defaults"]["exchangeRate"]
                    .as_object_mut()
                    .unwrap()
                    .remove(field)
                    .is_some()
            );
        }
        assert_eq!(
            current["x-exportdoc-configuration"],
            baseline["x-exportdoc-configuration"]
        );
        for operation in current["paths"]
            .as_object()
            .unwrap()
            .values()
            .flat_map(|v| v.as_object().unwrap().values())
        {
            assert!(operation.get("x-exportdoc-policy").is_some());
        }
    }
}
