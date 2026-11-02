//! One bounded approval graph; a published definition contains no runtime state.
use crate::approval::{ApprovalDecision, ApprovalVoting};
pub use crate::approval_conditions::{
    ApprovalCondition, ConditionContext, ConditionField, ConditionOperator,
};
pub use crate::approval_graph::ApproverSource;
use crate::approval_graph::{
    Branch, ConditionGroup, Escalation, HandlingAction, Logic, NodeKind, Phase, SystemAction,
};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum OrganizationRouteMode {
    #[default]
    Current,
    Specified,
    Ancestor,
}
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct OrganizationRoute {
    pub mode: OrganizationRouteMode,
    pub unit_id: Option<i64>,
}
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum SelfApproval {
    Skip,
    Reject,
}
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum RepeatedApprover {
    Allow,
    Reject,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ApprovalStage {
    #[serde(default)]
    pub kind: NodeKind,
    #[serde(default)]
    pub pair_key: String,
    #[serde(default)]
    pub phase: Phase,
    #[serde(default)]
    pub source_id: Option<crate::identity::StableId>,
    #[serde(default)]
    pub condition_logic: Logic,
    #[serde(default)]
    pub condition_groups: Vec<ConditionGroup>,
    #[serde(default)]
    pub branches: Vec<Branch>,
    #[serde(default)]
    pub handling_action: HandlingAction,
    #[serde(default)]
    pub system_action: SystemAction,
    #[serde(default)]
    pub escalations: Vec<Escalation>,
    #[serde(default)]
    pub allow_administration: bool,
    #[serde(default)]
    pub material_policy: crate::approval::MaterialPolicy,
    #[serde(default)]
    pub subflow: Option<SubflowReference>,
    #[serde(skip)]
    pub runtime_calendar_key: String,
    #[serde(default)]
    pub layout: Option<GraphPoint>,
    #[serde(default)]
    pub deadline_mode: crate::business_calendar::DeadlineMode,
    pub key: String,
    pub name: String,
    /// All predecessors must finish; independent roots run in parallel.
    pub depends_on: Vec<String>,
    pub source: ApproverSource,
    #[serde(default)]
    pub organization_route: OrganizationRoute,
    #[serde(default)]
    pub review_points: Vec<String>,
    pub approver_user_ids: Vec<i64>,
    pub decision: ApprovalDecision,
    #[serde(flatten)]
    pub voting: ApprovalVoting,
    pub self_approval: SelfApproval,
    pub repeated_approver: RepeatedApprover,
    /// Conjunction of typed conditions. Excluded nodes do not block successors.
    pub conditions: Vec<ApprovalCondition>,
    pub due_hours: u32,
    #[serde(default)]
    pub revision_policy: crate::approval::RevisionPolicy,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct GraphPoint {
    pub x: i32,
    pub y: i32,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SubflowReference {
    pub definition_key: String,
    pub version: i64,
    #[serde(default)]
    pub inputs: Vec<crate::approval_graph::InputMapping>,
    #[serde(default)]
    pub outputs: Vec<crate::approval_graph::OutputMapping>,
    #[serde(default)]
    pub policy: crate::approval_graph::SubflowPolicy,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ApprovalRule {
    #[serde(default)]
    pub calendar: Option<crate::business_calendar::BusinessCalendar>,
    pub key: String,
    pub kind: String,
    pub variant: String,
    pub name: String,
    pub stages: Vec<ApprovalStage>,
    pub notify_user_ids: Vec<i64>,
}
fn key(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 80
        && value
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b"-_:".contains(&b))
}
impl ApprovalRule {
    fn validate_branches(&self, node: &ApprovalStage) -> Result<(), String> {
        if node.branches.len() < 2
            || node.branches.len() > 16
            || node.branches.iter().filter(|b| b.is_default).count() != 1
        {
            return Err("排他节点须有 2–16 个分支及唯一的显式默认分支。".into());
        }
        let mut targets = HashSet::new();
        for (index, branch) in node.branches.iter().enumerate() {
            if !targets.insert(&branch.target)
                || !self.stages.iter().any(|n| {
                    n.key == branch.target
                        && n.depends_on.contains(&node.key)
                        && n.phase == node.phase
                })
            {
                return Err("分支目标须唯一、属于同一阶段且以排他节点为前置。".into());
            }
            branch.condition.validate()?;
            if branch.is_default {
                if !branch.condition.conditions.is_empty() || !branch.condition.groups.is_empty() {
                    return Err("默认分支不声明条件。".into());
                }
            } else {
                if branch.condition.conditions.is_empty() && branch.condition.groups.is_empty() {
                    return Err("普通分支须声明条件。".into());
                }
                for earlier in &node.branches[..index] {
                    if !earlier.is_default
                        && crate::approval_graph::overlap(&earlier.condition, &branch.condition)?
                    {
                        return Err("排他分支条件存在重叠，请修正后发布。".into());
                    }
                }
                for term in branch.condition.terms()? {
                    if term.iter().any(|c| c.field == ConditionField::Amount)
                        && !term.iter().any(|c| {
                            c.field == ConditionField::Currency
                                && c.operator == ConditionOperator::Equal
                                && ["CNY", "USD", "EUR", "HKD", "JPY", "GBP"]
                                    .contains(&c.value.as_str())
                        })
                    {
                        return Err("每条金额分支路径须明确限定币种。".into());
                    }
                }
            }
        }
        Ok(())
    }
    pub fn routed_exclusions(&self, context: &ConditionContext) -> Result<HashSet<String>, String> {
        self.routed_exclusions_in(context, &std::collections::BTreeMap::new())
    }
    pub fn routed_exclusions_in(
        &self,
        context: &ConditionContext,
        contexts: &std::collections::BTreeMap<String, ConditionContext>,
    ) -> Result<HashSet<String>, String> {
        let mut excluded = HashSet::new();
        for node in self.stages.iter().filter(|n| n.kind == NodeKind::Exclusive) {
            let context = contexts.get(&node.key).unwrap_or(context);
            let mut selected = None;
            for branch in node.branches.iter().filter(|b| !b.is_default) {
                if branch.condition.matches(context)? {
                    if selected.is_some() {
                        return Err("排他节点同时命中多个分支，已停止提交。".into());
                    }
                    selected = Some(branch.target.as_str());
                }
            }
            let selected = selected
                .or_else(|| {
                    node.branches
                        .iter()
                        .find(|b| b.is_default)
                        .map(|b| b.target.as_str())
                })
                .ok_or("排他节点没有命中或默认分支。")?;
            excluded.extend(
                node.branches
                    .iter()
                    .filter(|b| b.target != selected)
                    .map(|b| b.target.clone()),
            );
        }
        for _ in 0..self.stages.len() {
            let previous = excluded.len();
            for node in &self.stages {
                let affected = if matches!(node.kind, NodeKind::Join | NodeKind::Subflow) {
                    !node.depends_on.is_empty()
                        && node.depends_on.iter().all(|key| excluded.contains(key))
                } else {
                    node.depends_on.iter().any(|key| excluded.contains(key))
                };
                if affected {
                    excluded.insert(node.key.clone());
                }
            }
            if excluded.len() == previous {
                break;
            }
        }
        Ok(excluded)
    }
    pub fn validate(&self) -> Result<(), String> {
        self.check(true)
    }
    pub fn validate_draft(&self) -> Result<(), String> {
        self.check(false)
    }
    fn check(&self, publishing: bool) -> Result<(), String> {
        if let Some(calendar) = &self.calendar {
            calendar.validate()?;
        }
        if publishing
            && self
                .stages
                .iter()
                .any(|s| s.deadline_mode == crate::business_calendar::DeadlineMode::Working)
            && self.calendar.is_none()
        {
            return Err("工作时限流程须冻结公司工作日历。".into());
        }
        if !key(&self.key) || self.name.trim().is_empty() || self.name.chars().count() > 100 {
            return Err("流程编号或名称无效。".into());
        }
        if ![
            "leave",
            "overtime",
            "expense",
            "travel",
            "purchase",
            "general",
            "personnel",
            "room",
            "supply",
        ]
        .contains(&self.kind.as_str())
        {
            return Err("流程业务类型无效。".into());
        }
        if self.kind == "personnel"
            && !["Hire", "Confirm", "Transfer", "Depart", "Rehire"].contains(&self.variant.as_str())
        {
            return Err("人员流程须指定具体任职变动。".into());
        }
        if self.kind != "personnel" && !self.variant.is_empty() {
            return Err("此业务不支持人员变动类型。".into());
        }
        if self.variant.len() > 80 || self.stages.is_empty() || self.stages.len() > 32 {
            return Err("流程须有 1–32 个节点。".into());
        }
        let nodes: HashMap<_, _> = self
            .stages
            .iter()
            .enumerate()
            .map(|(i, n)| (n.key.as_str(), i))
            .collect();
        if nodes.len() != self.stages.len() {
            return Err("节点编号不能重复。".into());
        }
        let mut people = HashSet::new();
        if self.notify_user_ids.len() > 50
            || self
                .notify_user_ids
                .iter()
                .any(|id| *id <= 0 || !people.insert(id))
        {
            return Err("抄送账号无效、重复或超过 50 人。".into());
        }
        for node in &self.stages {
            node.expression().validate()?;
            if node.escalations.len() > 4 {
                return Err("每个节点最多四级升级。".into());
            }
            if !node.escalations.is_empty()
                && !matches!(node.kind, NodeKind::Approval | NodeKind::Handling)
            {
                return Err("只有人工审批和办理节点可以配置逐级升级。".into());
            }
            for escalation in &node.escalations {
                escalation.validate()?;
            }
            if node
                .escalations
                .windows(2)
                .any(|pair| pair[0].after_hours >= pair[1].after_hours)
            {
                return Err("逐级升级须按超过原时限的累计小时严格递增。".into());
            }
            if matches!(node.kind, NodeKind::Handling | NodeKind::System)
                && node.phase != Phase::Fulfillment
            {
                return Err("实际办理和受控系统动作只可位于获批办理阶段。".into());
            }
            if (node.kind == NodeKind::System) != (node.system_action != SystemAction::None) {
                return Err("系统节点须声明受控动作，其他节点不得声明系统动作。".into());
            }
            if node.system_action == SystemAction::ApplyPersonnel && self.kind != "personnel"
                || node.system_action == SystemAction::ReserveSupply && self.kind != "supply"
            {
                return Err("受控动作不适用于本申请类型。".into());
            }
            if node.kind == NodeKind::Exclusive {
                self.validate_branches(node)?;
            } else if !node.branches.is_empty() {
                return Err("只有排他节点可声明分支。".into());
            }
            if matches!(node.kind, NodeKind::Fork | NodeKind::Join) {
                let paired = self
                    .stages
                    .iter()
                    .find(|p| {
                        p.key == node.pair_key && p.pair_key == node.key && p.phase == node.phase
                    })
                    .ok_or("分叉与汇合须互相配对并属于同一阶段。")?;
                if (node.kind == NodeKind::Fork && paired.kind != NodeKind::Join)
                    || (node.kind == NodeKind::Join
                        && !matches!(paired.kind, NodeKind::Fork | NodeKind::Exclusive))
                {
                    return Err("分叉只能配对汇合；汇合须配对分叉或排他分支。".into());
                }
                if node.kind == NodeKind::Join && node.depends_on.len() < 2 {
                    return Err("汇合须有至少两条前置分支。".into());
                }
                if node.kind == NodeKind::Fork {
                    let successors = self
                        .stages
                        .iter()
                        .filter(|n| n.depends_on.contains(&node.key))
                        .collect::<Vec<_>>();
                    if successors.len() < 2 {
                        return Err("并行分叉须有至少两条后续分支。".into());
                    }
                    let mut ancestors = HashSet::from([paired.key.clone()]);
                    for _ in 0..self.stages.len() {
                        for n in &self.stages {
                            if ancestors.contains(&n.key) {
                                ancestors.extend(n.depends_on.iter().cloned());
                            }
                        }
                    }
                    if successors.iter().any(|n| !ancestors.contains(&n.key)) {
                        return Err("分叉的每条分支必须汇入配对节点。".into());
                    }
                }
            } else if !node.pair_key.is_empty() && node.kind != NodeKind::Exclusive {
                return Err("只有分叉、排他和汇合可以声明配对节点。".into());
            }
            if let Some(child) = &node.subflow {
                if node.kind != NodeKind::Subflow
                    || !key(&child.definition_key)
                    || child.version <= 0
                    || child.inputs.len() > 32
                    || child.outputs.len() > 32
                {
                    return Err("子流程类型、版本或映射数量无效。".into());
                }
                let mut targets = HashSet::new();
                for input in &child.inputs {
                    input.source_field()?;
                    if !targets.insert(input.target) {
                        return Err("子流程输入路径无效或目标重复。".into());
                    }
                }
                let mut targets = HashSet::new();
                for output in &child.outputs {
                    if !key(&output.node_key)
                        || !crate::approval_graph::key(&output.verification_key, 40)
                        || !crate::approval_graph::key(&output.target, 40)
                        || !targets.insert(&output.target)
                    {
                        return Err("子流程输出映射无效或目标重复。".into());
                    }
                }
            } else if node.kind == NodeKind::Subflow && node.runtime_calendar_key.is_empty() {
                return Err("子流程节点缺少固定发布版本。".into());
            }
            node.material_policy.validate()?;
            if node
                .layout
                .as_ref()
                .is_some_and(|p| !(0..=10000).contains(&p.x) || !(0..=10000).contains(&p.y))
            {
                return Err("流程画布位置超出范围。".into());
            }
            node.revision_policy.validate()?;
            node.voting.validate(node.decision)?;
            if node.review_points.len() > 12
                || node
                    .review_points
                    .iter()
                    .any(|p| p.trim().is_empty() || p.chars().count() > 200)
            {
                return Err("审批要点最多十二条，每条须为 1–200 字。".into());
            }
            let route = &node.organization_route;
            if node.source == ApproverSource::ProjectLeader
                && route.mode != OrganizationRouteMode::Current
            {
                return Err("项目职责仅用于申请所在经营单位的有效项目成员。".into());
            }
            if route.mode != OrganizationRouteMode::Current
                && !matches!(
                    node.source,
                    ApproverSource::CompanyExecutive
                        | ApproverSource::CompanyLeaders
                        | ApproverSource::Position
                        | ApproverSource::Responsibility
                )
            {
                return Err(
                    "指定管理单位或沿上级查找只适用于单位负责人、岗位或业务职责来源。".into(),
                );
            }
            if (route.mode == OrganizationRouteMode::Specified
                && route.unit_id.is_none_or(|id| id <= 0))
                || (route.mode != OrganizationRouteMode::Specified && route.unit_id.is_some())
            {
                return Err("指定管理单位须选择有效单位，其他路由不得携带指定单位。".into());
            }
            if !key(&node.key)
                || node.name.trim().is_empty()
                || node.name.chars().count() > 100
                || node.due_hours == 0
                || node.due_hours > 8760
            {
                return Err("节点编号、名称或办理时限无效。".into());
            }
            let mut dependencies = HashSet::new();
            if node.depends_on.iter().any(|id| {
                id == &node.key || !nodes.contains_key(id.as_str()) || !dependencies.insert(id)
            }) {
                return Err("前置节点不存在、重复或引用自身。".into());
            }
            let mut ids = HashSet::new();
            if matches!(
                node.kind,
                NodeKind::Approval | NodeKind::Copy | NodeKind::Handling
            ) && node.source == ApproverSource::Named
            {
                if (publishing && node.approver_user_ids.is_empty())
                    || node.approver_user_ids.len() > 50
                    || node
                        .approver_user_ids
                        .iter()
                        .any(|id| *id <= 0 || !ids.insert(id))
                {
                    return Err("指定节点须有 1–50 位互不重复的审批人。".into());
                }
            } else if node.source != ApproverSource::Named && !node.approver_user_ids.is_empty() {
                return Err("组织来源节点不能另填指定账号。".into());
            }
            let needs_id = matches!(
                node.source,
                ApproverSource::Position
                    | ApproverSource::Responsibility
                    | ApproverSource::ProjectLeader
            );
            if needs_id != node.source_id.is_some() {
                return Err("岗位、职责和项目来源须有稳定编号；其他来源不接受该编号。".into());
            }
            if node.kind == NodeKind::Approval
                && matches!(
                    node.source,
                    ApproverSource::Requester | ApproverSource::Handlers
                )
            {
                return Err("申请人和办理分工不能作为审批资格来源。".into());
            }
            if node.phase == Phase::Decision
                && node.depends_on.iter().any(|id| {
                    self.stages
                        .iter()
                        .any(|n| &n.key == id && n.phase == Phase::Fulfillment)
                })
            {
                return Err("批准前的节点不能依赖获批办理节点。".into());
            }
            let expression = node.expression();
            for condition in expression.terms()?.into_iter().flatten() {
                condition.validate()?;
                let applies = match condition.field {
                    ConditionField::Amount | ConditionField::Currency => {
                        matches!(self.kind.as_str(), "expense" | "purchase")
                    }
                    ConditionField::Duration => {
                        matches!(self.kind.as_str(), "leave" | "travel" | "overtime")
                    }
                    ConditionField::PersonnelAction => self.kind == "personnel",
                    ConditionField::Category => matches!(self.kind.as_str(), "general" | "leave"),
                    ConditionField::Department => true,
                    ConditionField::Returnable => self.kind == "supply",
                };
                if !applies {
                    return Err("条件字段不适用于本业务。".into());
                }
            }
            for term in expression.terms()? {
                if term.iter().any(|c| c.field == ConditionField::Amount)
                    && !term.iter().any(|c| {
                        c.field == ConditionField::Currency
                            && c.operator == ConditionOperator::Equal
                            && ["CNY", "USD", "EUR", "HKD", "JPY", "GBP"]
                                .contains(&c.value.as_str())
                    })
                {
                    return Err("金额条件必须同时限定一种有效币种，不进行隐式换汇。".into());
                }
            }
        }
        let mut done = HashSet::new();
        loop {
            let size = done.len();
            for node in &self.stages {
                if node.depends_on.iter().all(|id| done.contains(id.as_str())) {
                    done.insert(node.key.as_str());
                }
            }
            if done.len() == self.stages.len() {
                return Ok(());
            }
            if done.len() == size {
                return Err("流程存在循环依赖，无法发布。".into());
            }
        }
    }
    pub fn account_ids(&self) -> Vec<i64> {
        self.stages
            .iter()
            .flat_map(|s| s.approver_user_ids.iter().copied())
            .collect()
    }
    pub fn includes(&self, index: usize, context: &ConditionContext) -> Result<bool, String> {
        self.stages[index].expression().matches(context)
    }
    pub fn route_group(
        &self,
        index: usize,
        ids: Vec<i64>,
        owner: i64,
        applicant: Option<i64>,
        previous: &mut HashSet<i64>,
    ) -> Result<(Vec<i64>, Vec<String>), String> {
        let node = &self.stages[index];
        if ids.is_empty() {
            return Err(format!("{}没有有效负责人。", node.name));
        }
        let mut retained = vec![];
        let mut notes = vec![];
        for id in ids {
            if retained.contains(&id) {
                continue;
            }
            if id == owner || Some(id) == applicant {
                if node.self_approval == SelfApproval::Reject {
                    return Err(format!("{}包含申请人本人，请配置其他责任人。", node.name));
                }
                notes.push(format!("{}：账号 #{id} 为申请人本人，已回避。", node.name));
                continue;
            }
            if previous.contains(&id) && node.repeated_approver == RepeatedApprover::Reject {
                return Err(format!(
                    "{}要求独立复核，审批人不能兼任前面节点。",
                    node.name
                ));
            }
            retained.push(id);
        }
        previous.extend(&retained);
        if retained.is_empty() {
            return Err(format!("{}的责任人全部回避，请补充独立责任人。", node.name));
        }
        Ok((retained, notes))
    }
}
impl ApprovalStage {
    pub fn expression(&self) -> ConditionGroup {
        ConditionGroup {
            logic: self.condition_logic,
            conditions: self.conditions.clone(),
            groups: self.condition_groups.clone(),
        }
    }
}
