//! Account-specific grants and module restrictions extend the frozen contract.
use serde_json::{Value, json};

pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "ApiUserSaveRequest",
        &["permissionGrants", "disabledModules"],
    ),
    (
        "ApiUserAccountDto",
        &["permissionGrants", "disabledModules"],
    ),
    ("ApiPermissionTemplateDto", &["disabledModules"]),
    ("ApiPermissionTemplateSaveRequest", &["disabledModules"]),
];

pub(super) fn extend(doc: &mut Value) {
    // Technical lookup helpers are derived, not independently assigned. A
    // write-capable helper must also be readable by the page using it.
    let mut dependencies = Vec::new();
    for resource in doc["x-exportdoc-permissions"]["resources"]
        .as_array()
        .unwrap()
    {
        let actions = resource["actions"].as_array().unwrap();
        if resource["isTechnical"] == true && actions.iter().any(|a| a["key"] == "view") {
            for action in actions
                .iter()
                .filter(|a| a["key"] == "operate" || a["key"] == "manage")
            {
                dependencies.push(json!({"resourceKey":resource["key"],"action":action["key"],
                    "grants":[{"resourceKey":resource["key"],"action":"view"}]}));
            }
        }
    }
    doc["x-exportdoc-permissions"]["dependencies"]
        .as_array_mut()
        .unwrap()
        .extend(dependencies);
    for (schema, fields) in FIELDS {
        for field in *fields {
            doc["components"]["schemas"][schema]["properties"][field] = if *field
                == "permissionGrants"
            {
                json!({"type":["array","null"],"items":{"$ref":"#/components/schemas/ApiPermissionGrantDto"},"maxItems":1000,"default":null,"description":"null inherits the selected group or role; an array replaces it, including an empty deny-all array."})
            } else {
                json!({"type":"array","items":{"type":"string"},"maxItems":100,"default":[],"description":"Disabled modules are removed from both effective permissions and navigation."})
            };
        }
    }
    for role in doc["x-exportdoc-permissions"]["roles"]
        .as_array_mut()
        .unwrap()
    {
        if role["code"] == "OfficeEmployee" {
            role["name"] = json!("普通员工");
            role["description"] = json!(
                "使用人事与行政自助服务、本人付款报销及个人票据模板设计打印；不授予私密人事档案、审批、发票报关或公共模板管理权限。"
            );
        }
    }
}
