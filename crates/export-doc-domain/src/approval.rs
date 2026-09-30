//! Ordered approval snapshots and delegation dates, independent of storage/UI.
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApprovalMode {
    Single,
    DepartmentChain,
    Named,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalStep {
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
}
impl ApprovalPlan {
    pub fn current(&self) -> Option<usize> {
        self.steps.iter().position(|s| s.status == "Waiting")
    }
    pub fn validate(&self, owner: i64) -> Result<(), String> {
        if self.mode == ApprovalMode::Single {
            return if self.steps.is_empty() {
                Ok(())
            } else {
                Err("单步审批不指定逐级步骤。".into())
            };
        }
        if self.steps.is_empty() || self.steps.len() > 10 {
            return Err("审批须有 1–10 个步骤。".into());
        }
        let mut seen = HashSet::new();
        let mut phase = 0;
        for step in &self.steps {
            if step.approver_user_id <= 0
                || step.approver_user_id == owner
                || !seen.insert(step.approver_user_id)
            {
                return Err("逐级审批人必须有效、互不重复且不能是申请人。".into());
            }
            match step.status.as_str() {
                "Approved" if phase == 0 && step.acted_by_user_id.is_some() => {}
                "Rejected" if phase == 0 && step.acted_by_user_id.is_some() => phase = 2,
                "Waiting" if step.acted_by_user_id.is_none() => {
                    if phase == 0 {
                        phase = 1;
                    }
                }
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
