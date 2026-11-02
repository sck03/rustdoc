//! Fixed child contracts and explicit recovery preserve signed and physical facts.
use super::*;
use crate::approval_graph::{Compensation, OutputAggregation, Propagation, SubflowProgress};

impl ApprovalPlan {
    pub(super) fn cancellation_reaches_root(&self, child: &SubflowInstance) -> bool {
        let mut current = child;
        for _ in 0..4 {
            if current.policy.cancelled != Propagation::Propagate {
                return false;
            }
            if current.parent_key.is_empty() {
                return true;
            }
            let Some(parent) = self.subflows.iter().find(|s| s.key == current.parent_key) else {
                return false;
            };
            current = parent;
        }
        false
    }
    fn nearest_child(&self, stage: usize) -> Option<&SubflowInstance> {
        self.subflows
            .iter()
            .filter(|s| s.node_keys.contains(&self.nodes[stage].key))
            .max_by_key(|s| s.key.matches(':').count())
    }
    pub(super) fn child_stopped(&self, stage: usize) -> bool {
        self.subflows
            .iter()
            .filter(|s| s.node_keys.contains(&self.nodes[stage].key))
            .any(|s| {
                self.subflow_progress.get(&s.key).is_some_and(|p| {
                    p.state == NodeState::Blocked
                        || !matches!(p.outcome.as_str(), "Pending" | "Approved")
                })
            })
    }
    pub(super) fn local_failure_nodes(&self, stage: usize, status: &str) -> Option<Vec<String>> {
        let mut child = self.nearest_child(stage)?;
        for _ in 0..4 {
            if child.policy.propagation(status) != Propagation::Propagate {
                return Some(child.node_keys.clone());
            }
            if child.parent_key.is_empty() {
                return None;
            }
            child = self.subflows.iter().find(|s| s.key == child.parent_key)?;
        }
        None
    }
    pub(super) fn return_blocks_parent(&self, stage: usize) -> bool {
        self.subflows
            .iter()
            .filter(|s| s.node_keys.contains(&self.nodes[stage].key))
            .all(|s| {
                s.policy.returned == Propagation::Propagate
                    && self
                        .subflow_progress
                        .get(&s.key)
                        .is_none_or(|p| p.state != NodeState::Blocked && p.outcome != "Ignored")
            })
    }
    pub fn refresh_subflows(&mut self) -> Result<(), String> {
        let previous = std::mem::take(&mut self.subflow_progress);
        let mut children = self.subflows.clone();
        children.sort_by_key(|s| std::cmp::Reverse(s.key.matches(':').count()));
        for child in children {
            let indices = child
                .node_keys
                .iter()
                .filter_map(|key| {
                    self.nodes
                        .iter()
                        .position(|n| &n.key == key && n.phase == child.phase)
                })
                .collect::<Vec<_>>();
            if indices.is_empty() {
                return Err("子流程缺少声明阶段的执行节点。".into());
            }
            let mut progress = SubflowProgress {
                outcome: "Pending".into(),
                ..Default::default()
            };
            if let Some(old) = previous.get(&child.key) {
                progress.control_outcome = old.control_outcome.clone();
                progress.compensation_completed = old.compensation_completed;
            }
            let own = indices
                .iter()
                .copied()
                .filter(|i| {
                    self.nodes[*i].kind != NodeKind::Subflow
                        && self.nearest_child(*i).is_none_or(|s| s.key == child.key)
                })
                .collect::<Vec<_>>();
            let nested_failure = |outcome: &str| {
                self.subflows
                    .iter()
                    .filter(|s| s.parent_key == child.key)
                    .any(|s| {
                        self.subflow_progress.get(&s.key).is_some_and(|p| {
                            p.outcome == outcome
                                && s.policy.propagation(outcome) == Propagation::Propagate
                        })
                    })
            };
            let nested_blocked = self
                .subflows
                .iter()
                .filter(|s| s.parent_key == child.key)
                .any(|s| {
                    self.subflow_progress.get(&s.key).is_some_and(|p| {
                        p.state == NodeState::Blocked
                            && (p.outcome == "Pending"
                                || s.policy.propagation(&p.outcome) != Propagation::Propagate)
                    })
                });
            let returned = self
                .steps
                .iter()
                .any(|s| own.contains(&s.stage) && s.status == "Returned")
                || nested_failure("Returned");
            let rejected = own.iter().any(|i| {
                self.raw_node_outcome(*i) == VoteOutcome::Rejected
                    && !self
                        .steps
                        .iter()
                        .any(|s| s.stage == *i && s.status == "Returned")
                    && self.nodes[*i].state != NodeState::Cancelled
            }) || nested_failure("Rejected");
            let cancelled = own
                .iter()
                .any(|i| self.nodes[*i].state == NodeState::Cancelled)
                || nested_failure("Cancelled");
            let own_exception = own.iter().any(|i| {
                self.nodes[*i].state == NodeState::Blocked && !self.nodes[*i].error_code.is_empty()
            }) || nested_failure("Exception");
            let failure = match progress.control_outcome.as_str() {
                "Cancelled" => Some(("Cancelled", child.policy.cancelled)),
                "Exception" => Some(("Exception", child.policy.exception)),
                "" if returned => Some(("Returned", child.policy.returned)),
                "" if cancelled => Some(("Cancelled", child.policy.cancelled)),
                "" if rejected => Some(("Rejected", child.policy.rejected)),
                "" if own_exception => Some(("Exception", child.policy.exception)),
                "" => None,
                _ => return Err("子流程控制结果无效。".into()),
            };
            if let Some((outcome, policy)) = failure {
                progress.outcome = outcome.into();
                progress.state = match policy {
                    Propagation::Ignore => {
                        progress.outcome = "Ignored".into();
                        NodeState::Completed
                    }
                    Propagation::Propagate if outcome != "Exception" => NodeState::Completed,
                    _ => NodeState::Blocked,
                };
                if progress.state == NodeState::Blocked {
                    progress.error_code = format!("Subflow{outcome}");
                }
            } else if nested_blocked {
                progress.state = NodeState::Blocked;
                progress.error_code = "SubflowWaitingForChild".into();
            } else {
                let completed = self.completion_in(Some(&child.key));
                if indices.iter().all(|i| self.nodes[*i].skipped) {
                    progress.state = NodeState::Completed;
                    progress.outcome = "Ignored".into();
                } else if indices.iter().all(|i| completed[*i]) {
                    match self.child_outputs(&child) {
                        Ok(outputs) => {
                            progress.state = NodeState::Completed;
                            progress.outcome = "Approved".into();
                            progress.outputs = outputs;
                        }
                        Err(cause) => {
                            progress.state = NodeState::Blocked;
                            progress.outcome = "Exception".into();
                            progress.error_code = cause;
                        }
                    }
                } else if indices
                    .iter()
                    .any(|i| !self.nodes[*i].started_at.is_empty())
                {
                    progress.state = NodeState::Active;
                }
            }
            if let Some(boundary) = self.nodes.iter_mut().find(|n| n.key == child.boundary_key) {
                boundary.state = if progress.state == NodeState::Blocked {
                    NodeState::Blocked
                } else if progress.state == NodeState::Completed
                    && matches!(progress.outcome.as_str(), "Approved" | "Ignored")
                {
                    NodeState::Completed
                } else {
                    NodeState::Pending
                };
                boundary.error_code = progress.error_code.clone();
            }
            self.subflow_progress.insert(child.key.clone(), progress);
        }
        Ok(())
    }
    pub fn cancel_child(&mut self, key: &str) -> Result<(), String> {
        let child = self
            .subflows
            .iter()
            .find(|s| s.key == key)
            .ok_or("子流程不存在。")?
            .clone();
        if self
            .subflow_progress
            .get(key)
            .is_some_and(|p| p.state == NodeState::Completed)
        {
            return Err("已结束子流程不能取消。".into());
        }
        for (i, node) in self
            .nodes
            .iter_mut()
            .enumerate()
            .filter(|(_, n)| child.node_keys.contains(&n.key))
        {
            if matches!(
                node.state,
                NodeState::Pending | NodeState::Active | NodeState::Blocked
            ) {
                node.state = NodeState::Cancelled;
            }
            for step in self
                .steps
                .iter_mut()
                .filter(|s| s.stage == i && s.status == "Waiting")
            {
                step.status = "Cancelled".into();
            }
        }
        self.subflow_progress
            .entry(key.into())
            .or_default()
            .control_outcome = "Cancelled".into();
        self.refresh_subflows()
    }
    pub fn mark_child_compensated(&mut self, key: &str) -> Result<(), String> {
        let child = self
            .subflows
            .iter()
            .find(|s| s.key == key)
            .ok_or("子流程不存在。")?;
        if child.policy.compensation == Compensation::None {
            return Err("发布合同未声明补偿操作。".into());
        }
        let progress = self
            .subflow_progress
            .get_mut(key)
            .ok_or("子流程状态缺失。")?;
        if matches!(progress.outcome.as_str(), "Pending" | "Approved") {
            return Err("正常子流程不需要补偿。".into());
        }
        progress.compensation_completed = true;
        Ok(())
    }
    pub fn retry_child(&mut self, key: &str, owner: i64, now: DateTime<Utc>) -> Result<(), String> {
        let child = self
            .subflows
            .iter()
            .find(|s| s.key == key)
            .ok_or("子流程不存在。")?
            .clone();
        let progress = self.subflow_progress.get(key).ok_or("子流程状态缺失。")?;
        if matches!(
            progress.outcome.as_str(),
            "Pending" | "Approved" | "Ignored"
        ) || self.generation >= 1000
        {
            return Err("仅失败子流程可以重试，且不能超过执行代次上限。".into());
        }
        if child.node_keys.iter().any(|key| {
            self.nodes
                .iter()
                .any(|n| n.key == *key && n.execution.is_some())
        }) && !progress.compensation_completed
        {
            return Err("已有实际办理结果，须先完成合同声明的补偿。".into());
        }
        let mut next = self.clone();
        let old = next.generation;
        next.generation += 1;
        let mut affected = HashSet::from([child.boundary_key.clone()]);
        affected.extend(child.node_keys.iter().cloned());
        loop {
            let count = affected.len();
            for node in &next.nodes {
                if node.phase == child.phase && node.depends_on.iter().any(|k| affected.contains(k))
                {
                    affected.insert(node.key.clone());
                }
            }
            if count == affected.len() {
                break;
            }
        }
        for (i, node) in next.nodes.iter_mut().enumerate() {
            let reset = affected.contains(&node.key);
            if reset {
                node.state = NodeState::Pending;
                node.error_code.clear();
                node.execution = None;
                node.started_at.clear();
                node.due_at.clear();
            }
            for step in next.steps.iter_mut().filter(|s| s.stage == i) {
                if reset {
                    *step = ApprovalStep {
                        mandatory: step.mandatory,
                        task_key: format!(
                            "{}:g{}:{}",
                            node.key, next.generation, step.approver_user_id
                        ),
                        stage: i,
                        decision: step.decision,
                        approver_user_id: step.approver_user_id,
                        approver_person_id: step.approver_person_id,
                        approver_name: step.approver_name.clone(),
                        approver_context: step.approver_context.clone(),
                        origin: step.origin.clone(),
                        status: "Waiting".into(),
                        ..Default::default()
                    };
                } else if step.acted_by_user_id.is_some() && step.source_generation.is_none() {
                    step.source_generation = Some(old);
                }
            }
        }
        next.subflow_progress.retain(|k, _| {
            !next
                .subflows
                .iter()
                .any(|s| s.key == *k && s.node_keys.iter().any(|n| affected.contains(n)))
        });
        next.refresh_subflows()?;
        next.activate(now)?;
        next.validate(owner)?;
        *self = next;
        Ok(())
    }
    fn child_outputs(
        &self,
        child: &SubflowInstance,
    ) -> Result<std::collections::BTreeMap<String, serde_json::Value>, String> {
        let mut outputs = std::collections::BTreeMap::new();
        for mapping in &child.outputs {
            let stage = self
                .nodes
                .iter()
                .position(|n| n.key == mapping.node_key)
                .ok_or("SubflowOutputNodeMissing")?;
            let mut values = self
                .steps
                .iter()
                .filter(|s| s.stage == stage && s.status == "Approved")
                .filter_map(|s| s.verification_values.get(&mapping.verification_key))
                .collect::<Vec<_>>();
            if let Some(execution) = self.nodes[stage]
                .execution
                .as_ref()
                .filter(|_| self.nodes[stage].state == NodeState::Completed)
            {
                if let Some(value) = execution.verification_values.get(&mapping.verification_key) {
                    values.push(value);
                }
            }
            if values.is_empty() {
                if mapping.required {
                    return Err("SubflowOutputMissing".into());
                }
                continue;
            }
            let value = match mapping.aggregation {
                OutputAggregation::AllEqual => {
                    if values.iter().any(|v| *v != values[0]) {
                        return Err("SubflowOutputConflict".into());
                    }
                    values[0].clone()
                }
                OutputAggregation::Sum => {
                    let total =
                        values
                            .iter()
                            .try_fold(rust_decimal::Decimal::ZERO, |total, value| {
                                let amount = value
                                    .as_str()
                                    .ok_or("SubflowOutputType")?
                                    .parse::<rust_decimal::Decimal>()
                                    .map_err(|_| "SubflowOutputType")?;
                                total.checked_add(amount).ok_or("SubflowOutputOverflow")
                            })?;
                    serde_json::json!(total.normalize().to_string())
                }
                OutputAggregation::AllTrue | OutputAggregation::AnyTrue => {
                    let flags = values
                        .iter()
                        .map(|v| v.as_bool().ok_or("SubflowOutputType"))
                        .collect::<Result<Vec<_>, _>>()?;
                    serde_json::json!(if mapping.aggregation == OutputAggregation::AllTrue {
                        flags.into_iter().all(|v| v)
                    } else {
                        flags.into_iter().any(|v| v)
                    })
                }
            };
            outputs.insert(mapping.target.clone(), value);
        }
        Ok(outputs)
    }
}
