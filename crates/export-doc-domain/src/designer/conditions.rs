//! Bounded, typed conditions shared by template validation and native reports.
use super::ReportConditionalBlock;
use chrono::NaiveDate;
use rust_decimal::Decimal;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{borrow::Cow, cmp::Ordering};

pub const MAX_CONDITIONAL_RULES: usize = 8;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum ConditionMatch {
    #[default]
    All,
    Any,
}
impl ConditionMatch {
    pub fn is_all(&self) -> bool {
        *self == Self::All
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum ComparisonType {
    #[default]
    Text,
    Number,
    Date,
}
impl ComparisonType {
    pub fn is_text(&self) -> bool {
        *self == Self::Text
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum ConditionOperator {
    HasValue,
    IsEmpty,
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ConditionalRule {
    pub field_path: String,
    pub operator: ConditionOperator,
    #[serde(default)]
    pub value: String,
    #[serde(default, skip_serializing_if = "ComparisonType::is_text")]
    pub comparison_type: ComparisonType,
    #[serde(default, skip_serializing_if = "is_false")]
    pub ignore_case: bool,
}

impl ConditionalRule {
    pub fn validate(&self) -> Result<(), String> {
        use ConditionOperator::*;
        if self.value.chars().count() > 2048 {
            return Err("条件比较值不能超过 2048 个字符。".into());
        }
        if self.ignore_case && self.comparison_type != ComparisonType::Text {
            return Err("只有文本条件支持忽略大小写。".into());
        }
        if matches!(self.operator, HasValue | IsEmpty) {
            return Ok(());
        }
        match self.comparison_type {
            ComparisonType::Text => {
                if matches!(
                    self.operator,
                    GreaterThan | GreaterOrEqual | LessThan | LessOrEqual
                ) {
                    return Err("大小和先后判断请选择数字或日期类型。".into());
                }
                if matches!(
                    self.operator,
                    Contains | NotContains | StartsWith | EndsWith
                ) && self.value.trim().is_empty()
                {
                    return Err("文本匹配的比较值不能为空白。".into());
                }
            }
            ComparisonType::Number | ComparisonType::Date => {
                if matches!(
                    self.operator,
                    Contains | NotContains | StartsWith | EndsWith
                ) {
                    return Err("包含、开头和结尾判断仅适用于文本。".into());
                }
                self.compare(&self.value, &self.value)?;
            }
        }
        Ok(())
    }

    /// `displayed` preserves existing text equality semantics; numeric and date
    /// rules use the original scalar so formatted amounts never lose precision.
    pub fn matches(&self, field: &Value, displayed: &str) -> Result<bool, String> {
        use ConditionOperator::*;
        let has_value = !field.is_null() && field != false && !displayed.trim().is_empty();
        if self.operator == HasValue {
            return Ok(has_value);
        }
        if self.operator == IsEmpty {
            return Ok(!has_value);
        }
        if self.comparison_type != ComparisonType::Text {
            if field.is_null() || field.as_str().is_some_and(|value| value.trim().is_empty()) {
                return Ok(false);
            }
            let raw = field
                .as_str()
                .map(str::to_owned)
                .unwrap_or_else(|| field.to_string());
            return self
                .compare(&raw, &self.value)
                .map_err(|error| format!("条件字段 {}：{error}", self.field_path));
        }
        let actual = if self.ignore_case {
            Cow::Owned(displayed.to_lowercase())
        } else {
            Cow::Borrowed(displayed)
        };
        let expected = if self.ignore_case {
            Cow::Owned(self.value.to_lowercase())
        } else {
            Cow::Borrowed(self.value.as_str())
        };
        Ok(match self.operator {
            Equals => actual == expected,
            NotEquals => actual != expected,
            Contains => actual.contains(expected.as_ref()),
            NotContains => !actual.contains(expected.as_ref()),
            StartsWith => actual.starts_with(expected.as_ref()),
            EndsWith => actual.ends_with(expected.as_ref()),
            _ => return Err("文本条件操作符无效。".into()),
        })
    }

    fn compare(&self, actual: &str, expected: &str) -> Result<bool, String> {
        let order = match self.comparison_type {
            ComparisonType::Number => number(actual)?.cmp(&number(expected)?),
            ComparisonType::Date => date(actual)?.cmp(&date(expected)?),
            ComparisonType::Text => return Err("大小比较类型无效。".into()),
        };
        use ConditionOperator::*;
        Ok(match self.operator {
            Equals => order == Ordering::Equal,
            NotEquals => order != Ordering::Equal,
            GreaterThan => order == Ordering::Greater,
            GreaterOrEqual => order != Ordering::Less,
            LessThan => order == Ordering::Less,
            LessOrEqual => order != Ordering::Greater,
            _ => return Err("数字或日期条件操作符无效。".into()),
        })
    }
}

impl ReportConditionalBlock {
    pub fn conditions(&self) -> impl Iterator<Item = &ConditionalRule> {
        std::iter::once(&self.condition).chain(&self.additional_conditions)
    }
    pub fn validate_conditions(&self) -> Result<(), String> {
        if self.additional_conditions.len() >= MAX_CONDITIONAL_RULES {
            return Err(format!("每个条件组件最多 {MAX_CONDITIONAL_RULES} 条规则。"));
        }
        for rule in self.conditions() {
            rule.validate()?;
        }
        Ok(())
    }
    pub fn matches(
        &self,
        mut evaluate: impl FnMut(&ConditionalRule) -> Result<bool, String>,
    ) -> Result<bool, String> {
        self.validate_conditions()?;
        let mut result = self.match_mode == ConditionMatch::All;
        for rule in self.conditions() {
            // Check every configured rule, even if an earlier one decided the
            // result, so an invalid business value cannot silently disappear.
            let matched = evaluate(rule)?;
            match self.match_mode {
                ConditionMatch::All => result &= matched,
                ConditionMatch::Any => result |= matched,
            }
        }
        Ok(result)
    }
}

fn number(value: &str) -> Result<Decimal, String> {
    let value = value.trim();
    let unsigned = value
        .strip_prefix('+')
        .or_else(|| value.strip_prefix('-'))
        .unwrap_or(value);
    let (whole, fraction) = unsigned.split_once('.').unwrap_or((unsigned, ""));
    if whole.is_empty()
        || !whole.bytes().all(|c| c.is_ascii_digit())
        || (unsigned.contains('.') && fraction.is_empty())
        || fraction.len() > 28
        || !fraction.bytes().all(|c| c.is_ascii_digit())
    {
        return Err("请输入精确小数（最多 28 位小数），不含千位分隔符或单位。".into());
    }
    Decimal::from_str_exact(value).map_err(|_| "数字超出精确小数支持范围。".into())
}

fn date(value: &str) -> Result<NaiveDate, String> {
    if !value.bytes().enumerate().all(|(index, byte)| {
        if index == 4 || index == 7 {
            byte == b'-'
        } else {
            byte.is_ascii_digit()
        }
    }) || !crate::invoice::valid_date(value)
    {
        return Err("日期须为有效的 YYYY-MM-DD。".into());
    }
    NaiveDate::parse_from_str(value, "%Y-%m-%d").map_err(|_| "日期须为有效的 YYYY-MM-DD。".into())
}
fn is_false(value: &bool) -> bool {
    !value
}
