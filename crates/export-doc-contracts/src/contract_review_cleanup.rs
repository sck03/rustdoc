//! Explicitly reviewed retirement; preserve every unrelated schema and endpoint property.
use serde_json::Value;
pub(super) fn apply(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    for field in ["companyScope", "departmentId", "ownerUserId"] {
        schemas["ApiInvoiceListItemDto"]["properties"][field] =
            schemas["ApiInvoiceDetailDto"]["properties"][field].clone();
    }
    schemas["ApiInvoiceListItemDto"]["required"]
        .as_array_mut()
        .unwrap()
        .extend([
            serde_json::json!("companyScope"),
            serde_json::json!("departmentId"),
        ]);
    // Publish resolved restrictions to align frontend record actions with the backend.
    let schemas = &mut baseline["components"]["schemas"];
    schemas["ApiPermissionDepartmentScopeDto"] = serde_json::json!({
        "type":"object", "properties":{"companyScope":{"type":"string"},"departmentId":{"type":"string"}},
        "required":["companyScope","departmentId"]
    });
    schemas["ApiPermissionScopeRestrictionDto"] = serde_json::json!({
        "type":"object", "properties":{
            "resourceKey":{"type":"string"}, "action":{"type":"string"},
            "mode":{"type":"string","enum":["Deny","Unrestricted","Units","Departments","Projects","Participation"]},
            "unitCodes":{"type":"array","items":{"type":"string"}},
            "departments":{"type":"array","items":{"$ref":"#/components/schemas/ApiPermissionDepartmentScopeDto"}},
            "projectIds":{"type":"array","items":{"type":"string"}}
        }, "required":["resourceKey","action","mode","unitCodes","departments","projectIds"]
    });
    schemas["ApiUserCapabilitiesDto"]["properties"]["scopeRestrictions"] = serde_json::json!({
        "type":"array","items":{"$ref":"#/components/schemas/ApiPermissionScopeRestrictionDto"}
    });
    let required = schemas["ApiUserCapabilitiesDto"]["required"]
        .as_array_mut()
        .unwrap();
    let permissions = required
        .iter()
        .position(|name| name == "permissions")
        .unwrap();
    required.insert(permissions + 1, serde_json::json!("scopeRestrictions"));

    for (name, fields) in [
        (
            "ApiDashboardResponse",
            &[
                "monthlyExportAmount",
                "previousMonthlyExportAmount",
                "exportAmountCurrency",
            ][..],
        ),
        (
            "ApiSingleWindowImportPackageRequest",
            &["workingDirectory", "keepWorkingDirectory"][..],
        ),
        (
            "ApiSingleWindowImportedPackageResponse",
            &["workingDirectory", "workingDirectoryKept"][..],
        ),
    ] {
        let schema = &mut baseline["components"]["schemas"][name];
        for field in fields {
            assert!(
                schema["properties"]
                    .as_object_mut()
                    .unwrap()
                    .remove(*field)
                    .is_some()
            );
        }
        if let Some(required) = schema["required"].as_array_mut() {
            required.retain(|field| !fields.contains(&field.as_str().unwrap()));
        }
    }
    for path in [
        "/api/single-window/packages/upload",
        "/api/single-window/receipts/upload",
    ] {
        baseline["paths"][path]["post"]["parameters"]
            .as_array_mut()
            .unwrap()
            .retain(|p| {
                !matches!(
                    p["name"].as_str(),
                    Some("workingDirectory" | "keepWorkingDirectory")
                )
            });
    }
    assert!(
        baseline["paths"]
            .as_object_mut()
            .unwrap()
            .remove("/api/single-window/receipts/download-package")
            .is_some()
    );
}
