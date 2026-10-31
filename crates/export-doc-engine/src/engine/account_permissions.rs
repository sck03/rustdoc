//! Resolve one permission source; account-specific grants never accumulate role grants.
use super::error::{Result, invalid, unavailable};
use crate::engine::store::Connection;
use export_doc_domain::permissions::{self, Grant};
use serde_json::{Value, json};

pub(super) fn validate_references(
    connection: &Connection,
    actor: &super::store::Actor,
    user: &Value,
) -> Result<()> {
    super::units::validate_user(connection, actor, user)
}

pub fn template(connection: &Connection, user: &Value) -> Result<Option<Value>> {
    if user["role"] == "Admin" || !user["permissionGrants"].is_null() {
        return Ok(None);
    }
    let id = user["permissionTemplateId"]
        .as_i64()
        .filter(|id| *id > 0)
        .ok_or_else(|| unavailable("身份缺少明确的权限方案或独立权限配置。"))?;
    let version = user["permissionSchemeVersion"]
        .as_i64()
        .filter(|v| *v > 0)
        .ok_or_else(|| unavailable("身份缺少明确的已发布权限版本。"))?;
    Ok(Some(
        connection
            .permission_scheme_version(id, version)?
            .ok_or_else(|| unavailable("引用的权限方案发布版本不存在。"))?,
    ))
}

#[derive(Clone)]
pub(super) struct Profile {
    pub direct: Vec<Grant>,
    pub disabled: Vec<String>,
    pub assigned_only: bool,
}

/// Account and unit identities resolve exactly one source, then apply restrictions.
pub(super) fn profile(connection: &Connection, identity: &Value) -> Result<Profile> {
    let template = template(connection, identity)?;
    let direct = if let Some(template) = &template {
        if template["isActive"] == true {
            super::permission_templates::direct_grants(template)?
        } else {
            vec![]
        }
    } else {
        serde_json::from_value(identity["permissionGrants"].clone())?
    };
    let mut disabled = modules(identity)?;
    if let Some(template) = &template {
        disabled.extend(modules(template)?);
    }
    Ok(Profile {
        direct,
        disabled,
        assigned_only: identity["oaAssignedOnly"] == true
            || template.is_some_and(|t| t["oaAssignedOnly"] == true),
    })
}

pub fn grants(connection: &Connection, user: &Value) -> Result<Vec<Grant>> {
    if user["role"] == "Admin" {
        return permissions::role_grants("Admin").map_err(unavailable);
    }
    let profile = profile(connection, user)?;
    permissions::profile::resolve(&profile.direct, &profile.disabled).map_err(unavailable)
}

pub(super) fn modules(value: &Value) -> Result<Vec<String>> {
    Ok(serde_json::from_value(
        value.get("disabledModules").cloned().unwrap_or(json!([])),
    )?)
}

pub(super) fn source(
    connection: &Connection,
    actor: &super::store::Actor,
    user: &Value,
) -> Result<Value> {
    let role = actor
        .context
        .strip_prefix("unit:")
        .and_then(|c| c.split_once(':'))
        .map(|(id, _)| id.parse::<i64>().map_err(|_| unavailable("身份编号损坏。")))
        .transpose()?
        .map(|id| super::store::get(connection, super::units::KIND, id))
        .transpose()?;
    let identity = role.as_ref().unwrap_or(user);
    let administrative = actor.admin || identity["mode"] == "Administration";
    let scheme = if administrative {
        None
    } else {
        template(connection, identity)?
    };
    let authority = identity["authorityRoleId"]
        .as_i64()
        .map(|id| super::store::get(connection, super::units::KIND, id))
        .transpose()?;
    let mut authority_modules = authority
        .as_ref()
        .map(modules)
        .transpose()?
        .unwrap_or_default();
    if let Some(authority) = authority {
        authority_modules.extend(modules(&super::store::get(
            connection,
            "users",
            authority["userId"]
                .as_i64()
                .ok_or_else(|| unavailable("授权来源账号缺失。"))?,
        )?)?);
    }
    let names = |keys: Vec<String>| {
        keys.into_iter()
            .map(|key| {
                permissions::catalog()
                    .resources
                    .iter()
                    .find(|r| r.module_key == key)
                    .map(|r| r.name.clone())
                    .unwrap_or(key)
            })
            .collect::<std::collections::BTreeSet<_>>()
    };
    let operations: Vec<_> = actor
        .grants
        .iter()
        .filter_map(|grant| {
            let resource = permissions::resource(&grant.resource_key)?;
            if resource.is_technical && !resource.key.starts_with("organization.") {
                return None;
            }
            let action = resource.actions.iter().find(|a| a.key == grant.action)?;
            let scope = match grant.data_scope.as_str() {
                "own" => "本人",
                "department" => "部门",
                "company" => "单位",
                _ => "全部",
            };
            Some(format!(
                "{} · {} · {}",
                resource.name,
                action.name,
                if resource.supports_data_scope {
                    scope
                } else {
                    &resource.scope_description
                }
            ))
        })
        .collect();
    Ok(
        json!({"name":if actor.admin {"平台管理员"} else if administrative {"单位管理授权"} else {scheme.as_ref().and_then(|s|s["name"].as_str()).unwrap_or("独立配置")},
        "templateId":scheme.as_ref().map(|s|s["id"].clone()),"templateVersion":scheme.as_ref().map(|s|s["versionNumber"].clone()),
        "accountDisabledModules":names(modules(user)?),"identityDisabledModules":names(if role.is_some() {modules(identity)?} else {vec![]}),"schemeDisabledModules":names(scheme.as_ref().map(modules).transpose()?.unwrap_or_default()),"authorityDisabledModules":names(authority_modules),"operations":operations,
        "startsAt":super::records::text(identity,"startsAt"),"endsAt":super::records::text(identity,"endsAt"),"oaAssignedOnly":actor.oa_assigned_only}),
    )
}

pub fn assigned_only(connection: &Connection, user: &Value) -> Result<bool> {
    if user["role"] == "Admin" {
        return Ok(false);
    }
    Ok(user["oaAssignedOnly"] == true
        || template(connection, user)?.is_some_and(|template| template["oaAssignedOnly"] == true))
}

pub fn validate(value: &mut Value) -> Result<()> {
    if value
        .get("oaAssignedOnly")
        .is_some_and(|flag| !flag.is_boolean())
    {
        return Err(invalid("申请可见性限制须为布尔值。"));
    }
    let modules = modules(value).map_err(|_| invalid("关闭模块须为有效模块列表。"))?;
    value["disabledModules"] =
        json!(permissions::profile::disabled_modules(&modules).map_err(invalid)?);
    if !value["permissionGrants"].is_null() {
        let direct: Vec<Grant> = serde_json::from_value(value["permissionGrants"].clone())
            .map_err(|_| invalid("账号权限须为资源、操作和数据范围列表。"))?;
        value["permissionGrants"] =
            json!(permissions::profile::assignable(&direct).map_err(invalid)?);
        value["permissionTemplateId"] = Value::Null;
        value["permissionSchemeVersion"] = Value::Null;
    }
    if value["role"] == "Admin" {
        value["oaAssignedOnly"] = json!(false);
        value["permissionTemplateId"] = Value::Null;
        value["permissionGrants"] = Value::Null;
        value["disabledModules"] = json!([]);
    }
    Ok(())
}
