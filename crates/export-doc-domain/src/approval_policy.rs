//! Configurable approval sources and exact, currency-specific amount conditions.
use crate::approval::ApprovalMode;
use rust_decimal::Decimal;
use serde::{Deserialize, Serialize};
use std::{collections::HashSet, str::FromStr};

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ApproverSource {
    Named,
    DepartmentManager,
    LineManager,
    FunctionalManagers,
    CompanyExecutive,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalStage {
    pub source: ApproverSource,
    pub approver_user_ids: Vec<i64>,
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test]
    fn exact_currency_ranges_have_no_rounding_overlap_or_boundary_gaps() {
        let stages = json!([{"source":"CompanyExecutive","approverUserIds":[]}]);
        let mut rule: ApprovalRule = serde_json::from_value(json!({"kind":"purchase","mode":"Single","approverUserIds":[],"amountBranches":[
            {"key":"low","name":"低额","currency":"CNY","minimum":"0","maximum":"1000.000001","stages":stages},
            {"key":"high","name":"高额","currency":"CNY","minimum":"1000.000001","maximum":null,"stages":stages}
        ]})).unwrap();
        rule.validate().unwrap();
        assert_eq!(
            rule.matching_branch("CNY", "1000.000000")
                .unwrap()
                .unwrap()
                .key,
            "low"
        );
        assert_eq!(
            rule.matching_branch("CNY", "1000.000001")
                .unwrap()
                .unwrap()
                .key,
            "high"
        );
        assert!(
            rule.matching_branch("USD", "1000.000001")
                .unwrap()
                .is_none()
        );
        rule.amount_branches[1].minimum = "999.99".into();
        assert!(rule.validate().is_err());
        rule.amount_branches[1].minimum = "1000.0000001".into();
        assert!(rule.validate().is_err());
        rule.kind = "general".into();
        assert!(rule.validate().is_err());
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AmountBranch {
    pub key: String,
    pub name: String,
    pub currency: String,
    pub minimum: String,
    pub maximum: Option<String>,
    pub stages: Vec<ApprovalStage>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalRule {
    pub kind: String,
    pub mode: ApprovalMode,
    pub approver_user_ids: Vec<i64>,
    #[serde(default)]
    pub stages: Vec<ApprovalStage>,
    #[serde(default)]
    pub amount_branches: Vec<AmountBranch>,
}

pub fn validate_stages(stages: &[ApprovalStage]) -> Result<(), String> {
    if stages.is_empty() || stages.len() > 10 {
        return Err("配置审批须有 1–10 个阶段。".into());
    }
    let mut named = HashSet::new();
    for stage in stages {
        if stage.source == ApproverSource::Named {
            if stage.approver_user_ids.is_empty() || stage.approver_user_ids.len() > 10 {
                return Err("每个指定会签阶段须有 1–10 位审批人。".into());
            }
            for id in &stage.approver_user_ids {
                if *id <= 0 || !named.insert(*id) {
                    return Err("指定审批人无效或在多个阶段重复。".into());
                }
            }
        } else if !stage.approver_user_ids.is_empty() {
            return Err("组织来源的审批阶段不填写指定账号。".into());
        }
    }
    Ok(())
}

impl AmountBranch {
    pub fn range(&self) -> Result<(Decimal, Option<Decimal>), String> {
        let parse = |value: &str| {
            Decimal::from_str(value)
                .ok()
                .filter(|value| *value >= Decimal::ZERO && value.scale() <= 6)
                .ok_or_else(|| "审批金额须为不小于零、最多六位小数的精确数字。".to_owned())
        };
        let min = parse(&self.minimum)?;
        let max = self.maximum.as_deref().map(parse).transpose()?;
        if max.is_some_and(|max| max <= min) {
            return Err("金额上限须大于下限。".into());
        }
        Ok((min, max))
    }
}

impl ApprovalRule {
    pub fn account_ids(&self) -> Vec<i64> {
        self.approver_user_ids
            .iter()
            .copied()
            .chain(
                self.stages
                    .iter()
                    .chain(
                        self.amount_branches
                            .iter()
                            .flat_map(|branch| &branch.stages),
                    )
                    .flat_map(|stage| stage.approver_user_ids.iter().copied()),
            )
            .collect::<HashSet<_>>()
            .into_iter()
            .collect()
    }
    pub fn validate(&self) -> Result<(), String> {
        let named = self.mode == ApprovalMode::Named;
        if (named && (self.approver_user_ids.is_empty() || self.approver_user_ids.len() > 10))
            || (!named && !self.approver_user_ids.is_empty())
        {
            return Err("指定逐级审批须为 1–10 人，其它模式不填写逐级账号。".into());
        }
        let mut ids = HashSet::new();
        if self
            .approver_user_ids
            .iter()
            .any(|id| *id <= 0 || !ids.insert(*id))
        {
            return Err("逐级审批人无效或重复。".into());
        }
        if self.mode == ApprovalMode::Configured {
            validate_stages(&self.stages)?;
        } else if !self.stages.is_empty() {
            return Err("只有配置审批模式可设置默认阶段。".into());
        }
        if self.amount_branches.len() > 10
            || (!self.amount_branches.is_empty()
                && !["expense", "purchase"].contains(&self.kind.as_str()))
        {
            return Err("金额条件仅用于费用报销和采购申请，每类最多十条。".into());
        }
        let mut keys = HashSet::new();
        let mut ranges = vec![];
        for branch in &self.amount_branches {
            if branch.key.is_empty()
                || branch.key.len() > 80
                || !keys.insert(&branch.key)
                || branch.name.trim().is_empty()
                || branch.name.chars().count() > 100
            {
                return Err("金额条件编号或名称无效、重复。".into());
            }
            if branch.currency.len() != 3
                || !branch.currency.bytes().all(|b| b.is_ascii_uppercase())
            {
                return Err("金额条件须指定三位大写币种代码。".into());
            }
            let (min, max) = branch.range()?;
            if ranges.iter().any(
                |(currency, start, end): &(&String, Decimal, Option<Decimal>)| {
                    *currency == &branch.currency
                        && end.is_none_or(|end| min < end)
                        && max.is_none_or(|max| *start < max)
                },
            ) {
                return Err("同一币种的金额条件不能重叠；下限包含，上限不包含。".into());
            }
            ranges.push((&branch.currency, min, max));
            validate_stages(&branch.stages)?;
        }
        Ok(())
    }
    pub fn matching_branch(
        &self,
        currency: &str,
        amount: &str,
    ) -> Result<Option<&AmountBranch>, String> {
        if self.amount_branches.is_empty() {
            return Ok(None);
        }
        let amount = Decimal::from_str(amount).map_err(|_| "申请金额损坏。".to_owned())?;
        for branch in &self.amount_branches {
            let (min, max) = branch.range()?;
            if branch.currency == currency && amount >= min && max.is_none_or(|max| amount < max) {
                return Ok(Some(branch));
            }
        }
        Ok(None)
    }
}
