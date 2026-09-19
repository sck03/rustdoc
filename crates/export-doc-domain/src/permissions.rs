//! Authorization vocabulary and dependency expansion from the official contract.
//! This module has no knowledge of a database, HTTP host, or UI framework.
use crate::contracts;
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, sync::OnceLock};

#[derive(Clone, Debug, PartialEq, Eq, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Grant {
    pub resource_key: String,
    pub action: String,
    pub data_scope: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Resource {
    pub key: String,
    pub name: String,
    pub group: String,
    pub workspace: String,
    pub is_technical: bool,
    pub module_key: String,
    pub supports_data_scope: bool,
    pub actions: Vec<Action>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Action {
    pub key: String,
    pub name: String,
    pub description: String,
    pub navigation_access_level: String,
}

#[derive(Deserialize)]
pub struct Role {
    pub code: String,
    pub name: String,
    pub grants: Vec<Grant>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Dependency {
    resource_key: String,
    action: String,
    grants: Vec<Requirement>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Requirement {
    resource_key: String,
    action: String,
}

#[derive(Deserialize)]
pub struct Catalog {
    pub resources: Vec<Resource>,
    pub roles: Vec<Role>,
    dependencies: Vec<Dependency>,
    pub editions: BTreeMap<String, Vec<String>>,
}

pub fn catalog() -> &'static Catalog {
    static CATALOG: OnceLock<Catalog> = OnceLock::new();
    CATALOG.get_or_init(|| {
        serde_json::from_value(contracts::contract()["permissions"].clone())
            .expect("generated permission catalog")
    })
}

pub fn resource(key: &str) -> Option<&'static Resource> {
    catalog()
        .resources
        .iter()
        .find(|resource| resource.key.eq_ignore_ascii_case(key))
}

pub fn scope_rank(scope: &str) -> u8 {
    match scope {
        "own" => 1,
        "department" => 2,
        "company" => 3,
        "all" => 4,
        _ => 0,
    }
}

pub fn normalize(grants: &[Grant]) -> Result<Vec<Grant>, String> {
    if grants.len() > 1000 {
        return Err("权限条目超出上限。".into());
    }
    let mut normalized = BTreeMap::new();
    for grant in grants {
        let resource = resource(grant.resource_key.trim()).ok_or("未知权限资源。")?;
        let action = resource
            .actions
            .iter()
            .find(|a| a.key.eq_ignore_ascii_case(grant.action.trim()))
            .ok_or("该资源不包含请求的权限动作。")?;
        let scope = grant.data_scope.trim().to_ascii_lowercase();
        if scope_rank(&scope) == 0 {
            return Err("未知权限数据范围。".into());
        }
        let grant = Grant {
            resource_key: resource.key.clone(),
            action: action.key.clone(),
            data_scope: if resource.supports_data_scope {
                scope
            } else {
                "all".into()
            },
        };
        let key = (grant.resource_key.clone(), grant.action.clone());
        if normalized.insert(key, grant).is_some() {
            return Err("同一资源动作不能重复授权。".into());
        }
    }
    Ok(normalized.into_values().collect())
}

pub fn effective(grants: &[Grant]) -> Result<Vec<Grant>, String> {
    Ok(effective_details(grants)?
        .into_iter()
        .map(|grant| grant.grant)
        .collect())
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EffectiveGrant {
    #[serde(flatten)]
    pub grant: Grant,
    pub source: String,
    pub source_resource_key: String,
}

pub fn effective_details(grants: &[Grant]) -> Result<Vec<EffectiveGrant>, String> {
    let normalized = normalize(grants)?;
    let mut result: BTreeMap<(String, String), EffectiveGrant> = normalized
        .into_iter()
        .map(|g| {
            (
                (g.resource_key.clone(), g.action.clone()),
                EffectiveGrant {
                    source: "direct".into(),
                    source_resource_key: g.resource_key.clone(),
                    grant: g,
                },
            )
        })
        .collect();
    let mut pending: Vec<_> = result.values().map(|value| value.grant.clone()).collect();
    while let Some(source) = pending.pop() {
        for dependency in catalog()
            .dependencies
            .iter()
            .filter(|d| d.resource_key == source.resource_key && d.action == source.action)
        {
            for requirement in &dependency.grants {
                let key = (requirement.resource_key.clone(), requirement.action.clone());
                if result.get(&key).is_none_or(|g| {
                    scope_rank(&g.grant.data_scope) < scope_rank(&source.data_scope)
                }) {
                    let grant = Grant {
                        resource_key: key.0.clone(),
                        action: key.1.clone(),
                        data_scope: source.data_scope.clone(),
                    };
                    result.insert(
                        key,
                        EffectiveGrant {
                            grant: grant.clone(),
                            source: "dependency".into(),
                            source_resource_key: source.resource_key.clone(),
                        },
                    );
                    pending.push(grant);
                }
            }
        }
    }
    Ok(result.into_values().collect())
}

pub fn role_grants(role: &str) -> Result<Vec<Grant>, String> {
    let role = catalog()
        .roles
        .iter()
        .find(|r| r.code == role)
        .ok_or("未知账号角色。")?;
    effective(&role.grants)
}

pub fn module_access(grants: &[Grant]) -> BTreeMap<String, String> {
    fn rank(level: &str) -> u8 {
        match level {
            "manage" => 3,
            "operate" => 2,
            "view" => 1,
            _ => 0,
        }
    }
    let mut modules: BTreeMap<String, String> = BTreeMap::new();
    for grant in grants {
        if let Some(resource) = resource(&grant.resource_key) {
            if let Some(action) = resource.actions.iter().find(|a| a.key == grant.action) {
                let level = modules
                    .entry(resource.module_key.clone())
                    .or_insert_with(|| "none".into());
                if rank(&action.navigation_access_level) > rank(level) {
                    *level = action.navigation_access_level.clone();
                }
            }
        }
    }
    modules
}

/// CRUD services express intent; standard resources use operate/manage, while
/// resources with separate actions retain their exact create/edit/delete grant.
pub fn service_action<'a>(key: &str, action: &'a str) -> &'a str {
    let Some(resource) = resource(key) else {
        return action;
    };
    if resource.actions.iter().any(|a| a.key == action) {
        return action;
    }
    let standard = resource.actions.iter().any(|a| a.key == "operate")
        && resource.actions.iter().any(|a| a.key == "manage");
    if standard {
        match action {
            "create" | "edit" => "operate",
            "delete" => "manage",
            _ => action,
        }
    } else {
        action
    }
}

/// Shared by application authorization and native action visibility. A missing
/// policy or grant always denies access; path names never imply permissions.
pub fn allows_operation(
    grants: &[Grant],
    administrator: bool,
    operation: crate::generated_api::Operation,
    query: &[(&str, String)],
) -> bool {
    let policy = &contracts::contract()["operations"][operation.id]["policy"];
    if !policy.is_object() {
        return false;
    }
    let allows = |key: &str, action: &str| {
        !key.is_empty()
            && !action.is_empty()
            && (administrator
                || (!key.starts_with("system.")
                    && grants.iter().any(|grant| {
                        grant.resource_key == key && grant.action == service_action(key, action)
                    })))
    };
    if let Some(requirements) = policy["requirements"]
        .as_array()
        .filter(|items| !items.is_empty())
    {
        return requirements.iter().all(|requirement| {
            allows(
                requirement["resourceKey"].as_str().unwrap_or(""),
                requirement["action"].as_str().unwrap_or(""),
            )
        });
    }
    if policy["permissionBypass"] == true {
        return true;
    }
    let metadata = &policy["permissions"];
    if !metadata.is_object() {
        return false;
    }
    let read = operation.method == "GET";
    let mut module = if read {
        metadata["readModule"].as_str()
    } else {
        metadata["writeModule"]
            .as_str()
            .or(metadata["readModule"].as_str())
    }
    .unwrap_or("");
    if read && metadata["selector"] == 1 {
        module = match query
            .iter()
            .find(|(key, _)| *key == "reportType")
            .map(|(_, value)| value.as_str())
        {
            Some("PaymentVoucher") => "document.payment-reports",
            Some("ExportDocument") => "document.invoice-reports",
            _ => module,
        };
    }
    if module.is_empty() {
        return false;
    }
    let level = if read {
        metadata["readAccessLevel"].as_str().unwrap_or("view")
    } else {
        metadata["writeAccessLevel"]
            .as_str()
            .unwrap_or(if operation.method == "DELETE" {
                "manage"
            } else {
                "operate"
            })
    };
    if resource(module).is_some() {
        return allows(module, level);
    }
    let rank = |value: &str| match value {
        "view" => 1,
        "operate" => 2,
        "manage" => 3,
        _ => 0,
    };
    rank(level) > 0
        && (administrator
            || module_access(grants)
                .get(module)
                .is_some_and(|actual| rank(actual) >= rank(level)))
}

#[cfg(test)]
mod tests {
    use super::*;
    fn grant(resource: &str, action: &str, scope: &str) -> Grant {
        Grant {
            resource_key: resource.into(),
            action: action.into(),
            data_scope: scope.into(),
        }
    }
    #[test]
    fn operation_visibility_uses_the_same_generated_policy_as_the_service() {
        use crate::generated_api::*;
        let grants = effective(&[grant("office.people", "view", "company")]).unwrap();
        assert!(allows_operation(&grants, false, LIST_PERSONNEL, &[]));
        assert!(!allows_operation(&grants, false, GET_PERSONNEL, &[]));
        assert!(!allows_operation(&grants, false, UPDATE_SETTINGS, &[]));
        assert!(allows_operation(&[], true, UPDATE_SETTINGS, &[]));
        assert!(!allows_operation(
            &[],
            true,
            Operation {
                id: "Unknown",
                method: "GET",
                path: "/api/invoices",
                requires_authentication: true
            },
            &[]
        ));
    }
    #[test]
    fn dependencies_keep_the_source_scope_and_do_not_add_write_rights() {
        let grants = effective(&[grant("sales.contacts", "create", "department")]).unwrap();
        assert!(grants.contains(&grant("sales.customers", "view", "department")));
        assert!(
            !grants
                .iter()
                .any(|g| g.resource_key == "sales.customers" && g.action == "edit")
        );
        assert_eq!(module_access(&grants)["sales.crm"], "operate");
    }
    #[test]
    fn invalid_actions_and_scopes_are_rejected_and_empty_grants_stay_empty() {
        assert!(normalize(&[grant("sales.contacts", "manage", "all")]).is_err());
        assert!(normalize(&[grant("sales.contacts", "view", "everyone")]).is_err());
        assert!(effective(&[]).unwrap().is_empty());
        assert!(role_grants("unknown").is_err());
        assert!(
            !role_grants("Sales")
                .unwrap()
                .iter()
                .any(|g| g.resource_key == "system.users")
        );
    }
}
