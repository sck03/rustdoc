//! Validate intentional unit contract changes before comparing the untouched reference.
use serde_json::{Value, json};

pub(in crate::openapi) fn compare_evolved_contract(current: &mut Value, baseline: &Value) {
    let parameters = current["paths"]["/api/office/people/options"]["get"]["parameters"]
        .as_array_mut()
        .unwrap();
    assert_eq!(
        parameters.pop().unwrap(),
        json!({"name":"companyCode","in":"query","schema":{"type":"string","maxLength":50}})
    );
    for name in ["ApiOrganizationCompanyDto", "ApiOrganizationDepartmentDto"] {
        let schema = &mut current["components"]["schemas"][name];
        assert_eq!(
            schema["properties"]["id"],
            json!({"type":"integer","format":"int64","readOnly":true})
        );
        assert_eq!(
            schema["required"].as_array_mut().unwrap().pop().unwrap(),
            "id"
        );
    }
    for entity in ["companies", "departments"] {
        let old = format!("/api/organization-directory/{entity}/{{code}}");
        let new = format!("/api/organization-directory/{entity}/{{id}}");
        let paths = current["paths"].as_object_mut().unwrap();
        assert!(
            !paths.contains_key(&old),
            "Retired routes must not remain callable"
        );
        let mut methods = paths.remove(&new).expect("Stable-ID route");
        for (method, operation) in methods.as_object_mut().unwrap() {
            let parameter = operation["parameters"]
                .as_array_mut()
                .unwrap()
                .iter_mut()
                .find(|p| p["in"] == "path")
                .unwrap();
            assert_eq!(
                *parameter,
                json!({"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int64","minimum":1}})
            );
            *parameter = baseline["paths"][&old][method]["parameters"]
                .as_array()
                .unwrap()
                .iter()
                .find(|p| p["in"] == "path")
                .unwrap()
                .clone();
        }
        paths.insert(old, methods);
    }
    let reference_operations: Vec<&Value> = baseline["paths"]
        .as_object()
        .unwrap()
        .values()
        .flat_map(|methods| methods.as_object().unwrap().values())
        .collect();
    for methods in current["paths"].as_object_mut().unwrap().values_mut() {
        for operation in methods.as_object_mut().unwrap().values_mut() {
            let (resource, action) = match operation["operationId"].as_str().unwrap_or("") {
                "ListOrganizationManagers" => ("organization.directory", "view"),
                "GetOrganizationDirectory"
                | "CreateOrganizationCompany"
                | "UpdateOrganizationCompany"
                | "DeleteOrganizationCompany"
                | "CreateOrganizationDepartment"
                | "UpdateOrganizationDepartment"
                | "DeleteOrganizationDepartment" => ("organization.directory", "manage"),
                "GetOaApprovalSettings"
                | "SaveOaApprovalSettings"
                | "PublishOaDefinition"
                | "DisableOaDefinition"
                | "GetOaApproverOptions" => ("organization.workflows", "manage"),
                _ => continue,
            };
            assert_eq!(
                operation["x-exportdoc-policy"]["requirements"],
                json!([{"resourceKey":resource,"action":action}])
            );
            if let Some(reference) = reference_operations
                .iter()
                .find(|r| r["operationId"] == operation["operationId"])
            {
                // Only the declared resource changes; authentication, errors and the other policy fields are compared by the caller.
                operation["x-exportdoc-policy"]["requirements"] =
                    reference["x-exportdoc-policy"]["requirements"].clone();
                if operation["x-exportdoc-office"].is_object() {
                    assert_eq!(operation["x-exportdoc-office"]["resource"], resource);
                    operation["x-exportdoc-office"]["resource"] =
                        reference["x-exportdoc-office"]["resource"].clone();
                }
            }
        }
    }
}
