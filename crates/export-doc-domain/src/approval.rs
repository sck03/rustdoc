//! Ordered approval snapshots and delegation dates, independent of storage/UI.
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApprovalMode {
    Single,
    DepartmentChain,
    Named,
    Configured,
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApprovalDecision {
    #[default]
    All,
    Any,
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    fn snapshot(stages: bool) -> ApprovalPlan {
        serde_json::from_value(json!({"mode":"Named","policyVersion":1,"steps":(1..=3).map(|id| {
            let mut step = json!({"approverUserId":id,"approverName":"reviewer","status":"Waiting","actedByUserId":null,"actedByName":"","actedAt":"","note":"","delegationKey":""});
            if stages { step["stage"] = json!(if id == 3 {1} else {0}); }
            step
        }).collect::<Vec<_>>()})).unwrap()
    }
    #[test]
    fn legacy_serial_and_parallel_snapshots_keep_their_order() {
        let serial = snapshot(false);
        serial.validate(4).unwrap();
        assert_eq!(serial.current_indices(), vec![0]);
        let mut parallel = snapshot(true);
        assert_eq!(parallel.current_indices(), vec![0, 1]);
        parallel.steps[1].status = "Approved".into();
        parallel.steps[1].acted_by_user_id = Some(2);
        parallel.validate(4).unwrap();
        assert_eq!(parallel.current_indices(), vec![0]);
        parallel.steps[2].status = "Approved".into();
        parallel.steps[2].acted_by_user_id = Some(3);
        assert!(parallel.validate(4).is_err());
        parallel.steps[2].status = "Waiting".into();
        parallel.steps[2].acted_by_user_id = None;
        parallel.steps[0].status = "Approved".into();
        parallel.steps[0].acted_by_user_id = Some(1);
        parallel.validate(4).unwrap();
        assert_eq!(parallel.current_indices(), vec![2]);
        parallel.steps[2].status = "Rejected".into();
        parallel.steps[2].acted_by_user_id = Some(3);
        assert!(parallel.current_indices().is_empty());
        assert!(parallel.validate(1).is_err());
    }
    #[test]
    fn any_sign_closes_only_its_node_without_fabricating_other_signatures() {
        let mut plan = snapshot(true);
        plan.steps[0].decision = ApprovalDecision::Any;
        plan.steps[1].decision = ApprovalDecision::Any;
        plan.validate(4).unwrap();
        plan.steps[1].status = "Approved".into();
        plan.steps[1].acted_by_user_id = Some(2);
        assert!(plan.validate(4).is_err());
        plan.close_any_stage(1);
        plan.validate(4).unwrap();
        assert_eq!(plan.steps[0].status, "Skipped");
        assert_eq!(plan.steps[0].acted_by_user_id, None);
        assert_eq!(plan.current_indices(), vec![2]);
        plan.steps[1].status = "Waiting".into();
        plan.steps[1].acted_by_user_id = None;
        assert!(plan.validate(4).is_err());
    }
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalStep {
    /// Omitted in previously issued serial plans; their array order remains authoritative.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub stage: Option<usize>,
    #[serde(default)]
    pub decision: ApprovalDecision,
    pub approver_user_id: i64,
    pub approver_name: String,
    pub status: String,
    pub acted_by_user_id: Option<i64>,
    pub acted_by_name: String,
    pub acted_at: String,
    pub note: String,
    pub delegation_key: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalPlan {
    pub mode: ApprovalMode,
    pub policy_version: i64,
    pub steps: Vec<ApprovalStep>,
    #[serde(default)]
    pub matched_branch: String,
}
impl ApprovalPlan {
    /// Any-sign closes only the other waiting steps of this node, without
    /// inventing signatures for people who did not approve it.
    pub fn close_any_stage(&mut self, approved: usize) {
        let step = &self.steps[approved];
        if step.status != "Approved" || step.decision != ApprovalDecision::Any {
            return;
        }
        let stage = step.stage.unwrap_or(approved);
        for (index, other) in self.steps.iter_mut().enumerate() {
            if other.stage.unwrap_or(index) == stage && other.status == "Waiting" {
                other.status = "Skipped".into();
            }
        }
    }
    pub fn current(&self) -> Option<usize> {
        self.current_indices().first().copied()
    }
    pub fn current_indices(&self) -> Vec<usize> {
        if self.steps.iter().any(|step| step.status == "Rejected") {
            return vec![];
        }
        let Some(first) = self.steps.iter().position(|step| step.status == "Waiting") else {
            return vec![];
        };
        let stage = self.steps[first].stage.unwrap_or(first);
        self.steps
            .iter()
            .enumerate()
            .filter_map(|(index, step)| {
                (step.status == "Waiting" && step.stage.unwrap_or(index) == stage).then_some(index)
            })
            .collect()
    }
    pub fn validate(&self, owner: i64) -> Result<(), String> {
        if self.mode == ApprovalMode::Single {
            return if self.steps.is_empty() {
                Ok(())
            } else {
                Err("单步审批不指定逐级步骤。".into())
            };
        }
        if self.steps.is_empty() || self.steps.len() > 50 {
            return Err("审批须有 1–50 位审批人。".into());
        }
        let mut seen = HashSet::new();
        let mut open_stage = None;
        let mut previous_stage = 0;
        let mut stages = std::collections::BTreeMap::new();
        for (index, step) in self.steps.iter().enumerate() {
            let entry =
                stages
                    .entry(step.stage.unwrap_or(index))
                    .or_insert((step.decision, 0, 0, 0));
            if entry.0 != step.decision {
                return Err("同一审批阶段的通过方式必须一致。".into());
            }
            entry.1 += usize::from(step.status == "Approved");
            entry.2 += usize::from(step.status == "Waiting");
            entry.3 += usize::from(step.status == "Skipped");
        }
        for (decision, approved, waiting, skipped) in stages.values() {
            if (*decision == ApprovalDecision::All && *skipped > 0)
                || (*decision == ApprovalDecision::Any
                    && (*approved > 1
                        || (*approved == 1 && *waiting > 0)
                        || (*skipped > 0 && *approved != 1)))
            {
                return Err("或签阶段只能有一位批准人，其余步骤须正确关闭。".into());
            }
        }
        for (index, step) in self.steps.iter().enumerate() {
            let stage = step.stage.unwrap_or(index);
            if stage >= 10 || (index > 0 && stage < previous_stage) {
                return Err("审批阶段须有序且不能超过十级。".into());
            }
            previous_stage = stage;
            if step.approver_user_id <= 0
                || step.approver_user_id == owner
                || !seen.insert(step.approver_user_id)
            {
                return Err("逐级审批人必须有效、互不重复且不能是申请人。".into());
            }
            match step.status.as_str() {
                "Approved"
                    if step.acted_by_user_id.is_some()
                        && open_stage.is_none_or(|open| stage == open) => {}
                "Rejected"
                    if step.acted_by_user_id.is_some()
                        && open_stage.is_none_or(|open| stage == open) =>
                {
                    open_stage = Some(stage);
                }
                "Waiting" if step.acted_by_user_id.is_none() => {
                    open_stage.get_or_insert(stage);
                }
                "Skipped"
                    if step.acted_by_user_id.is_none()
                        && step.decision == ApprovalDecision::Any => {}
                _ => return Err("审批步骤顺序或办理记录无效。".into()),
            }
        }
        Ok(())
    }
}
pub fn delegation_window(start: &str, end: &str) -> Result<(DateTime<Utc>, DateTime<Utc>), String> {
    let parse = |s| {
        DateTime::parse_from_rfc3339(s)
            .map(|v| v.with_timezone(&Utc))
            .map_err(|_| "代理起止时间须包含时区。".to_owned())
    };
    let start = parse(start)?;
    let end = parse(end)?;
    if end <= start || end - start > chrono::Duration::days(366) {
        return Err("代理有效期须大于零且不超过 366 天。".into());
    }
    Ok((start, end))
}
