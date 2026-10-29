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
    let rule = previous["rules"]
        .as_array()
        .and_then(|rules| rules.iter().find(|r| r["key"] == key))
        .ok_or_else(|| invalid("流程编号不存在。"))?
        .clone();
    let publications = previous["publications"]
        .as_array_mut()
        .ok_or_else(|| super::super::error::unavailable("流程发布记录损坏。"))?;
    let existing = publications.iter().position(|p| p["key"] == key);
    if action == "settings-disable" {
        let index = existing.ok_or_else(|| conflict("流程尚未发布。"))?;
        publications[index]["enabled"] = json!(false);
        return Ok(());
    }
    let typed: ApprovalRule =
        serde_json::from_value(rule.clone()).map_err(|_| invalid("流程定义格式无效。"))?;
    typed.validate().map_err(invalid)?;
    super::approval_integrity::rule(tx, actor, &actor.company, &typed)?;
    let version = existing
        .map(|i| publications[i]["version"].as_i64().unwrap_or(0))
        .unwrap_or(0)
        + 1;
    store::save(
        tx,
        "oa-definition-version",
        0,
        json!({"definition":rule,"version":version,"publishedAt":store::timestamp(),"publishedBy":actor.id}),
        Some(identity(&actor.company, &key, version)),
        actor,
        "publish-definition",
    )?;
    let publication = json!({"key":key,"version":version,"enabled":true});
    if let Some(index) = existing {
        publications[index] = publication;
    } else {
        publications.push(publication);
    }
    Ok(())
}
