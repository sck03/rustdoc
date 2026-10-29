//! Account-specific grants and module restrictions extend the frozen contract.
use serde_json::{Value, json};

pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "ApiUserSaveRequest",
        &["permissionGrants", "disabledModules"],
    ),
    (
        "ApiUserAccountDto",
        &["permissionGrants", "disabledModules", "employee"],
    ),
    ("ApiUserDto", &["employee"]),
    ("ApiPermissionTemplateDto", &["disabledModules"]),
    ("ApiPermissionTemplateSaveRequest", &["disabledModules"]),
];

pub(super) fn extend(doc: &mut Value) {
    for key in ["accountReferenceCount", "unitRoleReferenceCount"] {
        doc["components"]["schemas"]["ApiPermissionTemplateDto"]["properties"][key] =
            json!({"type":"integer","minimum":0,"readOnly":true,"default":0});
    }
    doc["components"]["schemas"]["ApiPermissionTemplateDto"]["properties"]["ownerUnitId"] =
        json!({"type":["integer","null"],"format":"int64","readOnly":true,"default":null});
    doc["components"]["schemas"]["PermissionSource"] = json!({"type":"object","required":["name","templateId","templateVersion","accountDisabledModules","identityDisabledModules","schemeDisabledModules","authorityDisabledModules","startsAt","endsAt","oaAssignedOnly"],"properties":{
        "name":{"type":"string"},"templateId":{"type":["integer","null"],"format":"int64"},"templateVersion":{"type":["integer","null"],"format":"int64"},
        "accountDisabledModules":{"type":"array","items":{"type":"string"}},"identityDisabledModules":{"type":"array","items":{"type":"string"}},"schemeDisabledModules":{"type":"array","items":{"type":"string"}},"authorityDisabledModules":{"type":"array","items":{"type":"string"}},
        "startsAt":{"type":"string"},"endsAt":{"type":"string"},"oaAssignedOnly":{"type":"boolean"}}});
    doc["components"]["schemas"]["ApiUserDto"]["properties"]["permissionSource"] =
        json!({"$ref":"#/components/schemas/PermissionSource"});
    doc["components"]["schemas"]["PermissionSource"]["properties"]["operations"] = json!({"type":"array","items":{"type":"string"},"description":"Business operation and data-scope descriptions for the current identity."});
    doc["components"]["schemas"]["PermissionSource"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("operations"));
    doc["components"]["schemas"]["AccountPersonnelRecord"] = json!({
        "type":"object", "required":["id","employeeNumber","fullName","jobTitle","status"],
        "properties": {
            "id":{"type":"integer","format":"int64"},
            "employeeNumber":{"type":"string"}, "fullName":{"type":"string"},
            "jobTitle":{"type":"string"},
            "status":{"type":"string","enum":["Probation","Active","Departed"]}
        }
    });
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
                json!({"type":["array","null"],"items":{"$ref":"#/components/schemas/ApiPermissionGrantDto"},"maxItems":1000,"default":null,"description":"null inherits the explicitly selected scheme; an array replaces it, including an empty deny-all array."})
            } else if *field == "employee" {
                json!({"anyOf":[{"$ref":"#/components/schemas/AccountPersonnelRecord"},{"type":"null"}],"default":null,"readOnly":true,"description":"Current personnel link; maintained through personnel workflows, never account save input."})
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
                "使用本人日常申请、凭证提交与审批结果查看；付款打印和模板设计按实际职责另行配置，不默认授予审批或私密人事档案权限。"
            );
        }
    }
}
