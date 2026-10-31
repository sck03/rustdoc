//! Resolve legacy module annotations once into explicit resource/action rules.
//! The annotations remain frozen evidence; runtime authorization consumes access.
use serde_json::{Value, json};
fn rank(level: &str) -> u8 {
    match level {
        "view" => 1,
        "operate" => 2,
        "manage" => 3,
        _ => 0,
    }
}
fn action(resource: &Value, requested: &str) -> String {
    let actions = resource["actions"].as_array().unwrap();
    crate::permission_actions::service_action(resource["key"].as_str().unwrap(), requested, |key| {
        actions.iter().any(|a| a["key"] == key)
    })
    .into()
}
fn resource_rule(resource: &str, actions: Vec<String>, administrator_only: bool) -> Value {
    json!({"resourceKey":resource,"actions":actions,"administratorOnly":administrator_only})
}
fn module_rule(catalog: &[Value], module: &str, level: &str) -> Value {
    if let Some(resource) = catalog.iter().find(|r| r["key"] == module) {
        return json!({"match":"All","resources":[resource_rule(module,vec![action(resource,level)],module.starts_with("system."))]});
    }
    let resources = if rank(level) > 0 {
        catalog
            .iter()
            .filter(|r| r["moduleKey"] == module)
            .map(|r| {
                let actions = r["actions"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .filter(|a| {
                        rank(a["navigationAccessLevel"].as_str().unwrap_or("")) >= rank(level)
                    })
                    .filter_map(|a| a["key"].as_str().map(str::to_owned))
                    .collect();
                resource_rule(r["key"].as_str().unwrap(), actions, false)
            })
            .collect::<Vec<_>>()
    } else {
        vec![]
    };
    json!({"match":"Any","resources":resources})
}
pub(super) fn rule(catalog: &[Value], method: &str, policy: &Value) -> Value {
    if let Some(requirements) = policy["requirements"].as_array().filter(|r| !r.is_empty()) {
        let resources = requirements
            .iter()
            .map(|r| {
                let key = r["resourceKey"].as_str().unwrap_or("");
                let requested = r["action"].as_str().unwrap_or("");
                let action = catalog
                    .iter()
                    .find(|r| r["key"] == key)
                    .map(|r| action(r, requested))
                    .unwrap_or_else(|| requested.into());
                resource_rule(key, vec![action], key.starts_with("system."))
            })
            .collect::<Vec<_>>();
        return json!({"match":"All","resources":resources});
    }
    if policy["permissionBypass"] == true {
        return json!({"bypass":true});
    }
    let metadata = &policy["permissions"];
    let read = method == "get";
    let module = if read {
        metadata["readModule"].as_str()
    } else {
        metadata["writeModule"]
            .as_str()
            .or(metadata["readModule"].as_str())
    }
    .unwrap_or("");
    let level = if read {
        metadata["readAccessLevel"].as_str().unwrap_or("view")
    } else {
        metadata["writeAccessLevel"]
            .as_str()
            .unwrap_or(if method == "delete" {
                "manage"
            } else {
                "operate"
            })
    };
    let fallback = module_rule(catalog, module, level);
    if read && metadata["selector"] == 1 {
        return json!({"query":"reportType","cases":{"PaymentVoucher":module_rule(catalog,"document.payment-reports",level),"ExportDocument":module_rule(catalog,"document.invoice-reports",level)},"fallback":fallback});
    }
    fallback
}
pub(super) fn extend(doc: &mut Value) {
    let catalog = doc["x-exportdoc-permissions"]["resources"]
        .as_array()
        .unwrap()
        .clone();
    for methods in doc["paths"].as_object_mut().unwrap().values_mut() {
        for (method, operation) in methods.as_object_mut().unwrap() {
            let policy = &mut operation["x-exportdoc-policy"];
            policy["access"] = rule(&catalog, method, policy);
        }
    }
}
#[cfg(test)]
pub(super) fn compare(current: &mut Value) {
    let catalog = current["x-exportdoc-permissions"]["resources"]
        .as_array()
        .unwrap()
        .clone();
    for methods in current["paths"].as_object_mut().unwrap().values_mut() {
        for (method, operation) in methods.as_object_mut().unwrap() {
            let policy = &mut operation["x-exportdoc-policy"];
            let access = policy
                .as_object_mut()
                .unwrap()
                .remove("access")
                .expect("compiled access rule");
            assert_eq!(access, rule(&catalog, method, policy));
        }
    }
}
