//! Resolve stable restriction members only within the caller's read/write snapshot.
use super::{
    error::{Result, unavailable},
    store::Connection,
};
use export_doc_domain::access_scope::{AccessRule, ScopeMode};
pub(super) fn rules(tx: &Connection, kind: &str, id: i64) -> Result<Vec<AccessRule>> {
    tx.access_restrictions(kind, id, true)?
        .into_iter()
        .map(|row| {
            let mut rule = AccessRule {
                resource_key: row["resourceKey"].as_str().unwrap_or("").into(),
                action: row["action"].as_str().unwrap_or("").into(),
                mode: serde_json::from_value(row["mode"].clone())
                    .map_err(|_| unavailable("授权限制类型损坏。"))?,
                unit_codes: Default::default(),
                departments: Default::default(),
                project_ids: Default::default(),
            };
            for member in row["members"].as_array().into_iter().flatten() {
                match rule.mode {
                    ScopeMode::Units => {
                        rule.unit_codes.insert(
                            member["unitCode"]
                                .as_str()
                                .ok_or_else(|| unavailable("授权单位引用失效。"))?
                                .into(),
                        );
                    }
                    ScopeMode::Departments => {
                        rule.departments.insert((
                            member["unitCode"]
                                .as_str()
                                .ok_or_else(|| unavailable("授权部门单位引用失效。"))?
                                .into(),
                            member["departmentCode"]
                                .as_str()
                                .ok_or_else(|| unavailable("授权部门引用失效。"))?
                                .into(),
                        ));
                    }
                    ScopeMode::Projects => {
                        rule.project_ids.insert(
                            member["projectId"]
                                .as_str()
                                .ok_or_else(|| unavailable("授权项目引用失效。"))?
                                .into(),
                        );
                    }
                    _ => {}
                }
            }
            Ok(rule)
        })
        .collect()
}
pub(super) fn restrict(actor: &mut super::store::Actor) {
    actor.grants.retain(|grant| {
        !actor.scope_rules.iter().any(|rule| {
            rule.resource_key == grant.resource_key
                && rule.action == grant.action
                && matches!(rule.mode, ScopeMode::Deny)
        })
    });
}
