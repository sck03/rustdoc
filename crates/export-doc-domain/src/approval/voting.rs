//! Integer voting rules shared by publication, simulation and execution.
use super::ApprovalDecision;
use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum RejectionPolicy {
    #[default]
    Immediate,
    Collect,
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalVoting {
    #[serde(default = "full_percentage")]
    pub approval_percentage: u8,
    #[serde(default)]
    pub rejection_policy: RejectionPolicy,
}

const fn full_percentage() -> u8 {
    100
}

impl Default for ApprovalVoting {
    fn default() -> Self {
        Self {
            approval_percentage: 100,
            rejection_policy: RejectionPolicy::Immediate,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum VoteOutcome {
    Pending,
    Approved,
    Rejected,
}

impl ApprovalVoting {
    pub fn validate(&self, decision: ApprovalDecision) -> Result<(), String> {
        if !(1..=100).contains(&self.approval_percentage)
            || (decision != ApprovalDecision::Ratio && self.approval_percentage != 100)
        {
            return Err("比例会签须设置 1–100 的整数百分比；其他通过方式不设置比例。".into());
        }
        Ok(())
    }

    pub fn required(
        &self,
        decision: ApprovalDecision,
        participants: usize,
    ) -> Result<usize, String> {
        self.validate(decision)?;
        if !(1..=200).contains(&participants) {
            return Err("有效签署人数须为 1–200 人。".into());
        }
        Ok(match decision {
            ApprovalDecision::All => participants,
            ApprovalDecision::Any => 1,
            ApprovalDecision::Ratio => {
                (participants * usize::from(self.approval_percentage)).div_ceil(100)
            }
        })
    }

    /// The denominator includes unsigned tasks closed as unnecessary. A refusal
    /// never counts as consent and never silently lowers the frozen threshold.
    pub fn outcome(
        &self,
        decision: ApprovalDecision,
        participants: usize,
        approved: usize,
        rejected: usize,
        waiting: usize,
    ) -> Result<VoteOutcome, String> {
        let required = self.required(decision, participants)?;
        if approved + rejected + waiting > participants {
            return Err("签署计数超出节点冻结人数。".into());
        }
        if rejected > 0 && self.rejection_policy == RejectionPolicy::Immediate {
            return Ok(VoteOutcome::Rejected);
        }
        if approved >= required {
            return Ok(VoteOutcome::Approved);
        }
        if waiting == 0 {
            Ok(VoteOutcome::Rejected)
        } else {
            Ok(VoteOutcome::Pending)
        }
    }
}
