//! Runtime graph snapshots. Decisions belong to explicit nodes and tasks.
use crate::approval_graph::{
    ActorBinding, AssignmentOrigin, Escalation, HandlingAction, NodeExecution, NodeKind, NodeState,
    Phase, SubflowProgress, SystemAction,
};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
mod voting;
pub use voting::{ApprovalVoting, RejectionPolicy, VoteOutcome};
mod materials;
mod operations;
mod participants;
mod subflows;
pub use materials::{MaterialPolicy, VerificationField, VerificationKind};
pub use operations::{RevisionKind, RevisionPolicy};
pub use participants::Participant;
fn initial_generation() -> u32 {
    1
}
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApprovalDecision {
    #[default]
    All,
    Any,
    Ratio,
}
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalNode {
    #[serde(default)]
    pub kind: NodeKind,
    #[serde(default)]
    pub pair_key: String,
    #[serde(default)]
    pub phase: Phase,
    #[serde(default)]
    pub state: NodeState,
    #[serde(default)]
    pub actors: Vec<ActorBinding>,
    #[serde(default)]
    pub handling_action: HandlingAction,
    #[serde(default)]
    pub system_action: SystemAction,
    #[serde(default)]
    pub escalations: Vec<Escalation>,
    #[serde(default)]
    pub escalation_targets: Vec<Vec<ActorBinding>>,
    #[serde(default)]
    pub escalation_cursor: u16,
    #[serde(default)]
    pub reminder_level: u16,
    #[serde(default)]
    pub next_escalation_at: String,
    #[serde(default)]
    pub execution: Option<NodeExecution>,
    #[serde(default)]
    pub error_code: String,
    #[serde(default)]
    pub allow_administration: bool,
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
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalStep {
    #[serde(default)]
    pub mandatory: bool,
    #[serde(default)]
    pub origin: AssignmentOrigin,
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
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SubflowInstance {
    pub key: String,
    pub definition_key: String,
    pub version: i64,
    pub definition_id: i64,
    pub node_keys: Vec<String>,
    #[serde(default)]
    pub inputs: Vec<crate::approval_graph::InputMapping>,
    #[serde(default)]
    pub outputs: Vec<crate::approval_graph::OutputMapping>,
    #[serde(default)]
    pub policy: crate::approval_graph::SubflowPolicy,
    #[serde(default)]
    pub input_snapshot: serde_json::Value,
    #[serde(default)]
    pub parent_key: String,
    #[serde(default)]
    pub phase: Phase,
    #[serde(default)]
    pub boundary_key: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalPlan {
    #[serde(default)]
    pub participants: Vec<Participant>,
    #[serde(default)]
    pub subflow_progress: std::collections::BTreeMap<String, SubflowProgress>,
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
    fn node_outcome(&self, node: usize) -> VoteOutcome {
        self.effective_node_outcome(node, None)
    }
    fn effective_node_outcome(&self, node: usize, evaluating_child: Option<&str>) -> VoteOutcome {
        let key = &self.nodes[node].key;
        for child in &self.subflows {
            if (child.node_keys.contains(key) || child.boundary_key == *key)
                && evaluating_child.is_none_or(|key| child.key != key)
            {
                if let Some(progress) = self.subflow_progress.get(&child.key) {
                    if progress.outcome == "Ignored" {
                        return VoteOutcome::Approved;
                    }
                    if progress.state == NodeState::Blocked {
                        return VoteOutcome::Pending;
                    }
                }
            }
        }
        self.raw_node_outcome(node)
    }
    fn raw_node_outcome(&self, node: usize) -> VoteOutcome {
        let Some(definition) = self.nodes.get(node) else {
            return VoteOutcome::Rejected;
        };
        if definition.skipped {
            return VoteOutcome::Approved;
        }
        if definition.kind != NodeKind::Approval {
            return if definition.state == NodeState::Completed {
                VoteOutcome::Approved
            } else {
                VoteOutcome::Pending
            };
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
        if self.subflows.iter().any(|child| {
            child.phase == Phase::Decision
                && self
                    .subflow_progress
                    .get(&child.key)
                    .is_some_and(|p| p.outcome == "Cancelled")
                && self.cancellation_reaches_root(child)
        }) {
            return "Cancelled";
        }
        if self.steps.iter().any(|s| {
            s.status == "Returned"
                && self.nodes[s.stage].phase == Phase::Decision
                && self.return_blocks_parent(s.stage)
        }) {
            return "Returned";
        }
        if self.nodes.iter().enumerate().any(|(i, node)| {
            node.phase == Phase::Decision && self.node_outcome(i) == VoteOutcome::Rejected
        }) {
            return "Rejected";
        }
        if self
            .completion()
            .iter()
            .enumerate()
            .filter(|(i, _)| self.nodes[*i].phase == Phase::Decision)
            .all(|(_, done)| *done)
        {
            "Approved"
        } else {
            "Pending"
        }
    }
    fn completion(&self) -> Vec<bool> {
        self.completion_in(None)
    }
    fn completion_in(&self, evaluating_child: Option<&str>) -> Vec<bool> {
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
                            && self.effective_node_outcome(index, evaluating_child)
                                == VoteOutcome::Approved,
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
        let outcome = self.outcome();
        if !self.suspended_at.is_empty() || !matches!(outcome, "Pending" | "Approved") {
            return vec![];
        }
        let completed = self.completion();
        self.steps
            .iter()
            .enumerate()
            .filter_map(|(i, s)| {
                (s.status == "Waiting"
                    && self.nodes[s.stage].kind == NodeKind::Approval
                    && self.nodes[s.stage].state != NodeState::Blocked
                    && !self.child_stopped(s.stage)
                    && ((outcome == "Pending" && self.nodes[s.stage].phase == Phase::Decision)
                        || (outcome == "Approved"
                            && self.nodes[s.stage].phase == Phase::Fulfillment))
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
        self.signer_conflict(node, user, None)
    }
    pub fn signer_conflict(&self, node: usize, user: i64, person: Option<i64>) -> bool {
        self.steps.iter().any(|step| {
            (step.acted_by_user_id == Some(user)
                || step.acted_by_user_id.is_some()
                    && person.is_some_and(|person| {
                        step.acted_by_person_id == Some(person)
                            || step.approver_person_id == Some(person)
                    }))
                && (step.stage == node
                    || self.nodes[node].independent_review
                    || self.nodes[step.stage].independent_review)
        }) || self.participant_conflict(node, user, person)
            || self
                .nodes
                .iter()
                .filter(|other| other.key != self.nodes[node].key)
                .any(|other| {
                    other.execution.as_ref().is_some_and(|execution| {
                        (execution.user_id == user
                            || person.is_some() && execution.person_id == person)
                            && (self.nodes[node].independent_review || other.independent_review)
                    })
                })
    }
    pub fn activate(&mut self, now: DateTime<Utc>) -> Result<(), String> {
        if !self.suspended_at.is_empty() {
            return Ok(());
        }
        for _ in 0..self.nodes.len() + 1 {
            let phase = if self.outcome() == "Pending" {
                Phase::Decision
            } else if self.outcome() == "Approved" {
                Phase::Fulfillment
            } else {
                return Ok(());
            };
            let completed = self.completion();
            let ready = self
                .nodes
                .iter()
                .enumerate()
                .filter_map(|(i, n)| {
                    (n.phase == phase
                        && !n.skipped
                        && !self.child_stopped(i)
                        && matches!(n.state, NodeState::Pending | NodeState::Active)
                        && n.depends_on.iter().all(|key| {
                            self.nodes
                                .iter()
                                .position(|n| &n.key == key)
                                .is_some_and(|j| completed[j])
                        }))
                    .then_some(i)
                })
                .collect::<Vec<_>>();
            let mut changed = false;
            for i in ready {
                let node = &mut self.nodes[i];
                if node.started_at.is_empty() {
                    node.started_at = now.to_rfc3339_opts(chrono::SecondsFormat::Nanos, true);
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
                    changed = true;
                }
                let next = if matches!(
                    node.kind,
                    NodeKind::Start
                        | NodeKind::End
                        | NodeKind::Fork
                        | NodeKind::Join
                        | NodeKind::Exclusive
                        | NodeKind::Copy
                        | NodeKind::Subflow
                ) {
                    NodeState::Completed
                } else {
                    NodeState::Active
                };
                if node.state != next {
                    node.state = next;
                    changed = true;
                }
            }
            if !changed {
                break;
            }
        }
        self.refresh_escalation_deadlines()
    }
    pub fn refresh_escalation_deadlines(&mut self) -> Result<(), String> {
        for node in &mut self.nodes {
            if node.due_at.is_empty()
                || usize::from(node.escalation_cursor) >= node.escalations.len()
            {
                node.next_escalation_at.clear();
                continue;
            }
            let due = DateTime::parse_from_rfc3339(&node.due_at)
                .map_err(|_| "节点期限损坏。")?
                .with_timezone(&Utc);
            let hours =
                i64::from(node.escalations[usize::from(node.escalation_cursor)].after_hours);
            let at = if node.deadline_mode == crate::business_calendar::DeadlineMode::Working {
                self.calendars
                    .get(&node.calendar_key)
                    .ok_or("升级日历缺失。")?
                    .deadline(due, hours * 3600)?
            } else {
                due.checked_add_signed(chrono::Duration::hours(hours))
                    .ok_or("升级期限超出范围。")?
            };
            node.next_escalation_at = at.to_rfc3339_opts(chrono::SecondsFormat::Nanos, true);
        }
        Ok(())
    }
    pub fn settle(&mut self, signed: usize) {
        let stage = self.steps[signed].stage;
        let outcome = self.raw_node_outcome(stage);
        let abort = self.steps[signed].status == "Returned" || outcome == VoteOutcome::Rejected;
        if outcome == VoteOutcome::Approved {
            self.nodes[stage].state = NodeState::Completed;
        }
        if abort {
            self.nodes[stage].state = NodeState::Blocked;
        }
        let local = self.local_failure_nodes(stage, &self.steps[signed].status);
        for other in &mut self.steps {
            if other.status == "Waiting" {
                if abort
                    && self.nodes[other.stage].phase == self.nodes[stage].phase
                    && local
                        .as_ref()
                        .is_none_or(|keys| keys.contains(&self.nodes[other.stage].key))
                {
                    other.status = "Cancelled".into();
                } else if other.stage == stage && outcome == VoteOutcome::Approved {
                    other.status = "Skipped".into();
                }
            }
        }
    }
    pub fn validate(&self, owner: i64) -> Result<(), String> {
        if self.participants.len() > 200
            || self.participants.iter().any(|p| {
                p.user_id <= 0
                    || p.generation == 0
                    || p.generation > self.generation
                    || !self.nodes.iter().any(|n| {
                        n.key == p.node_key && (p.user_id != owner || n.kind != NodeKind::Approval)
                    })
            })
        {
            return Err("实际办理参与人索引无效。".into());
        }
        for node in &self.nodes {
            node.material_policy.validate()?;
            if node.escalations.len() > 4
                || usize::from(node.escalation_cursor) > node.escalations.len()
                || node.reminder_level > 1
            {
                return Err("运行升级级次无效。".into());
            }
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
        if !self
            .nodes
            .iter()
            .any(|n| n.kind == NodeKind::Approval && n.phase == Phase::Decision && !n.skipped)
        {
            return Err("流程必须保留至少一个实际审批节点，不能由系统动作自动批准。".into());
        }
        if self.steps.len()
            + self
                .nodes
                .iter()
                .map(|n| n.actors.len() + n.escalation_targets.iter().map(Vec::len).sum::<usize>())
                .sum::<usize>()
            + self.notify_user_ids.len()
            > 200
        {
            return Err("审批、抄送和办理责任任务累计超过二百项。".into());
        }
        let mut keys = HashSet::new();
        if self
            .nodes
            .iter()
            .any(|n| n.key.is_empty() || !keys.insert(&n.key))
        {
            return Err("流程实例节点编号无效。".into());
        }
        let mut children = HashSet::new();
        if self.subflows.len() > 32
            || self.subflows.iter().any(|s| {
                !children.insert(&s.key)
                    || s.node_keys.is_empty()
                    || s.node_keys.len() > 32
                    || !self.nodes.iter().any(|n| n.key == s.boundary_key)
                    || s.node_keys.iter().any(|key| {
                        !self
                            .nodes
                            .iter()
                            .any(|n| n.key == *key && n.phase == s.phase)
                    })
            })
        {
            return Err("固定子流程绑定或节点范围无效。".into());
        }
        for child in &self.subflows {
            let mut current = child;
            let mut seen = HashSet::new();
            while !current.parent_key.is_empty() {
                if !seen.insert(&current.key) || seen.len() >= 4 {
                    return Err("子流程父关系存在递归或超过四层。".into());
                }
                current = self
                    .subflows
                    .iter()
                    .find(|s| s.key == current.parent_key)
                    .ok_or("子流程父实例缺失。")?;
            }
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
            if node.kind != NodeKind::Approval
                || node.skipped
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
            if !node.skipped && node.kind == NodeKind::Approval && steps.is_empty() {
                return Err("有效节点没有审批任务。".into());
            }
            if !node.skipped
                && node.kind == NodeKind::Approval
                && steps.len() < usize::from(node.revision_policy.minimum_signers)
            {
                return Err("节点未达到最低人数。".into());
            }
            let approved = steps.iter().filter(|s| s.status == "Approved").count();
            let skipped = steps.iter().filter(|s| s.status == "Skipped").count();
            let threshold = if node.skipped || node.kind != NodeKind::Approval {
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
            let mut people = HashSet::new();
            if steps
                .iter()
                .filter_map(|s| s.approver_person_id)
                .any(|id| !people.insert(id))
            {
                return Err("同一节点只能分派一个自然人的一个有效身份。".into());
            }
            let mut people = HashSet::new();
            if steps
                .iter()
                .filter_map(|s| s.acted_by_person_id)
                .any(|id| !people.insert(id))
            {
                return Err("同一自然人不能通过换绑账号在本节点重复计票。".into());
            }
            if steps.iter().any(|s| s.acted_by_user_id.is_some())
                && !node.depends_on.iter().all(|key| {
                    self.nodes
                        .iter()
                        .position(|n| &n.key == key)
                        .is_some_and(|j| self.historical_completed(j))
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
    fn historical_completed(&self, node: usize) -> bool {
        let mut done = vec![false; self.nodes.len()];
        for _ in 0..self.nodes.len() {
            for (i, n) in self.nodes.iter().enumerate() {
                if (self.raw_node_outcome(i) == VoteOutcome::Approved
                    || self.subflows.iter().any(|s| {
                        (s.node_keys.contains(&n.key) || s.boundary_key == n.key)
                            && self
                                .subflow_progress
                                .get(&s.key)
                                .is_some_and(|p| p.outcome == "Ignored")
                    }))
                    && n.depends_on.iter().all(|key| {
                        self.nodes
                            .iter()
                            .position(|n| &n.key == key)
                            .is_some_and(|j| done[j])
                    })
                {
                    done[i] = true;
                }
            }
        }
        done.get(node).copied().unwrap_or(false)
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
