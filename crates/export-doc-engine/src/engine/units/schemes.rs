//! Unit-owned schemes share validation and resolution with platform schemes.
use super::super::{permission_templates, store::Store};
use super::*;

pub(in crate::engine) fn directory(
    store: &Store,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        let code = query
            .iter()
            .find(|(key, _)| *key == "companyCode")
            .map(|(_, v)| v.as_str())
            .unwrap_or(&actor.company);
        demand(&actor, code, "organization.roles")?;
        let unit = company(tx, code)?;
        let items = store::matching(
            tx,
            "permission-templates",
            &[("ownerUnitId", unit["id"].to_string())],
            &[],
        )?
        .into_iter()
        .map(|v| permission_templates::project_with_usage(tx, v))
        .collect::<Result<Vec<_>>>()?;
        Ok(json!({"items":items,"unitId":unit["id"]}))
    })
}

pub(in crate::engine) fn save(store: &Store, actor: &Actor, input: Value) -> Result<Value> {
    store.transaction_as(actor, |tx, actor| {
        let unit_id = super::super::records::positive(&input, "unitId", "方案所属单位")?;
        let unit = store::get(tx, "companies", unit_id)?;
        let code = text(&unit, "code");
        demand(actor, &code, "organization.roles")?;
        company(tx, &code)?;
        let mut value = crate::contracts::dto(
            crate::contracts::schema("ApiPermissionTemplateSaveRequest"),
            input,
        );
        let id = value["id"].as_i64().unwrap_or(0);
        let previous = if id > 0 {
            store::get(tx, "permission-templates", id)?
        } else {
            json!({})
        };
        if id > 0 {
            if previous["ownerUnitId"] != unit_id {
                return Err(error(403, "只能维护当前单位拥有的权限方案。"));
            }
            store::check_version(&previous, store::expected(&value))?;
        }
        permission_templates::validate(id, &previous, &mut value)?;
        let grants = export_doc_domain::units::validate_grants(
            &permission_templates::direct_grants(&value)?,
        )
        .map_err(invalid)?;
        if !actor.admin {
            let role_id = actor
                .context
                .strip_prefix("unit:")
                .and_then(|v| v.split_once(':'))
                .and_then(|(id, _)| id.parse::<i64>().ok())
                .ok_or_else(|| error(403, "需要单位管理身份。"))?;
            let authority = store::get(tx, KIND, role_id)?;
            let ceiling = super::super::account_permissions::profile(tx, &authority)?;
            if !export_doc_domain::units::within_ceiling(&grants, &ceiling.direct) {
                return Err(error(403, "方案权限超过可下放上限。"));
            }
            let mut disabled = super::super::account_permissions::modules(&value)?;
            disabled.extend(ceiling.disabled);
            disabled.extend(super::super::account_permissions::modules(&store::get(
                tx, "users", actor.id,
            )?)?);
            value["disabledModules"] =
                json!(permissions::profile::disabled_modules(&disabled).map_err(invalid)?);
            value["oaAssignedOnly"] =
                json!(value["oaAssignedOnly"] == true || ceiling.assigned_only);
        }
        value["ownerUnitId"] = json!(unit_id);
        value["companyScope"] = json!(code);
        value["ownerUserId"] = json!(actor.id);
        let scope = value.clone();
        let identity = format!("unit:{unit_id}:{}", text(&value, "code"));
        let saved = store::save_in_scope(
            tx,
            "permission-templates",
            id,
            value,
            Some(identity),
            actor,
            "unit-scheme",
            Some(&scope),
        )?;
        validate_scheme_users(
            tx,
            actor,
            saved["id"]
                .as_i64()
                .ok_or_else(|| unavailable("方案编号缺失。"))?,
        )?;
        permission_templates::project_with_usage(tx, saved)
    })
}
