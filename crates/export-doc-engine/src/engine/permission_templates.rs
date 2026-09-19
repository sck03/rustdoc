//! Permission template invariants and projections shared by both frontends.
use super::{
    error::{Result, conflict, invalid},
    records::{required, text},
};
use export_doc_domain::permissions::{self, Grant};
use serde_json::{Value, json};

pub fn project(mut value: Value) -> Result<Value> {
    let grants: Vec<Grant> = serde_json::from_value(value["grants"].clone())?;
    value["effectiveGrants"] =
        serde_json::to_value(permissions::effective_details(&grants).map_err(invalid)?)?;
    Ok(value)
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
    let grants = permissions::normalize(&grants).map_err(invalid)?;
    if grants.iter().any(|grant| {
        permissions::resource(&grant.resource_key).is_some_and(|resource| resource.is_technical)
    }) {
        return Err(invalid(
            "系统身份与技术依赖能力由服务端派生，不能通过权限方案直接授予。",
        ));
    }
    value["grants"] = serde_json::to_value(grants)?;
    // Derived permission explanations are recomputed at read time.
    value["effectiveGrants"] = json!([]);
    Ok(())
}
