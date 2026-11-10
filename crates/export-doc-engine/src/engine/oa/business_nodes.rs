//! The existing thirteen request adapters declare their default fulfillment graph.
use super::*;
use export_doc_domain::{
    approval_graph::{HandlingAction, NodeKind, SystemAction},
    approval_policy::ApprovalStage,
};
fn node(
    key: &str,
    name: &str,
    kind: NodeKind,
    action: HandlingAction,
    system: SystemAction,
    depends: &[String],
) -> Result<ApprovalStage> {
    Ok(serde_json::from_value(
        json!({"key":format!("builtin-fulfillment-{key}"),"name":name,"kind":kind,"phase":"Fulfillment","dependsOn":depends,"source":if kind==NodeKind::System{"Named"}else{"Handlers"},"approverUserIds":[],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":48,"handlingAction":action,"systemAction":system,"allowAdministration":kind==NodeKind::Handling}),
    )?)
}
pub(super) fn defaults(kind: &str, depends: &[String]) -> Result<Vec<ApprovalStage>> {
    use HandlingAction::*;
    use NodeKind::*;
    use SystemAction as SystemCommand;
    let mut result = vec![];
    match kind {
        "personnel" => result.push(node(
            "personnel",
            "人员变动生效",
            System,
            Complete,
            SystemCommand::ApplyPersonnel,
            depends,
        )?),
        "room" => {
            let issue = node(
                "issue",
                "交接使用",
                Handling,
                Issue,
                SystemCommand::None,
                depends,
            )?;
            let returned = node(
                "return",
                "归还或结束使用",
                Handling,
                Return,
                SystemCommand::None,
                &[issue.key.clone()],
            )?;
            result.extend([issue, returned]);
        }
        "supply" => {
            let reserve = node(
                "reserve",
                "原子预留库存",
                System,
                Complete,
                SystemCommand::ReserveSupply,
                depends,
            )?;
            let issue = node(
                "issue",
                "实际发放",
                Handling,
                Issue,
                SystemCommand::None,
                &[reserve.key.clone()],
            )?;
            let mut returned = node(
                "return",
                "借用品归还",
                Handling,
                Return,
                SystemCommand::None,
                &[issue.key.clone()],
            )?;
            returned.conditions = vec![export_doc_domain::approval_policy::ApprovalCondition {
                field_key: String::new(),
                field: export_doc_domain::approval_policy::ConditionField::Returnable,
                operator: export_doc_domain::approval_policy::ConditionOperator::Equal,
                value: "true".into(),
            }];
            result.extend([reserve, issue, returned]);
        }
        "expense" => result.push(node(
            "receive",
            "财务接收",
            Handling,
            Receive,
            SystemCommand::None,
            depends,
        )?),
        "purchase" => result.push(node(
            "acceptance",
            "实际采购验收",
            Handling,
            Acceptance,
            SystemCommand::None,
            depends,
        )?),
        "leave" | "overtime" | "travel" | "general" => result.push(node(
            "complete",
            "实际办理登记",
            Handling,
            Complete,
            SystemCommand::None,
            depends,
        )?),
        _ => return Err(invalid("申请类型没有登记办理适配器。")),
    }
    result.push(node(
        "document",
        "归档审批单",
        System,
        Complete,
        SystemCommand::GenerateApprovalDocument,
        depends,
    )?);
    Ok(result)
}
pub(super) fn instantiate_standard(
    rule: &mut export_doc_domain::approval_policy::ApprovalRule,
) -> Result<()> {
    use export_doc_domain::approval_graph::Phase;
    if rule.stages.iter().any(|n| n.phase == Phase::Fulfillment) {
        return Ok(());
    }
    let leaves = rule
        .stages
        .iter()
        .filter(|n| {
            !rule
                .stages
                .iter()
                .any(|other| other.depends_on.contains(&n.key))
        })
        .map(|n| n.key.clone())
        .collect::<Vec<_>>();
    rule.stages.extend(defaults(&rule.kind, &leaves)?);
    rule.validate().map_err(invalid)?;
    Ok(())
}
pub(super) fn validate_plan(
    rule: &export_doc_domain::approval_policy::ApprovalRule,
    plan: &export_doc_domain::approval::ApprovalPlan,
    context: &export_doc_domain::approval_policy::ConditionContext,
) -> Result<()> {
    for node in plan.nodes.iter().filter(|n| {
        matches!(n.kind, NodeKind::Handling | NodeKind::System)
            && n.system_action != SystemAction::GenerateApprovalDocument
    }) {
        let required = match rule.kind.as_str() {
            "personnel" => node.system_action == SystemAction::ApplyPersonnel,
            "supply" => {
                matches!(node.handling_action, HandlingAction::Issue)
                    || node.system_action == SystemAction::ReserveSupply
                    || node.handling_action == HandlingAction::Return
                        && context.returnable == "true"
            }
            "room" => matches!(
                node.handling_action,
                HandlingAction::Issue | HandlingAction::Return
            ),
            "expense" => node.handling_action == HandlingAction::Receive,
            "purchase" => node.handling_action == HandlingAction::Acceptance,
            _ => node.handling_action == HandlingAction::Complete,
        };
        if required && node.skipped {
            return Err(invalid(
                "申请路线不能跳过本业务必需的实际生效、接收、交付或验收节点。",
            ));
        }
        if rule.kind == "supply"
            && context.returnable == "false"
            && node.handling_action == HandlingAction::Return
            && !node.skipped
        {
            return Err(invalid(
                "消耗品路线不能等待实物归还，请按借用属性设置条件。",
            ));
        }
    }
    Ok(())
}
pub(super) fn validate(rule: &export_doc_domain::approval_policy::ApprovalRule) -> Result<()> {
    use export_doc_domain::approval_graph::Phase;
    let count = |kind, handling, system| {
        rule.stages
            .iter()
            .filter(|n| {
                n.phase == Phase::Fulfillment
                    && n.kind == kind
                    && n.handling_action == handling
                    && n.system_action == system
            })
            .count()
    };
    use HandlingAction::*;
    use NodeKind::*;
    let required = match rule.kind.as_str() {
        "personnel" => count(System, Complete, SystemAction::ApplyPersonnel) == 1,
        "supply" => {
            count(System, Complete, SystemAction::ReserveSupply) == 1
                && count(Handling, Issue, SystemAction::None) == 1
                && count(Handling, Return, SystemAction::None) == 1
        }
        "room" => {
            count(Handling, Issue, SystemAction::None) == 1
                && count(Handling, Return, SystemAction::None) == 1
        }
        "expense" => count(Handling, Receive, SystemAction::None) == 1,
        "purchase" => count(Handling, Acceptance, SystemAction::None) == 1,
        _ => count(Handling, Complete, SystemAction::None) >= 1,
    };
    if !required {
        return Err(invalid(
            "办理图须保留本业务唯一的实际生效、预留、交付、归还或接收节点。",
        ));
    }
    if rule.kind == "personnel"
        && rule
            .stages
            .iter()
            .find(|n| n.system_action == SystemAction::ApplyPersonnel)
            .is_some_and(|apply| {
                rule.stages.iter().any(|n| {
                    n.phase == Phase::Fulfillment
                        && n.depends_on.contains(&apply.key)
                        && n.system_action != SystemAction::GenerateApprovalDocument
                })
            })
    {
        return Err(invalid(
            "人员生效须为实际办理的最后一步，后续仅可生成技术文档。",
        ));
    }
    if rule.stages.iter().any(|n| {
        n.kind == Approval
            && n.phase == Phase::Fulfillment
            && matches!(n.handling_action, Issue | Return)
    }) {
        return Err(invalid(
            "实物交付及归还必须由办理节点执行，不能用核验签署代替。",
        ));
    }
    Ok(())
}
