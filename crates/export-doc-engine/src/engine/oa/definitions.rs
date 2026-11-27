//! Immutable published definitions and independent activation pointers.
use super::*;
use export_doc_domain::approval_policy::ApprovalRule;

fn identity(company: &str, key: &str, version: i64) -> String {
    store::normalize(&format!("{company}:{key}:{version}"))
}
pub(super) fn validate_service_change(
    tx: &Connection,
    actor: &Actor,
    service: &Value,
) -> Result<()> {
    if service["isActive"] != true
        || matches!(service["category"].as_str(), Some("Room" | "Supply"))
    {
        return Ok(());
    }
    let registry = settings_store::registry(tx, &actor.company)?;
    for publication in registry["publications"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|p| p["enabled"] == true)
    {
        let snapshot = tx
            .find_identity(
                "oa-definition-version",
                &identity(
                    &actor.company,
                    &text(publication, "key"),
                    publication["version"].as_i64().unwrap_or(0),
                ),
            )?
            .ok_or_else(|| super::super::error::unavailable("启用的流程版本缺失。"))?;
        if snapshot["definition"]["kind"] != "general" {
            continue;
        }
        let samples = snapshot["samples"]
            .as_array()
            .ok_or_else(|| invalid("已发布流程缺少仿真样例，请重新发布。"))?;
        let mut sample = samples
            .iter()
            .find(|s| s["category"] == service["category"])
            .or_else(|| samples.first())
            .cloned()
            .ok_or_else(|| invalid("已发布流程缺少申请人员任职样例。"))?;
        sample["definition"] = snapshot["definition"].clone();
        sample["category"] = service["category"].clone();
        sample["serviceId"] = service["key"].clone();
        sample["serviceRevision"] = tx
            .get_handling_service(&actor.company, &text(service, "key"))?
            .ok_or_else(|| invalid("办理事项尚未保存。"))?["revision"]
            .clone();
        super::simulation::simulate_in(tx, actor, &sample).map_err(|mut cause| {
            if matches!(cause.status, Some(400 | 403 | 409)) {
                cause.message = format!(
                    "事项“{}”无法用于已启用流程“{}”：{}。请先修正流程或保存为停用状态。",
                    text(service, "name"),
                    text(&snapshot["definition"], "name"),
                    cause.message
                );
            }
            cause
        })?;
    }
    Ok(())
}

pub(super) fn published(
    tx: &Connection,
    company: &str,
    kind: &str,
    variant: &str,
) -> Result<(ApprovalRule, i64)> {
    let settings = super::settings_store::registry(tx, company)?;
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
    published_rules(tx, company, &[publication])?
        .pop()
        .ok_or_else(|| super::super::error::unavailable("已发布流程版本缺失。"))
}

/// Read the selected immutable versions in bounded batches within the caller's snapshot.
pub(super) fn published_rules(
    tx: &Connection,
    company: &str,
    publications: &[&Value],
) -> Result<Vec<(ApprovalRule, i64)>> {
    let identities = publications
        .iter()
        .map(|publication| {
            Ok(identity(
                company,
                &text(publication, "key"),
                records::positive(publication, "version", "流程版本")?,
            ))
        })
        .collect::<Result<Vec<_>>>()?;
    let mut snapshots = std::collections::BTreeMap::new();
    for snapshot in tx.records_by_identities(
        "oa-definition-version",
        &identities,
        &[("definition", "definition"), ("version", "version")],
    )? {
        let rule: ApprovalRule = serde_json::from_value(snapshot["definition"].clone())
            .map_err(|_| super::super::error::unavailable("已发布流程定义损坏。"))?;
        rule.validate().map_err(super::super::error::unavailable)?;
        let version = records::positive(&snapshot, "version", "流程版本")?;
        snapshots.insert(identity(company, &rule.key, version), (rule, version));
    }
    identities
        .into_iter()
        .map(|key| {
            snapshots
                .remove(&key)
                .ok_or_else(|| super::super::error::unavailable("已发布流程版本缺失或重复。"))
        })
        .collect()
}
pub(super) fn preview(
    tx: &Connection,
    actor: &Actor,
    settings: &Value,
    key: &str,
    samples: &[Value],
) -> Result<Value> {
    let mut rule = settings["rules"]
        .as_array()
        .and_then(|rows| rows.iter().find(|rule| rule["key"] == key))
        .cloned()
        .ok_or_else(|| invalid("流程编号不存在。"))?;
    rule["calendar"] = settings["calendar"].clone();
    let mut typed: ApprovalRule =
        serde_json::from_value(rule).map_err(|_| invalid("流程定义格式无效。"))?;
    super::business_nodes::instantiate_standard(&mut typed)?;
    typed.validate().map_err(invalid)?;
    super::approval_integrity::rule(tx, actor, &actor.company, &typed)?;
    if samples.is_empty() || samples.len() > 20 {
        return Err(invalid(
            "发布前必须提供 1–20 个实际样例并通过同一内核仿真。",
        ));
    }
    let catalog_revision = super::publication_coverage::revision(tx, &actor.company)?;
    let definition = serde_json::to_value(&typed)?;
    let mut reached = std::collections::HashSet::new();
    for sample in samples {
        if typed.kind == "general"
            && sample["catalogRevision"]
                .as_i64()
                .is_some_and(|revision| revision != catalog_revision)
        {
            return Err(conflict("办理事项目录已变更，请重新仿真并核对发布样例。"));
        }
        let mut sample = sample.clone();
        sample["definition"] = definition.clone();
        let result = super::simulation::simulate_in(tx, actor, &sample)?;
        super::publication_coverage::observe(&result, &mut reached);
    }
    reached.extend(super::publication_coverage::validate(
        tx, actor, &typed, samples,
    )?);
    super::publication_coverage::branches(&typed, &reached)?;
    Ok(definition)
}

pub(super) fn change(
    tx: &Connection,
    actor: &Actor,
    previous: &mut Value,
    action: &str,
    body: &Value,
) -> Result<()> {
    let key = text(body, "definitionKey");
    previous["rules"]
        .as_array()
        .and_then(|rules| rules.iter().find(|r| r["key"] == key))
        .ok_or_else(|| invalid("流程编号不存在。"))?;
    let publications = previous["publications"]
        .as_array()
        .ok_or_else(|| super::super::error::unavailable("流程发布记录损坏。"))?;
    let existing = publications.iter().position(|p| p["key"] == key);
    if action == "settings-disable" {
        let index = existing.ok_or_else(|| conflict("流程尚未发布。"))?;
        previous["publications"][index]["enabled"] = json!(false);
        return Ok(());
    }
    let samples = body["samples"]
        .as_array()
        .ok_or_else(|| invalid("缺少发布样例。"))?;
    let rule = preview(tx, actor, previous, &key, samples)?;
    if let Some(draft) = previous["rules"]
        .as_array_mut()
        .and_then(|rules| rules.iter_mut().find(|r| r["key"] == key))
    {
        *draft = rule.clone();
    }
    let publications = previous["publications"]
        .as_array_mut()
        .ok_or_else(|| super::super::error::unavailable("发布目录损坏。"))?;
    let version = tx.workflow_version_next(&actor.company, &key)?;
    let snapshot = store::save(
        tx,
        "oa-definition-version",
        0,
        json!({"definition":rule,"version":version,"publishedAt":store::timestamp(),"publishedBy":actor.id,"samples":samples,"catalogRevision":super::publication_coverage::revision(tx,&actor.company)?}),
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
    if rule.kind == "general"
        && snapshot["catalogRevision"].as_i64()
            != Some(super::publication_coverage::revision(tx, &actor.company)?)
    {
        return Err(
            conflict("办理分工已修改，定时发布核验已过期，请重新仿真发布。")
                .coded(crate::api::ErrorCode::PublicationStale),
        );
    }
    let samples = snapshot["samples"]
        .as_array()
        .ok_or_else(|| invalid("发布版本缺少核验样例。"))?;
    let mut reached = super::publication_coverage::validate(tx, actor, &rule, samples)?;
    for sample in samples {
        let mut input = sample.clone();
        input["definition"] = snapshot["definition"].clone();
        let result = super::simulation::simulate_in(tx, actor, &input)?;
        super::publication_coverage::observe(&result, &mut reached);
    }
    super::publication_coverage::branches(&rule, &reached)?;
    let publications = previous["publications"]
        .as_array_mut()
        .ok_or_else(|| super::super::error::unavailable("发布目录损坏。"))?;
    let value = json!({"key":key,"version":entry["definitionVersion"],"enabled":true});
    if let Some(i) = publications.iter().position(|p| p["key"] == key) {
        publications[i] = value;
    } else {
        publications.push(value);
    }
    super::settings_store::save_registry(tx, actor, previous, "scheduled-activation")?;
    Ok(())
}
