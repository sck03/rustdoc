//! Official contract composition. The frozen .NET export is preserved verbatim;
//! new Rust capabilities are added here and both clients consume the same output.
mod accounts;
mod communication;
mod observability;
mod office;
mod payment_printing;
mod personnel;
mod reports;
use serde_json::Value;

pub fn document() -> Value {
    let mut document: Value = serde_json::from_str(include_str!("reference_openapi.json"))
        .expect("reviewed reference OpenAPI");
    office::extend(&mut document);
    communication::extend(&mut document);
    personnel::extend(&mut document);
    reports::extend(&mut document);
    observability::extend(&mut document);
    accounts::extend(&mut document);
    payment_printing::extend(&mut document);
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
        current["components"]["schemas"]["PersonnelRecord"]["properties"]
            .as_object_mut()
            .unwrap()
            .remove("attachments");
        for (schema, fields) in reports::FIELDS.iter().chain(accounts::FIELDS) {
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
        ] {
            let properties = current["components"]["schemas"][name]["properties"]
                .as_object_mut()
                .unwrap();
            assert_eq!(properties.remove("handlingKey").unwrap()["type"], "string");
            if name != "OfficeSupplySaveRequest" {
                for field in ["handlingName", "handlerNames", "canHandle"] {
                    assert!(properties.remove(field).is_some());
                }
            }
        }
        let params = current["paths"]["/api/office/supply-requests"]["get"]["parameters"]
            .as_array_mut()
            .unwrap();
        assert_eq!(params.pop().unwrap()["name"], "handlingOnly");
        for (path, methods) in baseline["paths"].as_object().unwrap() {
            assert_eq!(&current["paths"][path], methods, "endpoint drift: {path}");
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
