//! Independently versioned service records; directory size never limits editing.
use super::*;
use unicode_normalization::UnicodeNormalization;

pub(super) fn handle(
    tx: &Connection,
    actor: &Actor,
    action: &str,
    body: &Value,
    query: &[(&str, String)],
) -> Result<Value> {
    super::super::units::demand(actor, &actor.company, "organization.workflows")?;
    if action == "service-list" {
        return handling::directory(tx, actor, "", query);
    }
    let mut registry = settings_store::registry(tx, &actor.company)?;
    if registry["id"].is_null() {
        registry = settings_store::save_registry(tx, actor, registry, "initialize-oa-registry")?;
    }
    if super::super::command_receipts::prepare(tx, actor, &mut registry, action, body)? {
        return Ok(json!({"success":true}));
    }
    let mut service = body["service"].clone();
    if !service.is_object() {
        return Err(invalid("办理事项内容缺失。"));
    }
    for field in ["key", "name"] {
        service[field] = json!(text(&service, field).nfc().collect::<String>());
    }
    service.as_object_mut().unwrap().remove("handlerNames");
    let key = text(&service, "key");
    let previous = tx.get_handling_service(&actor.company, &key)?;
    let expected = previous
        .as_ref()
        .and_then(|s| s["revision"].as_i64())
        .unwrap_or(0);
    if service["revision"].as_i64().unwrap_or(0) != expected {
        return Err(conflict("该办理事项已被修改，请重新载入后核对。"));
    }
    if action == "service-remove" {
        if previous.is_none() {
            return Err(error(404, "办理事项不存在。"));
        }
        handling::validate(tx, actor, None, previous.as_ref())?;
        tx.remove_handling_service(&actor.company, &key)?;
    } else if action == "service-save" {
        handling::validate(tx, actor, Some(&service), previous.as_ref())?;
        // The provider's unique index also protects concurrent editors.
        tx.save_handling_service(&actor.company, &service)?;
        definitions::validate_service_change(tx, actor, &service)?;
        if previous
            .as_ref()
            .is_some_and(|old| old["handlerUserIds"] != service["handlerUserIds"])
        {
            handling_handover::notify(tx, actor, &key)?;
        }
    } else {
        return Err(invalid("未知办理事项操作。"));
    }
    settings_store::advance_services(tx, actor)?;
    settings_store::save_registry(tx, actor, registry, action)?;
    Ok(json!({"success":true}))
}
