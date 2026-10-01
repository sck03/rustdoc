//! Permission template invariants and projections shared by both frontends.
use super::{
    error::{Result, conflict, invalid},
    records::{required, text},
};
use export_doc_domain::permissions::{self, Grant};
use serde_json::{Value, json};

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

/// Version one is the untouched system seed. Saving a scheme pins the explicit
/// choices at version two or later; independent and custom schemes never inherit.
pub(super) fn direct_grants(value: &Value) -> Result<Vec<Grant>> {
    if value["isSystem"] == true && value["versionNumber"] == 1 {
        if let Some(role) = permissions::catalog()
            .roles
            .iter()
            .find(|r| value["code"] == r.code)
        {
            return Ok(role.grants.clone());
        }
    }
    serde_json::from_value(value["grants"].clone()).map_err(Into::into)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_untouched_system_defaults_follow_the_current_role() {
        let mut seed =
            json!({"isSystem":true,"code":"OfficeEmployee","versionNumber":1,"grants":[]});
        assert!(
            direct_grants(&seed)
                .unwrap()
                .iter()
                .any(|g| g.resource_key == "document.payment-output")
        );
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
