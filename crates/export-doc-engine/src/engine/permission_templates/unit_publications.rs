//! Read-only unit publication access shares the directory's ownership and grant ceiling.
use super::*;
use crate::engine::{
    error::{error, unavailable},
    store::{self, Actor},
    units,
};

pub(in crate::engine) fn ceiling(tx: &Connection, actor: &Actor) -> Result<Value> {
    if actor.admin {
        return Ok(Value::Null);
    }
    let id = actor
        .context
        .strip_prefix("unit:")
        .and_then(|v| v.split_once(':'))
        .and_then(|(id, _)| id.parse::<i64>().ok())
        .ok_or_else(|| error(403, "单位管理身份无效。"))?;
    let identity = store::get(tx, "unit-access", id)?;
    identity["permissionGrants"]
        .as_array()
        .map(|grants| json!(grants))
        .ok_or_else(|| unavailable("单位管理身份缺少独立授权上限。"))
}

pub(in crate::engine) fn eligible(value: &Value, ceiling: &Value) -> Result<bool> {
    let grants = direct_grants(value)?;
    if export_doc_domain::units::validate_grants(&grants).is_err() {
        return Ok(false);
    }
    if !ceiling.is_null() {
        let ceiling: Vec<Grant> = serde_json::from_value(ceiling.clone())?;
        if !export_doc_domain::units::within_ceiling(&grants, &ceiling) {
            return Ok(false);
        }
    }
    Ok(true)
}

pub(super) fn read(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    version: Option<i64>,
    page: i64,
    size: i64,
) -> Result<Value> {
    units::demand(actor, &actor.company, "organization.roles")?;
    let company = units::company(tx, &actor.company)?;
    let draft = store::get(tx, "permission-templates", id)?;
    validate_owner(&draft, &company)?;
    if draft["isActive"] != true {
        return Err(error(403, "权限方案已停用。"));
    }
    let ceiling = ceiling(tx, actor)?;
    if let Some(version) = version {
        let value = tx
            .permission_scheme_version(id, version)?
            .ok_or_else(|| error(404, "发布版本不存在。"))?;
        validate_owner(&value, &company)?;
        if !eligible(&value, &ceiling)? {
            return Err(error(403, "发布版本超出当前单位授权上限。"));
        }
        return project(value);
    }
    let mut available = vec![];
    for value in tx.unit_permission_publications(
        company["id"]
            .as_i64()
            .ok_or_else(|| unavailable("单位编号缺失。"))?,
        Some(id),
    )? {
        if validate_owner(&value, &company).is_ok() && eligible(&value, &ceiling)? {
            available.push(json!({"schemeId":id.to_string(),"version":value["publishedVersion"],"publishedAt":value["publishedAt"]}));
        }
    }
    if available.is_empty() {
        return Err(error(403, "没有可供本单位使用的发布版本。"));
    }
    let total = available.len();
    let items: Vec<_> = available
        .into_iter()
        .skip(page.saturating_sub(1).saturating_mul(size) as usize)
        .take(size as usize)
        .collect();
    Ok(json!({"items":items,"totalCount":total,"pageNumber":page,"pageSize":size}))
}
