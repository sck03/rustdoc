//! Fixed-version subflows compile into the same bounded graph. The parent waits
//! on child leaves; signatures and exceptional commands use one kernel.
use super::*;
use export_doc_domain::{
    approval::SubflowInstance, approval_policy::ApprovalRule, business_calendar::BusinessCalendar,
};
use std::collections::{BTreeMap, HashSet};
pub(super) struct Compiled {
    pub rule: ApprovalRule,
    pub calendars: BTreeMap<String, BusinessCalendar>,
    pub instances: Vec<SubflowInstance>,
}
pub(super) fn compile(tx: &Connection, company: &str, rule: &ApprovalRule) -> Result<Compiled> {
    let calendar = match &rule.calendar {
        Some(c) => c.clone(),
        None => serde_json::from_value(settings::load(tx, company)?["calendar"].clone())
            .map_err(|_| super::super::error::unavailable("公司日历损坏。"))?,
    };
    let mut result = Compiled {
        rule: rule.clone(),
        calendars: BTreeMap::from([("main".into(), calendar)]),
        instances: vec![],
    };
    expand(
        tx,
        company,
        &mut result.rule,
        &mut result.calendars,
        &mut result.instances,
        &mut HashSet::new(),
        0,
    )?;
    if result.rule.stages.len() > 32 {
        return Err(invalid("根流程与子流程累计节点最多 32 个。"));
    }
    Ok(result)
}
fn expand(
    tx: &Connection,
    company: &str,
    rule: &mut ApprovalRule,
    calendars: &mut BTreeMap<String, BusinessCalendar>,
    instances: &mut Vec<SubflowInstance>,
    visiting: &mut HashSet<(String, i64)>,
    depth: usize,
) -> Result<()> {
    if depth >= 4 {
        return Err(invalid("子流程最多四层，不能递归引用。"));
    }
    let mut index = 0;
    while index < rule.stages.len() {
        let wrapper = rule.stages[index].clone();
        let Some(reference) = &wrapper.subflow else {
            index += 1;
            continue;
        };
        if reference.version <= 0
            || !visiting.insert((reference.definition_key.clone(), reference.version))
        {
            return Err(invalid("子流程版本无效或存在递归引用。"));
        }
        let identity = store::normalize(&format!(
            "{company}:{}:{}",
            reference.definition_key, reference.version
        ));
        let snapshot = tx
            .find_identity("oa-definition-version", &identity)?
            .ok_or_else(|| conflict("子流程必须绑定本单位已经发布的明确版本。"))?;
        let mut child: ApprovalRule = serde_json::from_value(snapshot["definition"].clone())
            .map_err(|_| super::super::error::unavailable("子流程定义损坏。"))?;
        child.validate().map_err(invalid)?;
        if child.kind != rule.kind && child.kind != "general" {
            return Err(invalid(
                "子流程须使用同一业务类型或通用审核上下文，不能引入其他业务生效动作。",
            ));
        }
        let prefix = format!("sub{depth}-{}", instances.len());
        if let Some(calendar) = &child.calendar {
            calendars.insert(prefix.clone(), calendar.clone());
        }
        for stage in &mut child.stages {
            stage.runtime_calendar_key = prefix.clone();
        }
        let first_nested = instances.len();
        expand(
            tx,
            company,
            &mut child,
            calendars,
            instances,
            visiting,
            depth + 1,
        )?;
        visiting.remove(&(reference.definition_key.clone(), reference.version));
        for nested in &mut instances[first_nested..] {
            for key in &mut nested.node_keys {
                *key = format!("{prefix}:{key}");
            }
        }
        rule.notify_user_ids
            .extend(child.notify_user_ids.iter().copied());
        rule.notify_user_ids.sort();
        rule.notify_user_ids.dedup();
        if rule.notify_user_ids.len() > 50 {
            return Err(invalid("子流程累计抄送人数超过 50 人。"));
        }
        let keys = child
            .stages
            .iter()
            .map(|s| s.key.clone())
            .collect::<HashSet<_>>();
        let leaves = child
            .stages
            .iter()
            .filter(|s| !child.stages.iter().any(|n| n.depends_on.contains(&s.key)))
            .map(|s| format!("{prefix}:{}", s.key))
            .collect::<Vec<_>>();
        let mut stages = vec![];
        for mut stage in child.stages {
            let original = stage.key.clone();
            stage.key = format!("{prefix}:{original}");
            stage.depends_on = if stage.depends_on.is_empty() {
                wrapper.depends_on.clone()
            } else {
                stage
                    .depends_on
                    .iter()
                    .map(|k| {
                        if keys.contains(k) {
                            format!("{prefix}:{k}")
                        } else {
                            k.clone()
                        }
                    })
                    .collect()
            };
            let mut conditions = wrapper.conditions.clone();
            conditions.extend(stage.conditions);
            if conditions.len() > 16 {
                return Err(invalid("子流程继承条件超过 16 项。"));
            }
            stage.conditions = conditions;
            stages.push(stage);
        }
        let nodes = stages.iter().map(|s| s.key.clone()).collect::<Vec<_>>();
        for stage in &mut rule.stages {
            if stage.depends_on.contains(&wrapper.key) {
                stage.depends_on.retain(|key| key != &wrapper.key);
                stage.depends_on.extend(leaves.clone());
            }
        }
        let count = stages.len();
        rule.stages.splice(index..=index, stages);
        instances.push(SubflowInstance {
            key: prefix,
            definition_key: reference.definition_key.clone(),
            version: reference.version,
            definition_id: records::positive(&snapshot, "id", "子流程版本")?,
            node_keys: nodes,
        });
        if rule.stages.len() > 32 || instances.len() > 32 {
            return Err(invalid("子流程超过统一节点或实例容量。"));
        }
        index += count;
    }
    Ok(())
}
