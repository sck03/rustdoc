//! One unit context for requests, transactions and background work.
mod access;
mod analytics;
pub(super) use analytics::summary as workflow_summary;
mod context;
mod dispatch;
pub(super) use dispatch::dispatch;
mod account_security;
pub(super) use account_security::{clear_grants, revoke_related};
mod foundation;
mod hierarchy;
mod members;
mod restrictions;
mod schemes;
pub(super) use schemes::{directory as scheme_directory, save as save_scheme};
#[path = "units/permissions.rs"]
mod role_permissions;
use super::{
    auth,
    error::{Result, error, invalid, unavailable},
    records::text,
    store::{self, Actor},
};
use crate::engine::store::Connection;
pub(super) use access::{approvers, directory, save};
pub(super) use context::{actors_for_operation, business_actors, contexts, for_operation, resolve};
use export_doc_domain::{
    permissions::{self, Grant},
    units::Management,
};
pub(super) use hierarchy::{companies, preview as preview_company_change, validate_company};
pub(super) use members::members;
pub(super) use role_permissions::invalidate_user;
pub(super) use role_permissions::validate_issuer;
use serde_json::{Value, json};

pub(super) const KIND: &str = "unit-access";
pub(super) fn today(tx: &Connection) -> Result<chrono::NaiveDate> {
    tx.clock.now().map(|t| t.today).map_err(unavailable)
}
pub(super) fn approval_actor(
    tx: &Connection,
    id: i64,
    edition: permissions::ProductEdition,
    unit: &str,
) -> Result<Actor> {
    business_actors(tx, id, edition, unit)?
        .into_iter()
        .find(|actor| {
            permissions::catalog().resources.iter().any(|r| {
                r.workflow_resource
                    && auth::authorize(actor, &r.key, "view").is_ok()
                    && auth::authorize(actor, &r.key, "approve").is_ok()
            })
        })
        .ok_or_else(|| error(403, "账号在此单位没有有效审批身份。"))
}
fn records(tx: &Connection, user: i64) -> Result<Vec<Value>> {
    store::matching(tx, KIND, &[("userId", user.to_string())], &[])
}
pub(super) fn management(actor: &Actor) -> Management {
    Management {
        organization: auth::authorize(actor, "organization.directory", "manage").is_ok(),
        analytics: auth::authorize(actor, "organization.analytics", "manage").is_ok(),
        appointments: auth::authorize(actor, "organization.appointments", "manage").is_ok(),
        workflows: auth::authorize(actor, "organization.workflows", "manage").is_ok(),
        roles: auth::authorize(actor, "organization.roles", "manage").is_ok(),
    }
}
pub(super) fn demand(actor: &Actor, company: &str, capability: &str) -> Result<()> {
    auth::authorize(actor, capability, "manage")?;
    if !actor.admin && actor.company != company {
        return Err(error(403, "单位超出当前管理范围。"));
    }
    Ok(())
}
fn window(row: &Value) -> Result<bool> {
    let now = chrono::Utc::now();
    let parse = |key| -> Result<Option<chrono::DateTime<chrono::Utc>>> {
        let value = text(row, key);
        if value.is_empty() {
            return Ok(None);
        }
        chrono::DateTime::parse_from_rfc3339(&value)
            .map(|v| Some(v.with_timezone(&chrono::Utc)))
            .map_err(|_| unavailable("单位授权有效期损坏。"))
    };
    Ok(row["isActive"] == true
        && parse("startsAt")?.is_none_or(|v| v <= now)
        && parse("endsAt")?.is_none_or(|v| now < v))
}
fn company(tx: &Connection, code: &str) -> Result<Value> {
    tx.find_identity("companies", &store::normalize(code))?
        .filter(|c| c["isActive"] == true && c["code"] == code)
        .ok_or_else(|| error(403, "单位不存在或已停用。"))
}
fn covers(
    row: &Value,
    target: &str,
    tree: &std::collections::BTreeMap<String, Vec<String>>,
) -> bool {
    row["companyCode"] == target
        || row["includeDescendants"] == true
            && tree
                .get(target)
                .is_some_and(|parents| parents.iter().any(|p| row["companyCode"] == *p))
}
fn tree(tx: &Connection) -> Result<std::collections::BTreeMap<String, Vec<String>>> {
    tree_from(&store::all(tx, "companies")?)
}
fn tree_from(units: &[Value]) -> Result<std::collections::BTreeMap<String, Vec<String>>> {
    let rows: Vec<export_doc_domain::units::Unit> = units
        .iter()
        .cloned()
        .map(serde_json::from_value)
        .collect::<std::result::Result<_, _>>()
        .map_err(|_| unavailable("单位目录损坏。"))?;
    export_doc_domain::units::ancestry(&rows).map_err(unavailable)
}
/// Revalidate every company affected by the person's roles, including revoked roles.
pub(super) fn validate_user(tx: &Connection, actor: &Actor, user: &Value) -> Result<()> {
    let id = user["id"]
        .as_i64()
        .ok_or_else(|| unavailable("账号编号缺失。"))?;
    let mut units = std::collections::BTreeSet::from([text(user, "companyScope")]);
    if let Some(person) = super::personnel_accounts::person_for_account(tx, id)? {
        units.extend(
            super::personnel_organization::assignments(&person)?
                .into_iter()
                .map(|a| a.company_code),
        );
    }
    let tree = tree(tx)?;
    for record in records(tx, id)? {
        for unit in tree.keys().filter(|unit| covers(&record, unit, &tree)) {
            units.insert(unit.clone());
        }
    }
    for unit in units {
        super::oa::approval_references::validate_account_in(tx, actor, user, &unit)?;
        super::handling::validate_account_in(tx, actor, user, &unit)?;
    }
    Ok(())
}
