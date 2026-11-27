//! Publication checks enabled services in bounded pages through the production resolver.
use super::*;
use export_doc_domain::{approval_policy::ApprovalRule, general_request::HandlingRequirement};
use std::collections::HashSet;

pub(super) fn revision(tx: &Connection, company: &str) -> Result<i64> {
    settings_store::service_revision(tx, company)
}
pub(super) fn observe(result: &Value, reached: &mut HashSet<String>) {
    for node in result["approvalPlan"]["nodes"]
        .as_array()
        .into_iter()
        .flatten()
    {
        if node["skipped"] == false {
            reached.insert(text(node, "key"));
        }
    }
}
pub(super) fn validate(
    tx: &Connection,
    actor: &Actor,
    rule: &ApprovalRule,
    samples: &[Value],
) -> Result<HashSet<String>> {
    let mut reached = HashSet::new();
    if rule.kind != "general" {
        return Ok(reached);
    }
    let definition = serde_json::to_value(rule)?;
    for (category, name) in export_doc_contracts::native_contract::general_categories::CATEGORIES {
        let required = rule.general_handling.get(*category) == Some(&HandlingRequirement::Required);
        let mut after = String::new();
        loop {
            crate::operation::check()?;
            let page = tx.search_handling_services(
                &actor.company,
                category,
                "",
                "",
                "active",
                &after,
                100,
            )?;
            if page.is_empty() && after.is_empty() && required {
                if samples.iter().any(|s| s["category"] == *category) {
                    return Err(invalid(format!(
                        "发布核验：{name}类别没有启用的办理事项，请先配置事项及办理人员。"
                    ))
                    .coded(crate::api::ErrorCode::HandlingUnconfigured));
                }
                break;
            }
            let sample = samples
                .iter()
                .find(|s| s["category"] == *category)
                .ok_or_else(|| invalid(format!("发布核验缺少“{name}”类别的人员任职样例。")))?;
            for service in &page {
                let mut input = sample.clone();
                input["definition"] = definition.clone();
                input["serviceId"] = service["key"].clone();
                input["serviceRevision"] = service["revision"].clone();
                let result = simulation::simulate_in(tx, actor, &input).map_err(|mut cause| {
                    if matches!(cause.status, Some(400 | 403 | 409)) {
                        cause.message = format!(
                            "发布核验：{name}／{}：{}",
                            text(service, "name"),
                            cause.message
                        );
                    }
                    cause
                })?;
                observe(&result, &mut reached);
            }
            if after.is_empty() && !required {
                let mut input = sample.clone();
                input["definition"] = definition.clone();
                input["serviceId"] = json!("");
                observe(&simulation::simulate_in(tx, actor, &input)?, &mut reached);
            }
            if page.len() < 100 {
                break;
            }
            after = text(page.last().unwrap(), "key");
        }
    }
    Ok(reached)
}

pub(super) fn branches(rule: &ApprovalRule, reached: &HashSet<String>) -> Result<()> {
    for stage in &rule.stages {
        let conditional = !stage.conditions.is_empty() || !stage.condition_groups.is_empty();
        if conditional && !reached.contains(&stage.key) {
            return Err(invalid(format!("节点“{}”的条件路线尚未被样例覆盖，请在单项仿真中补充能命中该节点的人员、类别、事项或金额后发布。",stage.name)).at("samples","补充条件分支样例并重新预检。"));
        }
        for branch in &stage.branches {
            if !reached.contains(&branch.target) {
                return Err(invalid(format!(
                    "节点“{}”通向“{}”的分支尚未被样例覆盖，请补充该路线的仿真样例。",
                    stage.name, branch.target
                ))
                .at("samples", "默认分支和每条条件分支都须通过仿真。"));
            }
        }
    }
    Ok(())
}
