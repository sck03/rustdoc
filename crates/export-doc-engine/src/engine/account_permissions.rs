//! Resolve one permission source; account-specific grants never accumulate role grants.
use super::{
    error::{Result, invalid, unavailable},
    store,
};
use export_doc_domain::permissions::{self, Grant};
use export_doc_storage::Connection;
use serde_json::{Value, json};

pub fn template(connection: &Connection, user: &Value) -> Result<Option<Value>> {
    if user["role"] == "Admin" || !user["permissionGrants"].is_null() {
        return Ok(None);
    }
    Ok(
        match user["permissionTemplateId"].as_i64().filter(|id| *id > 0) {
            Some(id) => connection.get("permission-templates", id)?,
            None => connection
                .find_identity(
                    "permission-templates",
                    &store::normalize(user["role"].as_str().unwrap_or("")),
                )?
                .filter(|group| group["isSystem"] == true),
        },
    )
}

pub fn grants(connection: &Connection, user: &Value) -> Result<Vec<Grant>> {
    if user["role"] == "Admin" {
        return permissions::role_grants("Admin").map_err(unavailable);
    }
    let group = template(connection, user)?;
    let direct = if !user["permissionGrants"].is_null() {
        serde_json::from_value(user["permissionGrants"].clone())?
    } else if let Some(group) = &group {
        if group["isActive"] != true {
            return Ok(vec![]);
        }
        super::permission_templates::direct_grants(group)?
    } else if user["permissionTemplateId"]
        .as_i64()
        .is_some_and(|id| id > 0)
    {
        return Ok(vec![]);
    } else {
        permissions::catalog()
            .roles
            .iter()
            .find(|r| user["role"] == r.code)
            .ok_or_else(|| unavailable("未知账号角色。"))?
            .grants
            .clone()
    };
    let mut disabled = modules(user)?;
    if let Some(group) = &group {
        disabled.extend(modules(group)?);
    }
    permissions::profile::resolve(&direct, &disabled).map_err(unavailable)
}

fn modules(value: &Value) -> Result<Vec<String>> {
    Ok(serde_json::from_value(
        value.get("disabledModules").cloned().unwrap_or(json!([])),
    )?)
}

pub fn validate(value: &mut Value) -> Result<()> {
    let modules = modules(value).map_err(|_| invalid("关闭模块须为有效模块列表。"))?;
    value["disabledModules"] =
        json!(permissions::profile::disabled_modules(&modules).map_err(invalid)?);
    if !value["permissionGrants"].is_null() {
        let direct: Vec<Grant> = serde_json::from_value(value["permissionGrants"].clone())
            .map_err(|_| invalid("账号权限须为资源、操作和数据范围列表。"))?;
        value["permissionGrants"] =
            json!(permissions::profile::assignable(&direct).map_err(invalid)?);
    }
    if value["role"] == "Admin" {
        value["permissionTemplateId"] = Value::Null;
        value["permissionGrants"] = Value::Null;
        value["disabledModules"] = json!([]);
    }
    Ok(())
}
