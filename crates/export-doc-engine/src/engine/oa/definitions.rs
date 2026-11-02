//! Immutable published definitions and independent activation pointers.
use super::*;
use export_doc_domain::approval_policy::ApprovalRule;

fn identity(company: &str, key: &str, version: i64) -> String {
    store::normalize(&format!("{company}:{key}:{version}"))
}

pub(super) fn published(
    tx: &Connection,
    company: &str,
    kind: &str,
    variant: &str,
) -> Result<(ApprovalRule, i64)> {
    let settings = settings::load(tx, company)?;
    let key = format!(
        "{kind}{}",
        if variant.is_empty() {
            String::new()
        } else {
            format!(":{variant}")
        }
    );
    let publication = settings["publications"]
        .as_array()
        .and_then(|items| {
            items
                .iter()
                .find(|p| p["key"] == key && p["enabled"] == true)
        })
        .ok_or_else(|| conflict("该业务尚未发布启用的审批流程，请管理员配置并发布。"))?;
    let version = records::positive(publication, "version", "流程版本")?;
    let snapshot = tx
        .find_identity("oa-definition-version", &identity(company, &key, version))?
        .ok_or_else(|| super::super::error::unavailable("已发布流程版本缺失。"))?;
    let rule: ApprovalRule = serde_json::from_value(snapshot["definition"].clone())
        .map_err(|_| super::super::error::unavailable("已发布流程定义损坏。"))?;
    rule.validate().map_err(super::super::error::unavailable)?;
    Ok((rule, version))
}
pub(super) fn change(
    tx: &Connection,
    actor: &Actor,
    previous: &mut Value,
    action: &str,
    body: &Value,
) -> Result<()> {
    let key = text(body, "definitionKey");
    let mut rule = previous["rules"]
        .as_array()
        .and_then(|rules| rules.iter().find(|r| r["key"] == key))
        .ok_or_else(|| invalid("流程编号不存在。"))?
        .clone();
    let calendar = previous["calendar"].clone();
    let publications = previous["publications"]
        .as_array()
        .ok_or_else(|| super::super::error::unavailable("流程发布记录损坏。"))?;
    let existing = publications.iter().position(|p| p["key"] == key);
    if action == "settings-disable" {
        let index = existing.ok_or_else(|| conflict("流程尚未发布。"))?;
        previous["publications"][index]["enabled"] = json!(false);
        return Ok(());
    }
    rule["calendar"] = calendar;
    let mut typed: ApprovalRule =
        serde_json::from_value(rule.clone()).map_err(|_| invalid("流程定义格式无效。"))?;
    super::business_nodes::instantiate_standard(&mut typed)?;
    rule = serde_json::to_value(&typed)?;
    if let Some(draft) = previous["rules"]
        .as_array_mut()
        .and_then(|rules| rules.iter_mut().find(|r| r["key"] == key))
    {
        *draft = rule.clone();
    }
    let publications = previous["publications"]
        .as_array_mut()
        .ok_or_else(|| super::super::error::unavailable("发布目录损坏。"))?;
    typed.validate().map_err(invalid)?;
    super::approval_integrity::rule(tx, actor, &actor.company, &typed)?;
    let samples = body["samples"]
        .as_array()
        .filter(|s| !s.is_empty() && s.len() <= 20)
        .ok_or_else(|| invalid("发布前必须提供 1–20 个实际样例并通过同一内核仿真。"))?;
    for sample in samples {
        let mut sample = sample.clone();
        sample["definition"] = serde_json::to_value(&typed)?;
        super::simulation::simulate_in(tx, actor, &sample)?;
    }
    let version = tx.workflow_version_next(&actor.company, &key)?;
    let snapshot = store::save(
        tx,
        "oa-definition-version",
        0,
        json!({"definition":rule,"version":version,"publishedAt":store::timestamp(),"publishedBy":actor.id}),
        Some(identity(&actor.company, &key, version)),
        actor,
        "publish-definition",
    )?;
    if action == "settings-schedule" {
        let requested = text(body, "activateAt");
        let at = chrono::DateTime::parse_from_rfc3339(&requested)
            .map_err(|_| invalid("定时启用须使用包含时区的时间。"))?
            .with_timezone(&chrono::Utc);
        let now = chrono::Utc::now();
        if at <= now || at > now + chrono::Duration::days(366) {
            return Err(invalid("定时启用须在未来三百六十六天以内。"));
        }
        let command = text(body, "commandKey");
        super::super::command_receipts::validate_key(&command)?;
        let mut request = body.clone();
        request.sort_all_objects();
        tx.workflow_activation_save(&json!({"company":actor.company,"definitionKey":key,"definitionRecordId":snapshot["id"],"definitionVersion":version,"expectedActiveVersion":existing.map(|i|publications[i]["version"].clone()).unwrap_or(json!(0)),"expectedActiveEnabled":existing.is_some_and(|i|publications[i]["enabled"]==true),"actorId":actor.id,"contextId":actor.context,"commandKey":command,"requestDigest":super::super::media::digest(&serde_json::to_vec(&request)?),"activateAt":at.to_rfc3339_opts(chrono::SecondsFormat::Nanos,true)}))?;
        return Ok(());
    }
    let publication = json!({"key":key,"version":version,"enabled":true});
    if let Some(index) = existing {
        publications[index] = publication;
    } else {
        publications.push(publication);
    }
    Ok(())
}
pub(super) fn activate_scheduled(tx: &Connection, actor: &Actor, entry: &Value) -> Result<()> {
    super::super::units::demand(actor, &text(entry, "companyCode"), "organization.workflows")?;
    let mut previous = settings::load(tx, &actor.company)?;
    let key = text(entry, "definitionKey");
    let current = previous["publications"]
        .as_array()
        .and_then(|p| p.iter().find(|p| p["key"] == key))
        .cloned();
    let current_version = current
        .as_ref()
        .map(|p| p["version"].clone())
        .unwrap_or(json!(0));
    let current_enabled = current.as_ref().is_some_and(|p| p["enabled"] == true);
    if current_version != entry["expectedActiveVersion"]
        || json!(current_enabled) != entry["expectedActiveEnabled"]
    {
        return Err(conflict("活动流程已变更，定时启用进入待处理状态。"));
    }
    let snapshot = store::get(
        tx,
        "oa-definition-version",
        records::positive(entry, "definitionRecordId", "发布版本")?,
    )?;
    let rule: ApprovalRule = serde_json::from_value(snapshot["definition"].clone())
        .map_err(|_| super::super::error::unavailable("定时流程版本损坏。"))?;
    super::approval_integrity::rule(tx, actor, &actor.company, &rule)?;
    let publications = previous["publications"]
        .as_array_mut()
        .ok_or_else(|| super::super::error::unavailable("发布目录损坏。"))?;
    let value = json!({"key":key,"version":entry["definitionVersion"],"enabled":true});
    if let Some(i) = publications.iter().position(|p| p["key"] == key) {
        publications[i] = value;
    } else {
        publications.push(value);
    }
    let id = records::positive(&previous, "id", "流程配置")?;
    store::save(
        tx,
        settings::STORAGE_KIND,
        id,
        previous,
        Some(store::normalize(&actor.company)),
        actor,
        "scheduled-activation",
    )?;
    Ok(())
}
