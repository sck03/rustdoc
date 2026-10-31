//! Authorization vocabulary and dependency expansion from the official contract.
//! This module has no knowledge of a database, HTTP host, or UI framework.
use crate::contracts;
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, sync::OnceLock};
mod edition;
pub mod profile;
pub use edition::ProductEdition;

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
    pub unit_assignable: bool,
    pub workflow_resource: bool,
    pub scope_description: String,
    pub actions: Vec<Action>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Action {
    pub key: String,
    pub name: String,
    pub description: String,
    pub navigation_access_level: String,
    pub unit_assignable: bool,
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
    #[serde(default)]
    data_scope: Option<String>,
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
                let scope = requirement
                    .data_scope
                    .as_deref()
                    .unwrap_or(&source.data_scope);
                let key = (requirement.resource_key.clone(), requirement.action.clone());
                if result
                    .get(&key)
                    .is_none_or(|g| scope_rank(&g.grant.data_scope) < scope_rank(scope))
                {
                    let grant = Grant {
                        resource_key: key.0.clone(),
                        action: key.1.clone(),
                        data_scope: scope.into(),
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
    export_doc_contracts::permission_actions::service_action(key, action, |key| {
        resource.actions.iter().any(|a| a.key == key)
    })
}

/// Shared by application authorization and native action visibility. A missing
/// policy or grant always denies access; path names never imply permissions.
pub fn allows_operation(
    grants: &[Grant],
    administrator: bool,
    operation: crate::generated_api::Operation,
    query: &[(&str, String)],
) -> bool {
    allows_edition_operation(
        grants,
        administrator,
        ProductEdition::Full,
        operation,
        query,
    )
}

pub fn allows_edition_operation(
    grants: &[Grant],
    administrator: bool,
    edition: ProductEdition,
    operation: crate::generated_api::Operation,
    query: &[(&str, String)],
) -> bool {
    let policy = &contracts::contract()["operations"][operation.id]["policy"];
    matches_access(grants, administrator, edition, &policy["access"], query)
}

fn matches_access(
    grants: &[Grant],
    administrator: bool,
    edition: ProductEdition,
    rule: &serde_json::Value,
    query: &[(&str, String)],
) -> bool {
    if rule["bypass"] == true {
        return true;
    }
    if let Some(parameter) = rule["query"].as_str() {
        let selected = query
            .iter()
            .find(|(key, _)| *key == parameter)
            .and_then(|(_, value)| rule["cases"].get(value));
        return matches_access(
            grants,
            administrator,
            edition,
            selected.unwrap_or(&rule["fallback"]),
            query,
        );
    }
    let Some(resources) = rule["resources"]
        .as_array()
        .filter(|resources| !resources.is_empty())
    else {
        return false;
    };
    let allows = |resource: &serde_json::Value| {
        let Some(key) = resource["resourceKey"].as_str() else {
            return false;
        };
        edition.allows(key)
            && (administrator
                || resource["administratorOnly"] != true
                    && resource["actions"].as_array().is_some_and(|actions| {
                        actions.iter().any(|action| {
                            grants.iter().any(|grant| {
                                grant.resource_key == key
                                    && Some(grant.action.as_str()) == action.as_str()
                            })
                        })
                    }))
    };
    match rule["match"].as_str() {
        Some("All") => resources.iter().all(allows),
        Some("Any") => resources.iter().any(allows),
        _ => false,
    }
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
        let grants = effective(&[grant("document.payments", "view", "own")]).unwrap();
        assert!(allows_operation(
            &grants,
            false,
            crate::generated_api::LIST_CUSTOM_OPTIONS,
            &[]
        ));
        assert!(!grants.contains(&grant("document.payments", "operate", "own")));
        let grants = effective(&[grant("document.invoices", "manage", "own")]).unwrap();
        assert!(!grants.contains(&grant("document.invoices", "view", "own")));
        assert!(!grants.contains(&grant("document.invoices", "operate", "own")));
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

    #[test]
    fn voucher_output_follows_explicit_or_job_duties() {
        use crate::generated_api::*;
        let employee = role_grants("OfficeEmployee").unwrap();
        for operation in [
            CREATE_PAYMENT,
            START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB,
            LIST_INVOICES,
        ] {
            assert!(!allows_operation(&employee, false, operation, &[]));
        }
        for role in [
            "Sales",
            "SalesManager",
            "OfficeManager",
            "PersonnelManager",
            "Finance",
        ] {
            let grants = role_grants(role).unwrap();
            assert!(
                allows_operation(&grants, false, CREATE_PAYMENT, &[]),
                "{role}"
            );
            assert!(
                allows_operation(&grants, false, START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB, &[]),
                "{role}"
            );
            assert!(allows_operation(&grants, false, LIST_JOBS, &[]), "{role}");
            assert!(
                !allows_operation(&grants, false, LIST_INVOICES, &[]),
                "{role}"
            );
        }
        let grants =
            effective(&[grant("document.payment-output", "export-pdf", "company")]).unwrap();
        assert!(grants.contains(&grant("document.jobs", "view", "own")));
        assert!(
            !grants
                .iter()
                .any(|g| g.resource_key == "document.report-templates")
        );
        assert!(!allows_operation(&grants, false, SAVE_CUSTOM_OPTION, &[]));
        assert!(!allows_operation(&grants, false, CREATE_PAYMENT, &[]));
        assert!(allows_operation(&grants, false, LIST_REPORT_TEMPLATES, &[]));
    }
}
