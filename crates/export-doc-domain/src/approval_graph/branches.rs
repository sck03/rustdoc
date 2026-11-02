//! Exact overlap checks for the finite comparison language; defaults cover gaps.
use super::ConditionGroup;
use crate::approval_conditions::{ApprovalCondition, ConditionField, ConditionOperator};
use rust_decimal::Decimal;
use std::str::FromStr;

pub fn overlap(a: &ConditionGroup, b: &ConditionGroup) -> Result<bool, String> {
    for left in a.terms()? {
        for right in b.terms()? {
            let mut term = left.clone();
            term.extend(&right);
            if possible(&term)? {
                return Ok(true);
            }
        }
    }
    Ok(false)
}
fn possible(term: &[&ApprovalCondition]) -> Result<bool, String> {
    for field in [
        ConditionField::Amount,
        ConditionField::Duration,
        ConditionField::Currency,
        ConditionField::Department,
        ConditionField::Category,
        ConditionField::PersonnelAction,
        ConditionField::Returnable,
    ] {
        let conditions = term
            .iter()
            .copied()
            .filter(|c| c.field == field)
            .collect::<Vec<_>>();
        if matches!(field, ConditionField::Amount | ConditionField::Duration) {
            let mut low = (Decimal::ZERO, true);
            let mut high: Option<(Decimal, bool)> = None;
            let mut excluded = vec![];
            let lower = |current: &mut (Decimal, bool), value: Decimal, inclusive: bool| {
                if value > current.0 {
                    *current = (value, inclusive);
                } else if value == current.0 {
                    current.1 &= inclusive;
                }
            };
            let upper = |current: &mut Option<(Decimal, bool)>, value: Decimal, inclusive: bool| {
                if current.is_none_or(|old| value < old.0) {
                    *current = Some((value, inclusive));
                } else if let Some(old) = current.as_mut() {
                    if value == old.0 {
                        old.1 &= inclusive;
                    }
                }
            };
            for c in conditions {
                let value = Decimal::from_str(&c.value).map_err(|_| "数字条件无效。")?;
                match c.operator {
                    ConditionOperator::Equal => {
                        lower(&mut low, value, true);
                        upper(&mut high, value, true);
                    }
                    ConditionOperator::NotEqual => excluded.push(value),
                    ConditionOperator::Greater => lower(&mut low, value, false),
                    ConditionOperator::GreaterOrEqual => lower(&mut low, value, true),
                    ConditionOperator::Less => upper(&mut high, value, false),
                    ConditionOperator::LessOrEqual => upper(&mut high, value, true),
                }
            }
            if high.is_some_and(|high| {
                low.0 > high.0
                    || low.0 == high.0 && (!low.1 || !high.1 || excluded.contains(&low.0))
            }) {
                return Ok(false);
            }
        } else {
            let mut equal: Option<&str> = None;
            let mut excluded = vec![];
            for c in conditions {
                match c.operator {
                    ConditionOperator::Equal => {
                        if equal.is_some_and(|old| old != c.value) {
                            return Ok(false);
                        }
                        equal = Some(&c.value);
                    }
                    ConditionOperator::NotEqual => excluded.push(c.value.as_str()),
                    _ => return Err("文字条件只支持等于和不等于。".into()),
                }
            }
            if equal.is_some_and(|equal| excluded.contains(&equal)) {
                return Ok(false);
            }
        }
    }
    Ok(true)
}
