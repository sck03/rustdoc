//! Unit-scoped deployment batches share the ordinary publication resolver and transaction.
use super::*;
use std::collections::HashSet;

fn entries<'a>(body: &'a Value) -> Result<&'a Vec<Value>> {
    let items = body["items"]
        .as_array()
        .filter(|items| !items.is_empty() && items.len() <= 50)
        .ok_or_else(|| invalid("请选择 1–50 个流程。"))?;
    let mut seen = HashSet::new();
    if items.iter().any(|item| {
        text(item, "definitionKey").is_empty() || !seen.insert(text(item, "definitionKey"))
    }) {
        return Err(invalid("流程编号缺失或重复。"));
    }
    Ok(items)
}

fn samples(tx: &Connection, actor: &Actor, rule: &Value, input: &Value) -> Result<Vec<Value>> {
    let revision = publication_coverage::revision(tx, &actor.company)?;
    if let Some(samples) = input["samples"]
        .as_array()
        .filter(|samples| !samples.is_empty())
    {
        return Ok(samples.clone());
    }
    let mut sample = input["sample"].clone();
    if !sample.is_object() {
        return Err(invalid("请选择用于校验的实际申请人员和任职。"));
    }
    for (key, value) in [
        ("amount", json!("0")),
        ("currency", json!("CNY")),
        ("duration", json!("1")),
        ("isReturnable", json!(true)),
    ] {
        if sample.get(key).is_none() {
            sample[key] = value;
        }
    }
    if rule["kind"] != "general" {
        sample["category"] = json!(if rule["kind"] == "leave" {
            "Annual"
        } else {
            "Other"
        });
        return Ok(vec![sample]);
    }
    let mut result = vec![];
    for (category, _) in export_doc_contracts::native_contract::general_categories::CATEGORIES {
        let mut current = sample.clone();
        current["category"] = json!(category);
        current["catalogRevision"] = json!(revision);
        if rule["generalHandling"][*category] == "ApprovalOnly" {
            current["serviceId"] = json!("");
        } else if let Some(service) = tx
            .search_handling_services(&actor.company, category, "", "", "active", "", 1)?
            .first()
        {
            current["serviceId"] = service["key"].clone();
            current["serviceRevision"] = service["revision"].clone();
        } else {
            continue;
        }
        result.push(current);
    }
    if result.is_empty() {
        return Err(invalid(
            "通用申请没有可用办理事项，请先在办理分工中配置至少一个类别及办理人员。",
        )
        .coded(crate::api::ErrorCode::HandlingUnconfigured));
    }
    Ok(result)
}

pub(super) fn preview(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    super::super::units::demand(actor, &actor.company, "organization.workflows")?;
    let settings = settings_store::load(tx, &actor.company)?;
    let disabling = text(body, "action") == "Disable";
    let mut results = vec![];
    for item in entries(body)? {
        crate::operation::check()?;
        let key = text(item, "definitionKey");
        let rule = settings["rules"]
            .as_array()
            .and_then(|rows| rows.iter().find(|row| row["key"] == key))
            .ok_or_else(|| invalid("所选流程不存在，请刷新目录。"))?;
        let revision = settings_patch::revision(&settings, &format!("rule:{key}"));
        let evaluated = if disabling {
            if settings["publications"].as_array().is_some_and(|rows| {
                rows.iter()
                    .any(|row| row["key"] == key && row["enabled"] == true)
            }) {
                Ok(vec![])
            } else {
                Err(conflict("该流程尚未启用，无需停用。"))
            }
        } else {
            samples(
                tx,
                actor,
                rule,
                &json!({"samples":item["samples"],"sample":body["sample"]}),
            )
            .and_then(|samples| {
                definitions::preview(tx, actor, &settings, &key, &samples)?;
                Ok(samples)
            })
        };
        let mut result = json!({"definitionKey":key,"name":rule["name"],"expectedRevision":revision,"ready":evaluated.is_ok(),"samples":[]});
        match evaluated {
            Ok(samples) => result["samples"] = json!(samples),
            Err(cause) if matches!(cause.status, Some(400 | 403 | 404 | 409)) => {
                result["failure"] = cause
                    .public_body(&crate::paths::nonce().map_err(super::super::error::unavailable)?)
            }
            Err(cause) => return Err(cause),
        }
        results.push(result);
    }
    Ok(
        json!({"expectedVersion":settings["versionNumber"],"catalogRevision":publication_coverage::revision(tx,&actor.company)?,"action":body["action"],"items":results}),
    )
}

pub(super) fn apply(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    super::super::units::demand(actor, &actor.company, "organization.workflows")?;
    let mut settings = settings_store::load(tx, &actor.company)?;
    let original_version = settings["versionNumber"].clone();
    if settings["id"].is_null() {
        settings = settings_store::save_registry(tx, actor, settings, "initialize-oa-registry")?;
    }
    // The caller owns one database transaction. A failed item rolls back the batch.
    if super::super::command_receipts::prepare(
        tx,
        actor,
        &mut settings,
        "DeployOaDefinitions",
        body,
    )? {
        return settings::response(tx, &actor.company, settings);
    }
    if original_version != body["expectedVersion"] {
        return Err(conflict("流程发布状态已变化，请重新预检后执行。"));
    }
    if body["catalogRevision"].as_i64() != Some(publication_coverage::revision(tx, &actor.company)?)
    {
        return Err(conflict("办理分工已变化，请重新预检后执行。"));
    }
    let disabling = text(body, "action") == "Disable";
    let action = if disabling {
        "settings-disable"
    } else {
        "settings-publish"
    };
    for item in entries(body)? {
        let key = text(item, "definitionKey");
        if item["expectedRevision"].as_i64()
            != Some(settings_patch::revision(&settings, &format!("rule:{key}")))
        {
            return Err(conflict(format!("流程“{key}”的草稿已修改，请重新预检。")));
        }
        definitions::change(tx, actor, &mut settings, action, item)?;
        if !disabling {
            settings_store::save(
                tx,
                actor,
                &json!({"section":"rules","definitionKey":key,"expectedRevision":item["expectedRevision"]}),
                &settings,
            )?;
        }
    }
    let saved = settings_store::save_registry(tx, actor, settings, "deploy-oa-definitions")?;
    settings::response(tx, &actor.company, saved)
}
