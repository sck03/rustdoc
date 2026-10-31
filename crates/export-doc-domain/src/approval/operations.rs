//! Bounded runtime revisions. Published policies govern every exceptional action.
use super::{ApprovalDecision, ApprovalNode, ApprovalPlan, ApprovalStep, ApprovalVoting};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct RevisionPolicy {
    #[serde(default = "clock_stops")]
    pub pause_stops_clock: bool,
    #[serde(default)]
    pub allow_add_signers: bool,
    #[serde(default)]
    pub allow_remove_signers: bool,
    #[serde(default)]
    pub allow_return_to_node: bool,
    #[serde(default = "minimum")]
    pub minimum_signers: u16,
    #[serde(default)]
    pub protected_user_ids: Vec<i64>,
}
fn minimum() -> u16 {
    1
}
fn clock_stops() -> bool {
    true
}
impl Default for RevisionPolicy {
    fn default() -> Self {
        Self {
            pause_stops_clock: true,
            allow_add_signers: false,
            allow_remove_signers: false,
            allow_return_to_node: false,
            minimum_signers: 1,
            protected_user_ids: vec![],
        }
    }
}
impl RevisionPolicy {
    pub fn validate(&self) -> Result<(), String> {
        let mut unique = HashSet::new();
        if !(1..=200).contains(&self.minimum_signers)
            || self.protected_user_ids.len() > 50
            || self
                .protected_user_ids
                .iter()
                .any(|id| *id <= 0 || !unique.insert(id))
        {
            return Err("最低人数或必经责任人配置无效。".into());
        }
        Ok(())
    }
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum RevisionKind {
    AddBefore,
    AddAfter,
    AddParallel,
    RemoveSigner,
    ReturnToNode,
}

impl ApprovalPlan {
    /// The caller resolves candidates against current identity, unit and grants.
    /// This operation is atomic in memory as well as in the application transaction.
    pub fn revise(
        &mut self,
        kind: RevisionKind,
        target: &str,
        candidates: &[ApprovalStep],
        remove: &[i64],
        owner: i64,
    ) -> Result<(), String> {
        let mut next = self.clone();
        next.revise_in_place(kind, target, candidates, remove)?;
        next.validate(owner)?;
        *self = next;
        Ok(())
    }
    fn revise_in_place(
        &mut self,
        kind: RevisionKind,
        target: &str,
        candidates: &[ApprovalStep],
        remove: &[i64],
    ) -> Result<(), String> {
        if self.outcome() != "Pending" || self.generation >= 1000 {
            return Err("当前流程不能修订，或已达到执行代次上限。".into());
        }
        let index = self
            .nodes
            .iter()
            .position(|n| n.key == target)
            .ok_or("目标节点不存在。")?;
        let node = self.nodes[index].clone();
        let permitted = match kind {
            RevisionKind::ReturnToNode => node.revision_policy.allow_return_to_node,
            RevisionKind::RemoveSigner => node.revision_policy.allow_remove_signers,
            _ => node.revision_policy.allow_add_signers,
        };
        if !permitted {
            return Err("发布的节点政策没有开放此操作。".into());
        }
        let active = self
            .current_indices()
            .iter()
            .any(|i| self.steps[*i].stage == index);
        if kind != RevisionKind::ReturnToNode && !active {
            return Err("只可修订当前有效审批节点。".into());
        }
        if kind == RevisionKind::ReturnToNode
            && (node.skipped
                || !self
                    .steps
                    .iter()
                    .any(|s| s.stage == index && s.acted_by_user_id.is_some()))
        {
            return Err("指定退回须选择本实例已经执行过的审批节点。".into());
        }
        let generation = self.generation;
        self.generation += 1;
        for step in &mut self.steps {
            if step.acted_by_user_id.is_some() && step.source_generation.is_none() {
                step.source_generation = Some(generation);
            }
        }
        match kind {
            RevisionKind::ReturnToNode => {
                let mut affected = HashSet::from([target.to_owned()]);
                loop {
                    let previous = affected.len();
                    for node in &self.nodes {
                        if node.depends_on.iter().any(|key| affected.contains(key)) {
                            affected.insert(node.key.clone());
                        }
                    }
                    if affected.len() == previous {
                        break;
                    }
                }
                for (i, node) in self
                    .nodes
                    .iter_mut()
                    .enumerate()
                    .filter(|(_, n)| affected.contains(&n.key))
                {
                    node.started_at.clear();
                    node.due_at.clear();
                    for step in self.steps.iter_mut().filter(|s| s.stage == i) {
                        step.status = "Waiting".into();
                        step.acted_by_user_id = None;
                        step.acted_by_person_id = None;
                        step.verification_values.clear();
                        step.confirmed_review_points.clear();
                        step.acted_by_name.clear();
                        step.acted_by_context.clear();
                        step.acted_at.clear();
                        step.note.clear();
                        step.delegation_key.clear();
                        step.source_generation = None;
                        step.task_key = format!(
                            "{}:g{}:{}",
                            node.key, self.generation, step.approver_user_id
                        );
                    }
                }
            }
            RevisionKind::RemoveSigner => {
                if remove.is_empty()
                    || remove
                        .iter()
                        .any(|id| node.revision_policy.protected_user_ids.contains(id))
                    || node.independent_review
                {
                    return Err("不能减去必经责任人或独立复核任务。".into());
                }
                if remove.iter().any(|id| {
                    !self.steps.iter().any(|s| {
                        s.stage == index && s.approver_user_id == *id && s.status == "Waiting"
                    })
                }) {
                    return Err("只能减去本节点尚未签署的责任人。".into());
                }
                self.steps
                    .retain(|s| s.stage != index || !remove.contains(&s.approver_user_id));
                let members = self.steps.iter().filter(|s| s.stage == index).count();
                if members < usize::from(node.revision_policy.minimum_signers) {
                    return Err("减签后人数低于发布的最低人数。".into());
                }
                if self.completed(index) {
                    return Err("人数修订本身不能使节点自动通过。".into());
                }
            }
            _ => {
                if candidates.is_empty() || self.steps.len() + candidates.len() > 200 {
                    return Err("加签须选择合格责任人，累计任务最多 200 项。".into());
                }
                if kind == RevisionKind::AddBefore
                    && self
                        .steps
                        .iter()
                        .any(|s| s.stage == index && s.acted_by_user_id.is_some())
                {
                    return Err("前加签须在原节点首次签署前办理。".into());
                }
                let stage = if kind == RevisionKind::AddParallel {
                    index
                } else {
                    if self.nodes.len() >= 32 {
                        return Err("累计节点最多 32 个。".into());
                    }
                    let key = format!("revision-{}-{}", self.generation, self.nodes.len());
                    let dependencies = if kind == RevisionKind::AddBefore {
                        node.depends_on.clone()
                    } else {
                        vec![node.key.clone()]
                    };
                    if kind == RevisionKind::AddBefore {
                        self.nodes[index].depends_on = vec![key.clone()];
                        self.nodes[index].started_at.clear();
                        self.nodes[index].due_at.clear();
                    } else {
                        for next in &mut self.nodes {
                            for previous in &mut next.depends_on {
                                if previous == target {
                                    *previous = key.clone();
                                }
                            }
                        }
                    }
                    let stage = self.nodes.len();
                    self.nodes.push(ApprovalNode {
                        material_policy: node.material_policy.clone(),
                        deadline_mode: node.deadline_mode,
                        calendar_key: node.calendar_key.clone(),
                        key,
                        name: if kind == RevisionKind::AddBefore {
                            "前加签"
                        } else {
                            "后加签"
                        }
                        .into(),
                        depends_on: dependencies,
                        decision: ApprovalDecision::All,
                        voting: ApprovalVoting::default(),
                        review_points: node.review_points.clone(),
                        due_hours: node.due_hours,
                        skipped: false,
                        started_at: String::new(),
                        due_at: String::new(),
                        independent_review: node.independent_review,
                        revision_policy: RevisionPolicy::default(),
                    });
                    stage
                };
                for candidate in candidates {
                    if self.steps.iter().any(|s| {
                        s.stage == stage && s.approver_user_id == candidate.approver_user_id
                    }) {
                        return Err("同一节点不能重复添加同一人员。".into());
                    }
                    let mut step = candidate.clone();
                    step.stage = stage;
                    step.decision = self.nodes[stage].decision;
                    step.task_key = format!(
                        "{}:g{}:{}",
                        self.nodes[stage].key, self.generation, step.approver_user_id
                    );
                    step.status = "Waiting".into();
                    step.source_generation = None;
                    step.acted_by_user_id = None;
                    step.acted_by_person_id = None;
                    step.verification_values.clear();
                    step.confirmed_review_points.clear();
                    step.acted_by_name.clear();
                    step.acted_by_context.clear();
                    step.acted_at.clear();
                    step.note.clear();
                    step.delegation_key.clear();
                    self.steps.push(step);
                }
                if kind == RevisionKind::AddParallel && self.completed(index) {
                    return Err("加签修订不能自行触发节点通过。".into());
                }
            }
        }
        Ok(())
    }
}
