//! One bounded approval graph; a published definition contains no runtime state.
use crate::approval::{ApprovalDecision, ApprovalVoting};
pub use crate::approval_conditions::{
    ApprovalCondition, ConditionContext, ConditionField, ConditionOperator,
};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApproverSource {
    Named,
    DepartmentManager,
    DepartmentLeaders,
    LineManager,
    FunctionalManagers,
    CompanyExecutive,
    CompanyLeaders,
}
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
            if route.mode != OrganizationRouteMode::Current
                && !matches!(
                    node.source,
                    ApproverSource::CompanyExecutive | ApproverSource::CompanyLeaders
                )
            {
                return Err("指定管理单位或沿上级查找只适用于单位负责人来源。".into());
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
            if node.source == ApproverSource::Named {
                if (publishing && node.approver_user_ids.is_empty())
                    || node.approver_user_ids.len() > 50
                    || node
                        .approver_user_ids
                        .iter()
                        .any(|id| *id <= 0 || !ids.insert(id))
                {
                    return Err("指定节点须有 1–50 位互不重复的审批人。".into());
                }
            } else if !node.approver_user_ids.is_empty() {
                return Err("组织来源节点不能另填指定账号。".into());
            }
            if node.conditions.len() > 16 {
                return Err("每个节点最多设置 16 个条件。".into());
            }
            for condition in &node.conditions {
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
                };
                if !applies {
                    return Err("条件字段不适用于本业务。".into());
                }
            }
            if node
                .conditions
                .iter()
                .any(|c| c.field == ConditionField::Amount)
                && !node.conditions.iter().any(|c| {
                    c.field == ConditionField::Currency
                        && c.operator == ConditionOperator::Equal
                        && ["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].contains(&c.value.as_str())
                })
            {
                return Err("金额条件必须同时限定一种有效币种，不进行隐式换汇。".into());
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
        for condition in &self.stages[index].conditions {
            if !condition.matches(context)? {
                return Ok(false);
            }
        }
        Ok(true)
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
