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
                "使用人事与行政自助服务：公司通讯录、本人申请、公告和通知；不授予人事私密档案、审批或单证销售权限。"
            );
        }
    }
}
