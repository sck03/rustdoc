//! Unit assignability is independent from record data scopes.
use serde_json::{Value, json};
pub(super) fn extend(doc: &mut Value) {
    let workflows = doc["paths"]
        .as_object()
        .unwrap()
        .values()
        .flat_map(|methods| methods.as_object().unwrap().values())
        .filter(|operation| {
            matches!(
                operation["x-exportdoc-office"]["action"].as_str(),
                Some("submit" | "resource-submit")
            )
        })
        .filter_map(|operation| {
            operation["x-exportdoc-office"]["resource"]
                .as_str()
                .map(str::to_owned)
        })
        .collect::<std::collections::HashSet<_>>();
    let properties =
        &mut doc["components"]["schemas"]["ApiPermissionResourceDefinitionDto"]["properties"];
    properties["unitAssignable"] = json!({"type":"boolean","readOnly":true});
    properties["scopeDescription"] = json!({"type":"string","readOnly":true});
    properties["workflowResource"] = json!({"type":"boolean","readOnly":true});
    doc["components"]["schemas"]["ApiPermissionActionDefinitionDto"]["properties"]["unitAssignable"] =
        json!({"type":"boolean","readOnly":true});
    for resource in doc["x-exportdoc-permissions"]["resources"]
        .as_array_mut()
        .unwrap()
    {
        let key = resource["key"].as_str().unwrap();
        let information = key == "system.about";
        let business = resource["isTechnical"] == false
            && matches!(
                resource["workspace"].as_str(),
                Some("document" | "sales" | "office" | "common")
            )
            && !key.starts_with("system.")
            && !key.starts_with("organization.");
        let description = match key {
            "system.about" => "仅查看系统信息，不授予系统维护能力",
            "office.announcements" => "按当前单位与公告受众限制",
            "office.notifications" => "按当前单位与通知收件人限制",
            _ if resource["supportsDataScope"] == true => "按已授权的记录数据范围限制",
            _ if business => "按当前单位及业务对象规则限制",
            _ => "功能授权，无记录数据范围",
        };
        let workflow = workflows.contains(key);
        resource["unitAssignable"] = json!(business || information);
        resource["scopeDescription"] = json!(description);
        resource["workflowResource"] = json!(workflow);
        if information {
            resource["supportsDataScope"] = json!(false);
        }
        for action in resource["actions"].as_array_mut().unwrap() {
            action["unitAssignable"] = json!(business || information && action["key"] == "view");
        }
    }
}

#[cfg(test)]
pub(super) fn compare(doc: &mut Value) {
    for (schema, fields) in [
        (
            "ApiPermissionResourceDefinitionDto",
            &[
                ("unitAssignable", "boolean"),
                ("scopeDescription", "string"),
                ("workflowResource", "boolean"),
            ][..],
        ),
        (
            "ApiPermissionActionDefinitionDto",
            &[("unitAssignable", "boolean")][..],
        ),
    ] {
        let properties = doc["components"]["schemas"][schema]["properties"]
            .as_object_mut()
            .unwrap();
        for (field, kind) in fields {
            assert_eq!(
                properties.remove(*field).expect("unit permission metadata"),
                json!({"type":kind,"readOnly":true})
            );
        }
    }
}
