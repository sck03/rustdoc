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
    pub contexts: BTreeMap<String, export_doc_domain::approval_policy::ConditionContext>,
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
        contexts: Default::default(),
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
    super::business_nodes::validate(&result.rule)?;
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
        for output in &reference.outputs {
            let node = child
                .stages
                .iter()
                .find(|n| n.key == output.node_key)
                .ok_or_else(|| invalid("子流程输出引用的节点不存在。"))?;
            let field = node
                .material_policy
                .verification_fields
                .iter()
                .find(|f| f.key == output.verification_key)
                .ok_or_else(|| invalid("子流程输出引用未声明的核验字段。"))?;
            if output.required && !field.required {
                return Err(invalid("必需输出须绑定必填核验字段。"));
            }
            use export_doc_domain::{
                approval::VerificationKind, approval_graph::OutputAggregation,
            };
            if output.aggregation == OutputAggregation::Sum
                && field.kind != VerificationKind::Decimal
                || matches!(
                    output.aggregation,
                    OutputAggregation::AllTrue | OutputAggregation::AnyTrue
                ) && field.kind != VerificationKind::Boolean
            {
                return Err(invalid("子流程输出聚合与核验字段类型不一致。"));
            }
            if wrapper.phase == export_doc_domain::approval_graph::Phase::Decision
                && node.phase != wrapper.phase
            {
                return Err(invalid("审批阶段子流程的输出不能依赖获批后的办理。"));
            }
        }
        if child.kind != rule.kind && child.kind != "general" {
            return Err(invalid(
                "子流程须使用同一业务类型或通用审核上下文，不能引入其他业务生效动作。",
            ));
        }
        let prefix = format!("sub{depth}-{}", instances.len());
        let calendar = child
            .calendar
            .clone()
            .or_else(|| calendars.get("main").cloned())
            .ok_or_else(|| invalid("子流程日历缺失。"))?;
        calendars.insert(prefix.clone(), calendar);
        for stage in &mut child.stages {
            stage.runtime_calendar_key = prefix.clone();
            if wrapper.phase == export_doc_domain::approval_graph::Phase::Fulfillment {
                stage.phase = wrapper.phase;
            }
        }
        if wrapper.phase == export_doc_domain::approval_graph::Phase::Decision {
            child.stages.retain(|stage| stage.phase == wrapper.phase);
        }
        use export_doc_domain::approval_graph::{NodeKind, Propagation};
        let ignore = [
            reference.policy.rejected,
            reference.policy.returned,
            reference.policy.cancelled,
            reference.policy.exception,
        ]
        .contains(&Propagation::Ignore);
        if ignore
            && (child.stages.iter().any(|n| {
                matches!(n.kind, NodeKind::Handling | NodeKind::System)
                    && n.system_action
                        != export_doc_domain::approval_graph::SystemAction::GenerateApprovalDocument
            }) || reference.outputs.iter().any(|o| o.required))
        {
            return Err(invalid(
                "实际办理或必需输出的子流程不能忽略失败，请使用补偿、重试或明确终止。",
            ));
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
            nested.parent_key = if nested.parent_key.is_empty() {
                prefix.clone()
            } else {
                format!("{prefix}:{}", nested.parent_key)
            };
            nested.key = format!("{prefix}:{}", nested.key);
            nested.boundary_key = format!("{prefix}:{}", nested.boundary_key);
            for key in &mut nested.node_keys {
                *key = format!("{prefix}:{key}");
            }
            for output in &mut nested.outputs {
                output.node_key = format!("{prefix}:{}", output.node_key);
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
            .filter(|s| {
                s.phase == wrapper.phase
                    && !child
                        .stages
                        .iter()
                        .any(|n| n.phase == wrapper.phase && n.depends_on.contains(&s.key))
            })
            .map(|s| format!("{prefix}:{}", s.key))
            .collect::<Vec<_>>();
        let mut stages = vec![];
        let mut entry = wrapper.clone();
        entry.kind = export_doc_domain::approval_graph::NodeKind::Start;
        entry.subflow = None;
        stages.push(entry);
        for mut stage in child.stages {
            let original = stage.key.clone();
            stage.key = format!("{prefix}:{original}");
            if !stage.pair_key.is_empty() {
                stage.pair_key = format!("{prefix}:{}", stage.pair_key);
            }
            stage.depends_on = if stage.depends_on.is_empty() {
                vec![wrapper.key.clone()]
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
            let own = stage.expression();
            let parent = wrapper.expression();
            stage.conditions.clear();
            stage.condition_logic = export_doc_domain::approval_graph::Logic::All;
            stage.condition_groups = vec![];
            for group in [parent, own] {
                if !group.conditions.is_empty() || !group.groups.is_empty() {
                    stage.condition_groups.push(group);
                }
            }
            stage.expression().validate().map_err(invalid)?;
            for branch in &mut stage.branches {
                branch.target = format!("{prefix}:{}", branch.target);
            }
            stages.push(stage);
        }
        let nodes = stages
            .iter()
            .skip(1)
            .map(|s| s.key.clone())
            .collect::<Vec<_>>();
        let boundary_key = format!("{prefix}:result");
        let mut boundary = wrapper.clone();
        boundary.key = boundary_key.clone();
        boundary.subflow = None;
        boundary.runtime_calendar_key = prefix.clone();
        boundary.depends_on = leaves;
        boundary.conditions.clear();
        boundary.condition_groups.clear();
        stages.push(boundary);
        for stage in &mut rule.stages {
            if stage.depends_on.contains(&wrapper.key) {
                stage.depends_on.retain(|key| key != &wrapper.key);
                stage.depends_on.push(boundary_key.clone());
            }
        }
        let count = stages.len();
        rule.stages.splice(index..=index, stages);
        instances.push(SubflowInstance {
            key: prefix.clone(),
            definition_key: reference.definition_key.clone(),
            version: reference.version,
            definition_id: records::positive(&snapshot, "id", "子流程版本")?,
            node_keys: nodes,
            inputs: reference.inputs.clone(),
            outputs: reference
                .outputs
                .iter()
                .cloned()
                .map(|mut output| {
                    output.node_key = format!("{prefix}:{}", output.node_key);
                    output
                })
                .collect(),
            policy: reference.policy.clone(),
            input_snapshot: Value::Null,
            parent_key: String::new(),
            phase: wrapper.phase,
            boundary_key,
        });
        if rule.stages.len() > 32 || instances.len() > 32 {
            return Err(invalid("子流程超过统一节点或实例容量。"));
        }
        index += count;
    }
    Ok(())
}
pub(super) fn map_contexts(
    compiled: &mut Compiled,
    row: &Value,
    parent: &export_doc_domain::approval_policy::ConditionContext,
) -> Result<()> {
    use export_doc_domain::{approval_graph::ContextField, approval_policy::ConditionContext};
    let mut order = (0..compiled.instances.len()).collect::<Vec<_>>();
    order.sort_by_key(|i| compiled.instances[*i].key.matches(':').count());
    let mut contexts: BTreeMap<String, ConditionContext> = BTreeMap::new();
    let duration_sources = match approval::business_kind(&text(row, "kind")) {
        "leave" => vec![
            "leave.startsOn",
            "leave.endsOn",
            "leave.startPeriod",
            "leave.endPeriod",
            "durationDays",
        ],
        "travel" => vec!["travel.startsOn", "travel.endsOn", "durationDays"],
        _ => vec!["overtime.startsAt", "overtime.endsAt", "durationHours"],
    };
    let defaults = json!({"amount":["totalAmount"],"currency":["currency"],"duration":duration_sources,"department":["departmentId"],"category":[if row["kind"]=="leave"{"leave.category"}else{"category"}],"personnelAction":["personnelChange.action"],"returnable":["isReturnable"]});
    let mut provenance: BTreeMap<String, Value> = BTreeMap::new();
    for index in order {
        let child = &mut compiled.instances[index];
        let base = if child.parent_key.is_empty() {
            parent.clone()
        } else {
            contexts
                .get(&child.parent_key)
                .cloned()
                .ok_or_else(|| super::super::error::unavailable("子流程父输入缺失。"))?
        };
        let mut context = base.clone();
        let base_sources = if child.parent_key.is_empty() {
            defaults.clone()
        } else {
            provenance
                .get(&child.parent_key)
                .cloned()
                .ok_or_else(|| super::super::error::unavailable("子流程父输入权限来源缺失。"))?
        };
        let mut sources = base_sources.clone();
        for mapping in &child.inputs {
            let source = mapping.source_field().map_err(invalid)?;
            sources[mapping.target.slot()] = base_sources[source.slot()].clone();
            let value = match mapping.source_path.as_str() {
                "totalAmount" => base.amount.map(|v| v.to_string()).unwrap_or_default(),
                "currency" => base.currency.clone(),
                "durationDays" | "durationHours" => {
                    base.duration.map(|v| v.to_string()).unwrap_or_default()
                }
                "departmentId" => base.department.clone(),
                "category" => base.category.clone(),
                "personnelChange.action" => base.personnel_action.clone(),
                "isReturnable" => base.returnable.clone(),
                _ => {
                    return Err(invalid(
                        "子流程输入仅可取已声明的冻结条件字段，不能读取私密资料或任意路径。",
                    ));
                }
            };
            match mapping.target {
                ContextField::Amount | ContextField::Duration => {
                    let value = value
                        .parse::<rust_decimal::Decimal>()
                        .map_err(|_| invalid("子流程数字输入缺失或类型不符。"))?;
                    if value < rust_decimal::Decimal::ZERO {
                        return Err(invalid("子流程数字输入不能为负数。"));
                    }
                    if mapping.target == ContextField::Amount {
                        context.amount = Some(value);
                    } else {
                        context.duration = Some(value);
                    }
                }
                ContextField::Currency => context.currency = value,
                ContextField::Department => context.department = value,
                ContextField::Category => context.category = value,
                ContextField::PersonnelAction => context.personnel_action = value,
                ContextField::Returnable => {
                    if !["true", "false"].contains(&value.as_str()) {
                        return Err(invalid("子流程借用属性须为布尔输入。"));
                    }
                    context.returnable = value;
                }
            }
        }
        child.input_snapshot = json!({"parentRequestId":row["id"],"amount":context.amount.map(|v|v.to_string()),"currency":context.currency,"duration":context.duration.map(|v|v.to_string()),"department":context.department,"category":context.category,"personnelAction":context.personnel_action,"returnable":context.returnable,"sources":sources});
        for key in &child.node_keys {
            compiled.contexts.insert(key.clone(), context.clone());
        }
        contexts.insert(child.key.clone(), context);
        provenance.insert(child.key.clone(), sources);
    }
    Ok(())
}
