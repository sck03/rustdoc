//! Each editable section and definition owns a separate record and CAS version.
use super::*;
const SECTION: &str = "oa-setting-section";
const DRAFT: &str = "oa-definition-draft";
fn identity(company: &str, key: &str) -> String {
    store::normalize(&format!("{company}:{key}"))
}
pub(super) fn registry(tx: &Connection, company: &str) -> Result<Value> {
    Ok(tx
        .find_identity(settings::STORAGE_KIND, &store::normalize(company))?
        .unwrap_or(json!({"versionNumber":0,"publications":[]})))
}
fn default_section(tx: &Connection, key: &str) -> Result<Value> {
    Ok(match key {
        "calendar" => {
            let mut calendar = export_doc_domain::business_calendar::BusinessCalendar::default();
            calendar.time_zone = tx
                .clock
                .now()
                .map_err(super::super::error::unavailable)?
                .time_zone
                .into();
            serde_json::to_value(calendar)?
        }
        "sheetTemplates" => sheet_templates::defaults()?,
        _ => json!([]),
    })
}
pub(in crate::engine) fn section(tx: &Connection, company: &str, key: &str) -> Result<Value> {
    match tx.find_identity(SECTION, &identity(company, key))? {
        Some(record) => Ok(record["value"].clone()),
        None => default_section(tx, key),
    }
}
pub(super) fn load(tx: &Connection, company: &str) -> Result<Value> {
    let mut value = registry(tx, company)?;
    value["rules"] = json!(settings::defaults()?);
    value["revisions"] = json!({});
    for record in store::matching(tx, DRAFT, &[("companyScope", company.into())], &[])? {
        let key = text(&record, "key");
        value["revisions"][format!("rule:{key}")] = record["versionNumber"].clone();
        if record["value"].is_null() {
            continue;
        }
        let rules = value["rules"]
            .as_array_mut()
            .ok_or_else(|| super::super::error::unavailable("流程目录损坏。"))?;
        if let Some(index) = rules.iter().position(|r| r["key"] == key) {
            rules[index] = record["value"].clone();
        } else {
            rules.push(record["value"].clone());
        }
    }
    for key in [
        "calendar",
        "delegations",
        "handlingServices",
        "sheetTemplates",
    ] {
        if let Some(record) = tx.find_identity(SECTION, &identity(company, key))? {
            value[key] = record["value"].clone();
            value["revisions"][key] = record["versionNumber"].clone();
        } else {
            value[key] = default_section(tx, key)?;
            value["revisions"][key] = json!(0);
        }
    }
    Ok(value)
}
pub(super) fn save_registry(
    tx: &Connection,
    actor: &Actor,
    mut value: Value,
    action: &str,
) -> Result<Value> {
    for key in [
        "rules",
        "revisions",
        "calendar",
        "delegations",
        "handlingServices",
        "sheetTemplates",
        "sheetFieldCatalog",
        "sheetDefaultContent",
        "activations",
    ] {
        value.as_object_mut().unwrap().remove(key);
    }
    store::save(
        tx,
        settings::STORAGE_KIND,
        value["id"].as_i64().unwrap_or(0),
        value,
        Some(store::normalize(&actor.company)),
        actor,
        action,
    )?;
    load(tx, &actor.company)
}
pub(super) fn save(
    tx: &Connection,
    actor: &Actor,
    patch: &Value,
    validated: &Value,
) -> Result<Value> {
    let section = text(patch, "section");
    let definition = text(patch, "definitionKey");
    let (kind, key) = if section == "rules" {
        (DRAFT, definition.as_str())
    } else {
        (SECTION, section.as_str())
    };
    let identity = identity(&actor.company, key);
    let previous = tx.find_identity(kind, &identity)?;
    let expected = patch["expectedRevision"]
        .as_i64()
        .ok_or_else(|| invalid("设置版本缺失。"))?;
    if previous.as_ref().map(|v| store::expected(v)).unwrap_or(0) != expected {
        return Err(conflict("当前设置版本已改变。"));
    }
    let id = previous
        .as_ref()
        .and_then(|v| v["id"].as_i64())
        .unwrap_or(0);
    if patch["remove"] == true {
        if tx.record_exists(
            "oa-definition-version",
            &[
                ("companyScope", actor.company.clone()),
                ("definition.key", key.into()),
            ],
        )? {
            return Err(conflict(
                "已发布或计划发布的事项不能删除，请停用或取消发布调度。",
            ));
        }
        if id == 0 {
            return Err(conflict("草稿已改变或不存在。"));
        }
        store::save(
            tx,
            kind,
            id,
            json!({"key":key,"value":null,"expectedVersion":expected}),
            Some(identity),
            actor,
            "remove-oa-definition",
        )?;
    } else {
        let content = if section == "rules" {
            validated["rules"]
                .as_array()
                .and_then(|rules| rules.iter().find(|r| r["key"] == key))
                .cloned()
                .ok_or_else(|| invalid("缺少流程草稿。"))?
        } else {
            validated[&section].clone()
        };
        store::save(
            tx,
            kind,
            id,
            json!({"key":key,"value":content,"expectedVersion":expected}),
            Some(identity),
            actor,
            "save-oa-configuration",
        )?;
    }
    let registry = registry(tx, &actor.company)?;
    if registry["id"].is_null() {
        save_registry(tx, actor, registry, "initialize-oa-registry")?;
    }
    load(tx, &actor.company)
}
pub(in crate::engine) fn revoke_delegations(
    tx: &Connection,
    actor: &Actor,
    account: i64,
) -> Result<()> {
    for mut record in store::matching(tx, SECTION, &[("key", "delegations".into())], &[])? {
        let mut changed = false;
        for item in record["value"].as_array_mut().into_iter().flatten() {
            if item["isActive"] == true
                && (item["principalUserId"] == account || item["delegateUserId"] == account)
            {
                item["isActive"] = json!(false);
                changed = true;
            }
        }
        if changed {
            let scope = record.clone();
            let identity = identity(&text(&record, "companyScope"), "delegations");
            record["expectedVersion"] = record["versionNumber"].clone();
            store::save_in_scope(
                tx,
                SECTION,
                records::positive(&record, "id", "代理设置")?,
                record,
                Some(identity),
                actor,
                "revoke-delegations",
                Some(&scope),
            )?;
        }
    }
    Ok(())
}
