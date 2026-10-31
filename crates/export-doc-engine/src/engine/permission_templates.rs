//! Permission template invariants and projections shared by both frontends.
use super::store::{self, Connection};
use super::{
    error::{Result, conflict, invalid},
    records::{required, text},
};
use export_doc_domain::permissions::{self, Grant};
use serde_json::{Value, json};
pub(super) mod publications;

pub(super) fn project_with_usage(tx: &Connection, value: Value) -> Result<Value> {
    let mut value = project(value)?;
    let id = value["id"]
        .as_i64()
        .ok_or_else(|| invalid("方案编号缺失。"))?
        .to_string();
    let count = |kind| -> Result<usize> {
        Ok(store::matching(
            tx,
            kind,
            &[("permissionTemplateId", id.clone())],
            &[("permissionGrants", "permissionGrants")],
        )?
        .iter()
        .filter(|r| r["permissionGrants"].is_null())
        .count())
    };
    value["accountReferenceCount"] = json!(count("users")?);
    value["unitRoleReferenceCount"] = json!(count(super::units::KIND)?);
    value["publishedVersion"] = tx.permission_scheme_versions(
        id.parse().map_err(|_| invalid("方案编号无效。"))?,
        0,
        1,
    )?["items"]
        .as_array()
        .and_then(|items| items.first())
        .map(|row| row["version"].clone())
        .unwrap_or(Value::Null);
    Ok(value)
}

pub(super) fn validate_owner(value: &Value, company: &Value) -> Result<()> {
    if let Some(unit) = value["ownerUnitId"].as_i64() {
        if company["id"] != unit {
            return Err(super::error::error(403, "单位方案只能分配给本单位身份。"));
        }
    }
    Ok(())
}

pub fn project(mut value: Value) -> Result<Value> {
    let grants = direct_grants(&value)?;
    value["grants"] = json!(grants);
    let disabled: Vec<String> =
        serde_json::from_value(value.get("disabledModules").cloned().unwrap_or(json!([])))?;
    value["effectiveGrants"] = if value["isActive"] == true {
        json!(permissions::profile::resolve_details(&grants, &disabled).map_err(invalid)?)
    } else {
        json!([])
    };
    Ok(value)
}

/// Every saved scheme is explicit, including the initial system presets.
pub(super) fn direct_grants(value: &Value) -> Result<Vec<Grant>> {
    serde_json::from_value(value["grants"].clone()).map_err(Into::into)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn saved_schemes_never_gain_permissions_from_their_name_or_version() {
        let mut seed =
            json!({"isSystem":true,"code":"OfficeEmployee","versionNumber":1,"grants":[]});
        assert!(direct_grants(&seed).unwrap().is_empty());
        seed["versionNumber"] = json!(2);
        assert!(direct_grants(&seed).unwrap().is_empty());
        seed["versionNumber"] = json!(1);
        seed["isSystem"] = json!(false);
        assert!(direct_grants(&seed).unwrap().is_empty());
    }
}

pub fn validate(id: i64, previous: &Value, value: &mut Value) -> Result<()> {
    required(value, "code", "方案代码", 50)?;
    required(value, "name", "方案名称", 120)?;
    if text(value, "description").chars().count() > 1000 {
        return Err(invalid("权限方案说明不能超过 1000 字。"));
    }
    if !text(value, "code")
        .chars()
        .all(|ch| ch.is_alphanumeric() || matches!(ch, '-' | '_' | '.'))
    {
        return Err(invalid("方案代码只能使用字母、数字、点、横线和下划线。"));
    }
    if id == 0 && value["expectedVersion"].as_i64().unwrap_or(0) != 0 {
        return Err(conflict("新建权限方案不能携带旧版本号。"));
    }
    let system = id > 0 && previous["isSystem"] == true;
    if system && text(previous, "code").eq_ignore_ascii_case("Admin") {
        return Err(conflict("系统管理员权限方案不可修改，请复制后新建方案。"));
    }
    value["isSystem"] = json!(system);
    if system {
        value["code"] = previous["code"].clone();
        value["isActive"] = json!(true);
    }
    let grants: Vec<Grant> = serde_json::from_value(value["grants"].clone())
        .map_err(|_| invalid("权限方案须包含资源、动作和数据范围。"))?;
    let grants = permissions::profile::assignable(&grants).map_err(invalid)?;
    super::account_permissions::validate(value)?;
    value["grants"] = serde_json::to_value(grants)?;
    // Derived permission explanations are recomputed at read time.
    value["effectiveGrants"] = json!([]);
    Ok(())
}
