//! Bounded workflow definition types shared by validation, simulation and execution.
use crate::{approval_conditions::ApprovalCondition, identity::StableId};
use serde::{Deserialize, Serialize};
mod branches;
pub use branches::overlap;

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum NodeKind {
    Start,
    #[default]
    Approval,
    Exclusive,
    Fork,
    Join,
    Copy,
    Handling,
    System,
    Subflow,
    End,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum Phase {
    #[default]
    Decision,
    Fulfillment,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum NodeState {
    #[default]
    Pending,
    Active,
    Completed,
    Blocked,
    Cancelled,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum Logic {
    #[default]
    All,
    Any,
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApproverSource {
    Named,
    DepartmentManager,
    DepartmentLeaders,
    LineManager,
    FunctionalManagers,
    CompanyExecutive,
    CompanyLeaders,
    Position,
    Responsibility,
    ProjectLeader,
    Handlers,
    Requester,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ActorSelector {
    pub source: ApproverSource,
    #[serde(default)]
    pub source_id: Option<StableId>,
    #[serde(default)]
    pub user_ids: Vec<i64>,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum HandlingAction {
    #[default]
    Complete,
    Receive,
    Issue,
    Return,
    Acceptance,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum SystemAction {
    #[default]
    None,
    ApplyPersonnel,
    ReserveSupply,
    GenerateApprovalDocument,
}
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ConditionGroup {
    #[serde(default)]
    pub logic: Logic,
    #[serde(default)]
    pub conditions: Vec<ApprovalCondition>,
    #[serde(default)]
    pub groups: Vec<ConditionGroup>,
}
impl ConditionGroup {
    pub fn validate(&self) -> Result<(), String> {
        fn visit(group: &ConditionGroup, depth: usize, count: &mut usize) -> Result<(), String> {
            if depth > 4 || group.conditions.len() + group.groups.len() > 16 {
                return Err("条件组合超过四层或十六项。".into());
            }
            *count += group.conditions.len();
            if *count > 16 {
                return Err("每个条件组合累计最多十六条比较。".into());
            }
            for condition in &group.conditions {
                condition.validate()?;
            }
            for child in &group.groups {
                if child.conditions.is_empty() && child.groups.is_empty() {
                    return Err("条件子组不能为空。".into());
                }
                visit(child, depth + 1, count)?;
            }
            Ok(())
        }
        visit(self, 1, &mut 0)
    }
    pub fn matches(
        &self,
        context: &crate::approval_conditions::ConditionContext,
    ) -> Result<bool, String> {
        for condition in &self.conditions {
            let result = condition.matches(context)?;
            if result != (self.logic == Logic::All) {
                return Ok(result);
            }
        }
        for group in &self.groups {
            let result = group.matches(context)?;
            if result != (self.logic == Logic::All) {
                return Ok(result);
            }
        }
        Ok(self.logic == Logic::All)
    }
    pub fn terms(&self) -> Result<Vec<Vec<&ApprovalCondition>>, String> {
        self.validate()?;
        let mut result = if self.logic == Logic::All {
            vec![vec![]]
        } else {
            vec![]
        };
        let children = self
            .conditions
            .iter()
            .map(|c| Ok(vec![vec![c]]))
            .chain(self.groups.iter().map(ConditionGroup::terms));
        for child in children {
            let child = child?;
            if self.logic == Logic::Any {
                result.extend(child);
            } else {
                let mut next = vec![];
                for left in &result {
                    for right in &child {
                        if next.len() >= 256 {
                            return Err("条件路径超过二百五十六条。".into());
                        }
                        let mut term = left.clone();
                        term.extend(right);
                        next.push(term);
                    }
                }
                result = next;
            }
            if result.len() > 256 {
                return Err("条件路径超过二百五十六条。".into());
            }
        }
        Ok(result)
    }
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Branch {
    pub target: String,
    #[serde(default)]
    pub condition: ConditionGroup,
    #[serde(default)]
    pub is_default: bool,
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum EscalationAction {
    Remind,
    NotifySupervisor,
    Reassign,
    Exception,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Escalation {
    pub after_hours: u32,
    pub action: EscalationAction,
    #[serde(default)]
    pub target: Option<ActorSelector>,
}
impl Escalation {
    pub fn validate(&self) -> Result<(), String> {
        if self.after_hours == 0 || self.after_hours > 8760 {
            return Err("升级间隔须为 1–8760 小时。".into());
        }
        let needs = matches!(
            self.action,
            EscalationAction::NotifySupervisor | EscalationAction::Reassign
        );
        if needs != self.target.is_some() {
            return Err("督办或转派须声明接任来源；提醒及异常不接受接任来源。".into());
        }
        if let Some(target) = &self.target {
            if target.user_ids.len() > 50 || target.user_ids.iter().any(|id| *id <= 0) {
                return Err("升级责任人配置无效。".into());
            }
            if target.source == ApproverSource::Named && target.user_ids.is_empty() {
                return Err("指定升级来源不能为空。".into());
            }
        }
        Ok(())
    }
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct InputMapping {
    pub source_path: String,
    pub target: ContextField,
}
impl InputMapping {
    pub fn source_field(&self) -> Result<ContextField, String> {
        Ok(match self.source_path.as_str() {
            "totalAmount" => ContextField::Amount,
            "currency" => ContextField::Currency,
            "durationDays" | "durationHours" => ContextField::Duration,
            "departmentId" => ContextField::Department,
            "category" => ContextField::Category,
            "personnelChange.action" => ContextField::PersonnelAction,
            "isReturnable" => ContextField::Returnable,
            _ => return Err("子流程输入只能引用声明的冻结条件字段。".into()),
        })
    }
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq, Hash)]
pub enum ContextField {
    Amount,
    Currency,
    Duration,
    Department,
    Category,
    PersonnelAction,
    Returnable,
}
impl ContextField {
    pub const fn slot(self) -> &'static str {
        match self {
            Self::Amount => "amount",
            Self::Currency => "currency",
            Self::Duration => "duration",
            Self::Department => "department",
            Self::Category => "category",
            Self::PersonnelAction => "personnelAction",
            Self::Returnable => "returnable",
        }
    }
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum OutputAggregation {
    #[default]
    AllEqual,
    Sum,
    AllTrue,
    AnyTrue,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct OutputMapping {
    pub node_key: String,
    pub verification_key: String,
    pub target: String,
    #[serde(default)]
    pub aggregation: OutputAggregation,
    #[serde(default)]
    pub required: bool,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum Propagation {
    #[default]
    Propagate,
    Block,
    Ignore,
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum Compensation {
    #[default]
    None,
    ManualReview,
    ReturnResource,
    CancelPendingPersonnel,
}
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SubflowPolicy {
    #[serde(default)]
    pub rejected: Propagation,
    #[serde(default)]
    pub returned: Propagation,
    #[serde(default)]
    pub cancelled: Propagation,
    #[serde(default)]
    pub exception: Propagation,
    #[serde(default)]
    pub compensation: Compensation,
}
impl SubflowPolicy {
    pub fn propagation(&self, outcome: &str) -> Propagation {
        match outcome {
            "Rejected" => self.rejected,
            "Returned" => self.returned,
            "Cancelled" => self.cancelled,
            _ => self.exception,
        }
    }
}
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AssignmentOrigin {
    #[serde(default)]
    pub source: Option<ApproverSource>,
    #[serde(default)]
    pub source_id: Option<StableId>,
    #[serde(default)]
    pub unit_code: String,
    #[serde(default)]
    pub appointment_id: String,
    #[serde(default)]
    pub department_id: Option<StableId>,
    #[serde(default)]
    pub subject_person_id: Option<i64>,
    #[serde(default)]
    pub subject_appointment_id: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ActorBinding {
    pub user_id: i64,
    pub person_id: Option<i64>,
    pub name: String,
    pub context: String,
    #[serde(default)]
    pub administration: bool,
    #[serde(default)]
    pub origin: AssignmentOrigin,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct NodeExecution {
    pub user_id: i64,
    pub person_id: Option<i64>,
    pub context: String,
    pub occurred_at: String,
    pub note: String,
    #[serde(default)]
    pub verification_values: std::collections::BTreeMap<String, serde_json::Value>,
}
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SubflowProgress {
    #[serde(default)]
    pub state: NodeState,
    #[serde(default)]
    pub outputs: std::collections::BTreeMap<String, serde_json::Value>,
    #[serde(default)]
    pub error_code: String,
    #[serde(default)]
    pub outcome: String,
    #[serde(default)]
    pub control_outcome: String,
    #[serde(default)]
    pub compensation_completed: bool,
}
pub fn key(value: &str, maximum: usize) -> bool {
    !value.is_empty()
        && value.len() <= maximum
        && value
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b"-_:".contains(&b))
}
pub fn path(value: &str) -> bool {
    value.len() <= 160 && value.split('.').all(|part| key(part, 40))
}
