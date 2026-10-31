use serde_json::{Value, json};
#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[
    ("ApiUserDto", &["permissionSchemeVersion"]),
    ("ApiUserAccountDto", &["permissionSchemeVersion"]),
    ("ApiUserSaveRequest", &["permissionSchemeVersion"]),
    ("ApiPermissionTemplateDto", &["publishedVersion"]),
    ("ApiPermissionTemplateOptionDto", &["publishedVersion"]),
];
fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
fn object(properties: Value, required: &[&str]) -> Value {
    json!({"type":"object","additionalProperties":false,"properties":properties,"required":required})
}
pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let id = json!({"type":"string","pattern":"^[1-9][0-9]{0,18}$"});
    let version = json!({"type":"integer","format":"int64","minimum":1});
    let optional_version =
        json!({"type":["integer","null"],"format":"int64","minimum":1,"default":null});
    for name in [
        "ApiUserDto",
        "ApiUserAccountDto",
        "ApiUserSaveRequest",
        "UnitAccessSave",
        "UnitAccessRecord",
    ] {
        s[name]["properties"]["permissionSchemeVersion"] = optional_version.clone();
    }
    s["ApiPermissionTemplateDto"]["properties"]["publishedVersion"] = optional_version.clone();
    s["ApiPermissionTemplateOptionDto"]["properties"]["publishedVersion"] =
        optional_version.clone();
    s["PermissionSchemeVersion"] = object(
        json!({"schemeId":id,"version":version,"publishedAt":{"type":"string","format":"date-time"}}),
        &["schemeId", "version", "publishedAt"],
    );
    s["PermissionSchemeVersions"] = object(
        json!({"items":{"type":"array","items":reference("PermissionSchemeVersion")},"totalCount":{"type":"integer"},"pageNumber":{"type":"integer"},"pageSize":{"type":"integer"}}),
        &["items", "totalCount", "pageNumber", "pageSize"],
    );
    s["PermissionBindingTarget"] = object(
        json!({"kind":{"type":"string","enum":["Account","Unit"]},"id":id,"expectedVersion":version}),
        &["kind", "id", "expectedVersion"],
    );
    s["PermissionSchemePublish"] = object(
        json!({"schemeId":id,"expectedVersion":version,"note":{"type":"string","minLength":1,"maxLength":500},"commandKey":{"type":"string","minLength":8,"maxLength":128}}),
        &["schemeId", "expectedVersion", "note", "commandKey"],
    );
    s["PermissionSchemeApply"] = object(
        json!({"schemeId":id,"version":version,"targets":{"type":"array","items":reference("PermissionBindingTarget"),"minItems":1,"maxItems":200},"note":{"type":"string","minLength":1,"maxLength":500},"commandKey":{"type":"string","minLength":8,"maxLength":128}}),
        &["schemeId", "version", "targets", "note", "commandKey"],
    );
    s["PermissionBindingImpact"] = object(
        json!({"kind":{"type":"string","enum":["Account","Unit"]},"id":id,"versionNumber":version,"fromVersion":optional_version,"toVersion":version,"addedActions":{"type":"array","items":{"type":"string"}},"removedActions":{"type":"array","items":{"type":"string"}}}),
        &[
            "kind",
            "id",
            "versionNumber",
            "fromVersion",
            "toVersion",
            "addedActions",
            "removedActions",
        ],
    );
    s["PermissionSchemeApplication"] = object(
        json!({"schemeId":id,"version":version,"targets":{"type":"array","items":reference("PermissionBindingImpact")},"applied":{"type":"boolean"}}),
        &["schemeId", "version", "targets", "applied"],
    );
    s["PermissionSchemePublication"] = object(
        json!({"schemeId":id,"version":version,"versionNumber":version,"publishedAt":{"type":"string","format":"date-time"}}),
        &["schemeId", "version", "versionNumber", "publishedAt"],
    );
    for (path, method, operation, action, request, response) in [
        (
            "permission-versions",
            "get",
            "GetPermissionSchemeVersions",
            "versions",
            None,
            "PermissionSchemeVersions",
        ),
        (
            "permission-versions/definition",
            "get",
            "GetPermissionSchemeVersion",
            "version",
            None,
            "ApiPermissionTemplateDto",
        ),
        (
            "permission-versions/publish",
            "post",
            "PublishPermissionScheme",
            "publish",
            Some("PermissionSchemePublish"),
            "PermissionSchemePublication",
        ),
        (
            "permission-versions/preview",
            "post",
            "PreviewPermissionSchemeApplication",
            "preview",
            Some("PermissionSchemeApply"),
            "PermissionSchemeApplication",
        ),
        (
            "permission-versions/apply",
            "post",
            "ApplyPermissionSchemeVersion",
            "apply",
            Some("PermissionSchemeApply"),
            "PermissionSchemeApplication",
        ),
    ] {
        let mut endpoint = doc["paths"]["/api/organization/unit-tasks"]["get"].clone();
        endpoint["operationId"] = json!(operation);
        endpoint["x-exportdoc-foundation"] = json!({"kind":"permission-versions","action":action});
        endpoint["x-exportdoc-policy"]["requirements"] =
            json!([{"resourceKey":"organization.roles","action":"manage"}]);
        endpoint["parameters"] = if method == "get" {
            json!([{"name":"schemeId","in":"query","required":true,"schema":id},{"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},{"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}])
        } else {
            json!([])
        };
        if action == "version" {
            endpoint["parameters"]
                .as_array_mut()
                .unwrap()
                .push(json!({"name":"version","in":"query","required":true,"schema":version}));
        }
        if let Some(request) = request {
            endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference(request)}}});
        }
        endpoint["responses"]["200"]["content"]["application/json"]["schema"] = reference(response);
        doc["paths"][format!("/api/organization/{path}")][method] = endpoint;
    }
}
