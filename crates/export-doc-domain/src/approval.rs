//! Runtime graph snapshots. Decisions belong to explicit nodes and tasks.
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApprovalDecision {
    All,
    Any,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalNode {
    pub key: String,
    pub name: String,
    #[serde(default)]
    pub review_points: Vec<String>,
    pub depends_on: Vec<String>,
    pub decision: ApprovalDecision,
    pub due_hours: u32,
    pub skipped: bool,
    pub started_at: String,
    pub due_at: String,
    pub independent_review: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalStep {
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
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalPlan {
    pub definition_key: String,
    pub policy_version: i64,
    pub nodes: Vec<ApprovalNode>,
    pub steps: Vec<ApprovalStep>,
    pub notify_user_ids: Vec<i64>,
}
impl ApprovalPlan {
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
                    done[index] =
                        Some(
                            dependencies.into_iter().all(|d| d)
                                && (node.skipped
                                    || self.steps.iter().filter(|s| s.stage == index).all(|s| {
                                        matches!(s.status.as_str(), "Approved" | "Skipped")
                                    })),
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
        if self
            .steps
            .iter()
            .any(|s| matches!(s.status.as_str(), "Rejected" | "Returned" | "Cancelled"))
        {
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
    pub fn activate(&mut self, now: DateTime<Utc>) {
        for i in self.current_indices() {
            let node = &mut self.nodes[self.steps[i].stage];
            if node.started_at.is_empty() {
                node.started_at = now.to_rfc3339();
                node.due_at =
                    (now + chrono::Duration::hours(i64::from(node.due_hours))).to_rfc3339();
            }
        }
    }
    pub fn close_any_stage(&mut self, approved: usize) {
        let step = &self.steps[approved];
        if step.status != "Approved" || step.decision != ApprovalDecision::Any {
            return;
        }
        let stage = step.stage;
        for other in &mut self.steps {
            if other.stage == stage && other.status == "Waiting" {
                other.status = "Skipped".into();
            }
        }
    }
    pub fn validate(&self, owner: i64) -> Result<(), String> {
        if self.definition_key.is_empty()
            || self.policy_version <= 0
            || self.nodes.is_empty()
            || self.nodes.len() > 32
            || self.steps.is_empty()
            || self.steps.len() > 200
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
            let steps: Vec<_> = self.steps.iter().filter(|s| s.stage == i).collect();
            if !node.skipped && steps.is_empty() {
                return Err("有效节点没有审批任务。".into());
            }
            let approved = steps.iter().filter(|s| s.status == "Approved").count();
            let skipped = steps.iter().filter(|s| s.status == "Skipped").count();
            if (node.decision == ApprovalDecision::All && skipped > 0)
                || (node.decision == ApprovalDecision::Any
                    && (approved > 1
                        || (skipped > 0 && approved != 1)
                        || (approved == 1 && steps.iter().any(|s| s.status == "Waiting"))))
            {
                return Err("会签或或签任务关闭状态无效。".into());
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
