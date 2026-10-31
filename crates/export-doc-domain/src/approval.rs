//! Runtime graph snapshots. Decisions belong to explicit nodes and tasks.
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
mod voting;
pub use voting::{ApprovalVoting, RejectionPolicy, VoteOutcome};
mod materials;
mod operations;
pub use materials::{MaterialPolicy, VerificationField, VerificationKind};
pub use operations::{RevisionKind, RevisionPolicy};
fn initial_generation() -> u32 {
    1
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApprovalDecision {
    All,
    Any,
    Ratio,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalNode {
    #[serde(default)]
    pub material_policy: MaterialPolicy,
    #[serde(default)]
    pub deadline_mode: crate::business_calendar::DeadlineMode,
    #[serde(default)]
    pub calendar_key: String,
    pub key: String,
    pub name: String,
    #[serde(default)]
    pub review_points: Vec<String>,
    pub depends_on: Vec<String>,
    pub decision: ApprovalDecision,
    #[serde(flatten)]
    pub voting: ApprovalVoting,
    pub due_hours: u32,
    pub skipped: bool,
    pub started_at: String,
    pub due_at: String,
    pub independent_review: bool,
    #[serde(default)]
    pub revision_policy: RevisionPolicy,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalStep {
    #[serde(default)]
    pub approver_person_id: Option<i64>,
    #[serde(default)]
    pub acted_by_person_id: Option<i64>,
    #[serde(default)]
    pub verification_values: std::collections::BTreeMap<String, serde_json::Value>,
    #[serde(default)]
    pub confirmed_review_points: Vec<u16>,
    pub task_key: String,
    pub stage: usize,
    pub decision: ApprovalDecision,
    pub approver_user_id: i64,
    pub approver_name: String,
    #[serde(default)]
    pub approver_context: String,
    pub status: String,
    pub acted_by_user_id: Option<i64>,
    pub acted_by_name: String,
    #[serde(default)]
    pub acted_by_context: String,
    pub acted_at: String,
    pub note: String,
    pub delegation_key: String,
    /// Reuse references the original decision; it never creates a new signature.
    #[serde(default)]
    pub source_generation: Option<u32>,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SubflowInstance {
    pub key: String,
    pub definition_key: String,
    pub version: i64,
    pub definition_id: i64,
    pub node_keys: Vec<String>,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalPlan {
    #[serde(default)]
    pub subflows: Vec<SubflowInstance>,
    #[serde(default)]
    pub calendars: std::collections::BTreeMap<String, crate::business_calendar::BusinessCalendar>,
    #[serde(default = "initial_generation")]
    pub generation: u32,
    #[serde(default)]
    pub suspended_at: String,
    #[serde(default)]
    pub paused_seconds: u64,
    pub definition_key: String,
    pub policy_version: i64,
    pub nodes: Vec<ApprovalNode>,
    pub steps: Vec<ApprovalStep>,
    pub notify_user_ids: Vec<i64>,
}
impl ApprovalPlan {
    pub fn subflow_outcome(&self, keys: &[String]) -> &'static str {
        let nodes = keys
            .iter()
            .filter_map(|key| self.nodes.iter().position(|n| &n.key == key))
            .collect::<Vec<_>>();
        if nodes.len() != keys.len() || nodes.is_empty() {
            return "Blocked";
        }
        if nodes
            .iter()
            .any(|i| self.node_outcome(*i) == VoteOutcome::Rejected)
        {
            return "Rejected";
        }
        if nodes
            .iter()
            .all(|i| self.node_outcome(*i) == VoteOutcome::Approved)
        {
            return "Approved";
        }
        if !self.suspended_at.is_empty() {
            return "Suspended";
        }
        if self
            .steps
            .iter()
            .any(|s| nodes.contains(&s.stage) && s.status == "Returned")
        {
            return "Returned";
        }
        if self
            .steps
            .iter()
            .filter(|s| nodes.contains(&s.stage))
            .all(|s| s.status == "Cancelled")
        {
            return "Cancelled";
        }
        "Pending"
    }
    fn node_outcome(&self, node: usize) -> VoteOutcome {
        let Some(definition) = self.nodes.get(node) else {
            return VoteOutcome::Rejected;
        };
        if definition.skipped {
            return VoteOutcome::Approved;
        }
        let steps: Vec<_> = self.steps.iter().filter(|s| s.stage == node).collect();
        definition
            .voting
            .outcome(
                definition.decision,
                steps.len(),
                steps.iter().filter(|s| s.status == "Approved").count(),
                steps.iter().filter(|s| s.status == "Rejected").count(),
                steps.iter().filter(|s| s.status == "Waiting").count(),
            )
            .unwrap_or(VoteOutcome::Rejected)
    }
    pub fn outcome(&self) -> &'static str {
        if self.steps.iter().any(|s| s.status == "Returned") {
            return "Returned";
        }
        if self
            .nodes
            .iter()
            .enumerate()
            .any(|(i, _)| self.node_outcome(i) == VoteOutcome::Rejected)
        {
            return "Rejected";
        }
        if self.completion().iter().all(|done| *done) {
            "Approved"
        } else {
            "Pending"
        }
    }
    fn completion(&self) -> Vec<bool> {
        // Resolve each node once. Dense join graphs must not expand all predecessor paths.
        let mut done = vec![None; self.nodes.len()];
        for _ in 0..self.nodes.len() {
            let mut changed = false;
            for (index, node) in self.nodes.iter().enumerate() {
                if done[index].is_some() {
                    continue;
                }
                let dependencies: Option<Vec<_>> = node
                    .depends_on
                    .iter()
                    .map(|key| {
                        self.nodes
                            .iter()
                            .position(|n| &n.key == key)
                            .and_then(|i| done[i])
                    })
                    .collect();
                if let Some(dependencies) = dependencies {
                    done[index] = Some(
                        dependencies.into_iter().all(|d| d)
                            && self.node_outcome(index) == VoteOutcome::Approved,
                    );
                    changed = true;
                }
            }
            if !changed {
                break;
            }
        }
        done.into_iter().map(|d| d.unwrap_or(false)).collect()
    }
    pub fn completed(&self, node: usize) -> bool {
        self.completion().get(node).copied().unwrap_or(false)
    }
    pub fn current_indices(&self) -> Vec<usize> {
        if self.outcome() != "Pending" {
            return vec![];
        }
        let completed = self.completion();
        self.steps
            .iter()
            .enumerate()
            .filter_map(|(i, s)| {
                (s.status == "Waiting"
                    && self.nodes[s.stage].depends_on.iter().all(|key| {
                        self.nodes
                            .iter()
                            .position(|n| &n.key == key)
                            .is_some_and(|j| completed[j])
                    }))
                .then_some(i)
            })
            .collect()
    }
    pub fn current(&self) -> Option<usize> {
        self.current_indices().first().copied()
    }
    /// A delegated signature counts as the actual person's signature when
    /// deciding both new assignments and whether a waiting task is actionable.
    pub fn independent_signer_conflict(&self, node: usize, user: i64) -> bool {
        self.steps.iter().any(|step| {
            step.acted_by_user_id == Some(user)
                && (step.stage == node
                    || self.nodes[node].independent_review
                    || self.nodes[step.stage].independent_review)
        })
    }
    pub fn activate(&mut self, now: DateTime<Utc>) -> Result<(), String> {
        for i in self.current_indices() {
            let node = &mut self.nodes[self.steps[i].stage];
            if node.started_at.is_empty() {
                node.started_at = now.to_rfc3339();
                let deadline = match node.deadline_mode {
                    crate::business_calendar::DeadlineMode::Natural => now
                        .checked_add_signed(chrono::Duration::hours(i64::from(node.due_hours)))
                        .ok_or("节点期限超出范围。")?,
                    crate::business_calendar::DeadlineMode::Working => self
                        .calendars
                        .get(&node.calendar_key)
                        .ok_or("工作时限缺少已冻结的日历。")?
                        .deadline(now, i64::from(node.due_hours) * 3600)?,
                };
                node.due_at = deadline.to_rfc3339();
            }
        }
        Ok(())
    }
    pub fn settle(&mut self, signed: usize) {
        let stage = self.steps[signed].stage;
        let outcome = self.node_outcome(stage);
        let abort = self.steps[signed].status == "Returned" || outcome == VoteOutcome::Rejected;
        for other in &mut self.steps {
            if other.status == "Waiting" {
                if abort {
                    other.status = "Cancelled".into();
                } else if other.stage == stage && outcome == VoteOutcome::Approved {
                    other.status = "Skipped".into();
                }
            }
        }
    }
    pub fn validate(&self, owner: i64) -> Result<(), String> {
        for node in &self.nodes {
            node.material_policy.validate()?;
        }
        if self.calendars.len() > 32 {
            return Err("流程日历快照超过上限。".into());
        }
        for calendar in self.calendars.values() {
            calendar.validate()?;
        }
        if self.definition_key.is_empty()
            || self.policy_version <= 0
            || self.nodes.is_empty()
            || self.nodes.len() > 32
            || self.steps.is_empty()
            || self.steps.len() > 200
            || !(1..=1000).contains(&self.generation)
        {
            return Err("流程实例定义、版本或节点数量无效。".into());
        }
        let mut keys = HashSet::new();
        if self
            .nodes
            .iter()
            .any(|n| n.key.is_empty() || !keys.insert(&n.key))
        {
            return Err("流程实例节点编号无效。".into());
        }
        let mut done = HashSet::new();
        loop {
            let size = done.len();
            for node in &self.nodes {
                if node.depends_on.iter().all(|id| done.contains(id)) {
                    done.insert(node.key.clone());
                }
            }
            if done.len() == self.nodes.len() {
                break;
            }
            if done.len() == size {
                return Err("流程实例存在循环或无效依赖。".into());
            }
        }
        let mut tasks = HashSet::new();
        let mut reviewers = HashSet::new();
        for step in &self.steps {
            if step
                .source_generation
                .is_some_and(|g| g == 0 || g >= self.generation)
                || (step.source_generation.is_some() && step.acted_by_user_id.is_none())
            {
                return Err("复用批准须引用本提交版本更早的真实签署。".into());
            }
            let Some(node) = self.nodes.get(step.stage) else {
                return Err("审批任务引用未知节点。".into());
            };
            if node.skipped
                || step.task_key.is_empty()
                || !tasks.insert(&step.task_key)
                || step.approver_user_id <= 0
                || step.approver_user_id == owner
                || !reviewers.insert((step.stage, step.approver_user_id))
                || step.decision != node.decision
            {
                return Err("审批任务身份、责任人或节点规则无效。".into());
            }
            match step.status.as_str() {
                "Waiting" | "Skipped" | "Cancelled" if step.acted_by_user_id.is_none() => {}
                "Approved" | "Rejected" | "Returned"
                    if step
                        .acted_by_user_id
                        .is_some_and(|id| id > 0 && id != owner) => {}
                _ => return Err("审批决定与实际办理人不一致。".into()),
            }
        }
        for (i, node) in self.nodes.iter().enumerate() {
            if !(1..=8760).contains(&node.due_hours) {
                return Err("节点时限须为 1–8760 小时。".into());
            }
            if node.deadline_mode == crate::business_calendar::DeadlineMode::Working
                && !self.calendars.contains_key(&node.calendar_key)
            {
                return Err("工作时限节点缺少冻结日历。".into());
            }
            node.revision_policy.validate()?;
            node.voting.validate(node.decision)?;
            let steps: Vec<_> = self.steps.iter().filter(|s| s.stage == i).collect();
            if !node.skipped && steps.is_empty() {
                return Err("有效节点没有审批任务。".into());
            }
            let approved = steps.iter().filter(|s| s.status == "Approved").count();
            let skipped = steps.iter().filter(|s| s.status == "Skipped").count();
            let threshold = if node.skipped {
                0
            } else {
                node.voting.required(node.decision, steps.len())?
            };
            if (skipped > 0
                && (approved < threshold || steps.iter().any(|s| s.status == "Waiting")))
                || (node.decision == ApprovalDecision::Any && approved > 1)
            {
                return Err("会签或或签任务关闭状态无效。".into());
            }
            let mut actual_signers = HashSet::new();
            if steps
                .iter()
                .filter_map(|s| s.acted_by_user_id)
                .any(|id| !actual_signers.insert(id))
            {
                return Err("同一节点的实际签署人只能计票一次，代理不能重复签署。".into());
            }
            if steps.iter().any(|s| s.acted_by_user_id.is_some())
                && !node.depends_on.iter().all(|key| {
                    self.nodes
                        .iter()
                        .position(|n| &n.key == key)
                        .is_some_and(|j| self.completed(j))
                })
            {
                return Err("前置节点未完成，不能办理后续节点。".into());
            }
            if node.independent_review
                && steps.iter().any(|step| {
                    self.steps.iter().any(|other| {
                        other.stage != i && other.approver_user_id == step.approver_user_id
                    })
                })
            {
                return Err("独立复核节点不能与其他节点使用相同责任人。".into());
            }
            if node.independent_review
                && steps
                    .iter()
                    .filter_map(|s| s.acted_by_user_id)
                    .any(|actor| {
                        self.steps
                            .iter()
                            .any(|s| s.stage != i && s.acted_by_user_id == Some(actor))
                    })
            {
                return Err("独立复核节点不能由其他节点的实际办理人兼任。".into());
            }
        }
        Ok(())
    }
}
pub fn delegation_window(start: &str, end: &str) -> Result<(DateTime<Utc>, DateTime<Utc>), String> {
    let parse = |s| {
        DateTime::parse_from_rfc3339(s)
            .map(|v| v.with_timezone(&Utc))
            .map_err(|_| "代理时间须包含时区。".to_owned())
    };
    let start = parse(start)?;
    let end = parse(end)?;
    if end <= start || end - start > chrono::Duration::days(366) {
        return Err("代理期限须大于零且不超过 366 天。".into());
    }
    Ok((start, end))
}
