//! Typed conditions: no scripts, paths or arbitrary field access.
use rust_decimal::Decimal;
use serde::{Deserialize, Serialize};
use std::str::FromStr;

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ConditionField {
    Amount,
    Currency,
    Duration,
    Department,
    Category,
    PersonnelAction,
}
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum ConditionOperator {
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ApprovalCondition {
    pub field: ConditionField,
    pub operator: ConditionOperator,
    pub value: String,
}
#[derive(Default)]
pub struct ConditionContext {
    pub amount: Option<Decimal>,
    pub currency: String,
    pub duration: Option<Decimal>,
    pub department: String,
    pub category: String,
    pub personnel_action: String,
}
impl ApprovalCondition {
    pub fn validate(&self) -> Result<(), String> {
        if self.value.is_empty() || self.value.chars().count() > 100 {
            return Err("流程条件值须为 1–100 字。".into());
        }
        if matches!(
            self.field,
            ConditionField::Amount | ConditionField::Duration
        ) {
            let value =
                Decimal::from_str(&self.value).map_err(|_| "金额和时长条件须为精确数字。")?;
            if value < Decimal::ZERO || value.scale() > 6 {
                return Err("数字条件须非负且不超过六位小数。".into());
            }
        } else if !matches!(
            self.operator,
            ConditionOperator::Equal | ConditionOperator::NotEqual
        ) {
            return Err("文字条件只支持等于或不等于。".into());
        }
        Ok(())
    }
    pub fn matches(&self, context: &ConditionContext) -> Result<bool, String> {
        self.validate()?;
        let ordering = match self.field {
            ConditionField::Amount | ConditionField::Duration => {
                let actual = if self.field == ConditionField::Amount {
                    context.amount
                } else {
                    context.duration
                };
                actual
                    .ok_or("申请不提供该数字条件字段。")?
                    .cmp(&Decimal::from_str(&self.value).map_err(|_| "数字条件无效。")?)
            }
            _ => {
                let actual = match self.field {
                    ConditionField::Currency => &context.currency,
                    ConditionField::Department => &context.department,
                    ConditionField::Category => &context.category,
                    ConditionField::PersonnelAction => &context.personnel_action,
                    _ => unreachable!(),
                };
                actual.cmp(&self.value)
            }
        };
        Ok(match self.operator {
            ConditionOperator::Equal => ordering.is_eq(),
            ConditionOperator::NotEqual => !ordering.is_eq(),
            ConditionOperator::Greater => ordering.is_gt(),
            ConditionOperator::GreaterOrEqual => !ordering.is_lt(),
            ConditionOperator::Less => ordering.is_lt(),
            ConditionOperator::LessOrEqual => !ordering.is_gt(),
        })
    }
}
