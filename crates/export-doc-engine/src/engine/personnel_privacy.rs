//! Private profile data has its own table and never enters directory search documents.
use super::{
    error::{Result, unavailable},
    store::Connection,
};
use serde_json::{Value, json};

const PUBLIC_FIELDS: &[&str] = &["fullName", "workEmail", "workPhone", "workLocation"];
pub(super) fn public_profile(profile: &mut Value) {
    if let Some(fields) = profile.as_object_mut() {
        fields.retain(|key, _| PUBLIC_FIELDS.contains(&key.as_str()));
    }
}

/// Only an already-authorized personnel detail or mutation may join private fields.
pub(super) fn hydrate(tx: &Connection, person: &mut Value) -> Result<()> {
    let id = person["id"]
        .as_i64()
        .ok_or_else(|| unavailable("人员编号损坏。"))?;
    let profile = tx
        .personnel_private_profile(id)?
        .ok_or_else(|| unavailable("人员私密档案缺失。"))?;
    restore(person, profile)
}

pub(super) fn restore(person: &mut Value, private: Value) -> Result<()> {
    if person["profile"].is_null() {
        person["profile"] = json!({});
    }
    let profile = person["profile"]
        .as_object_mut()
        .ok_or_else(|| unavailable("人员资料格式损坏。"))?;
    for (key, value) in private
        .as_object()
        .ok_or_else(|| unavailable("私密档案格式损坏。"))?
    {
        if PUBLIC_FIELDS.contains(&key.as_str()) {
            return Err(unavailable("私密档案混入公开字段。"));
        }
        profile.insert(key.clone(), value.clone());
    }
    Ok(())
}

/// Public appointment edits preserve private fields; explicit supplied values can clear them.
pub(super) fn split(person: &mut Value, previous: Option<&Value>) -> Result<Value> {
    let mut profile = previous
        .and_then(|p| p["profile"].as_object())
        .cloned()
        .unwrap_or_default();
    if let Some(submitted) = person["profile"].as_object() {
        profile.extend(submitted.clone());
    }
    let mut public = serde_json::Map::new();
    for key in PUBLIC_FIELDS {
        if let Some(value) = profile.remove(*key) {
            public.insert((*key).into(), value);
        }
    }
    person["profile"] = Value::Object(public);
    Ok(Value::Object(profile))
}
