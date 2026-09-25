//! Office approval rules; no payment templates, database or host dependencies.
use chrono::{Datelike, NaiveDate};
use rust_decimal::Decimal;
use serde_json::Value;
use std::str::FromStr;

fn string<'a>(value: &'a Value, key: &str) -> &'a str {
    value[key].as_str().unwrap_or("")
}
pub fn date(value: &str) -> Result<NaiveDate, String> {
    if value.len() != 10 || value.as_bytes()[4] != b'-' || value.as_bytes()[7] != b'-' {
        return Err("日期须为 YYYY-MM-DD。".into());
    }
    NaiveDate::parse_from_str(value, "%Y-%m-%d").map_err(|_| "日期无效。".into())
}
pub fn leave_span(value: &Value) -> Result<(i64, i64), String> {
    if !["Annual", "Sick", "Personal", "Other"].contains(&string(value, "category")) {
        return Err("请选择有效的请假类型。".into());
    }
    let half = |key| match string(value, key) {
        "AM" => Ok(0),
        "PM" => Ok(1),
        _ => Err("请选择上午或下午。".to_owned()),
    };
    let start =
        i64::from(date(string(value, "startsOn"))?.num_days_from_ce()) * 2 + half("startPeriod")?;
    let end =
        i64::from(date(string(value, "endsOn"))?.num_days_from_ce()) * 2 + half("endPeriod")? + 1;
    if end <= start || end - start > 732 {
        return Err("请假结束须晚于开始，单次不超过 366 个自然日。".into());
    }
    Ok((start, end))
}
pub fn expense_total(lines: &Value, currency: &str, today: NaiveDate) -> Result<Decimal, String> {
    if !["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].contains(&currency) {
        return Err("请选择支持的币种。".into());
    }
    let lines = lines
        .as_array()
        .filter(|v| !v.is_empty() && v.len() <= 100)
        .ok_or("费用明细须为 1–100 行。")?;
    let mut total = Decimal::ZERO;
    for line in lines {
        if !["Travel", "Transport", "Meals", "Office", "Other"].contains(&string(line, "category"))
        {
            return Err("费用类别无效。".into());
        }
        if date(string(line, "spentOn"))? > today {
            return Err("费用日期不能晚于公司业务日期。".into());
        }
        let description = string(line, "description").trim();
        if description.is_empty() || description.chars().count() > 200 {
            return Err("每行须填写不超过 200 字的费用说明。".into());
        }
        let amount = string(line, "amount");
        if amount.is_empty()
            || amount.len() > 16
            || !amount.bytes().all(|v| v.is_ascii_digit() || v == b'.')
        {
            return Err("金额须为十进制数字，最多两位小数。".into());
        }
        let amount = Decimal::from_str(amount).map_err(|_| "金额无效。")?;
        if amount <= Decimal::ZERO || amount.scale() > 2 {
            return Err("金额须大于零且最多两位小数。".into());
        }
        total = total.checked_add(amount).ok_or("金额超出范围。")?;
        if total > Decimal::from(100_000_000) {
            return Err("单笔报销合计不能超过一亿元。".into());
        }
    }
    Ok(total)
}
pub fn next_status(status: &str, action: &str, expense: bool) -> Option<&'static str> {
    match (status, action) {
        ("Draft" | "Rejected", "submit") => Some("Pending"),
        ("Pending", "approve") => Some("Approved"),
        ("Pending", "reject") => Some("Rejected"),
        ("Pending", "withdraw") => Some("Draft"),
        ("Draft" | "Rejected", "cancel") | ("Approved", "void") => Some("Cancelled"),
        ("Approved", "complete") if expense => Some("HandedOff"),
        ("Approved", "complete") => Some("Completed"),
        _ => None,
    }
}

pub fn purchase_total(lines: &Value, currency: &str) -> Result<Decimal, String> {
    if !["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].contains(&currency) {
        return Err("请选择支持的币种。".into());
    }
    let lines = lines
        .as_array()
        .filter(|v| !v.is_empty() && v.len() <= 100)
        .ok_or("采购明细须为 1–100 行。")?;
    let mut total = Decimal::ZERO;
    for line in lines {
        for (key, max) in [("name", 200), ("unit", 20)] {
            let value = string(line, key).trim();
            if value.is_empty() || value.chars().count() > max {
                return Err("请填写有效的物品名称和单位。".into());
            }
        }
        if string(line, "specification").chars().count() > 200 {
            return Err("规格不能超过 200 字。".into());
        }
        let parse = |key, scale| {
            let value = string(line, key);
            if value.is_empty()
                || value.len() > 16
                || !value.bytes().all(|b| b.is_ascii_digit() || b == b'.')
            {
                return Err("数量或预算单价格式无效。".to_owned());
            }
            Decimal::from_str(value)
                .ok()
                .filter(|v| *v > Decimal::ZERO && v.scale() <= scale)
                .ok_or("数量最多三位小数，预算单价最多两位小数，且须大于零。".to_owned())
        };
        let amount = parse("quantity", 3)?
            .checked_mul(parse("unitPrice", 2)?)
            .ok_or("预算金额超出范围。")?
            .round_dp_with_strategy(2, rust_decimal::RoundingStrategy::MidpointAwayFromZero);
        if amount <= Decimal::ZERO {
            return Err("每项采购预算舍入到分后须大于零。".into());
        }
        total = total
            .checked_add(amount)
            .filter(|v| *v <= Decimal::from(100_000_000))
            .ok_or("采购预算合计超出范围。")?;
    }
    Ok(total)
}

pub fn overtime_span(
    value: &Value,
) -> Result<
    (
        chrono::DateTime<chrono::FixedOffset>,
        chrono::DateTime<chrono::FixedOffset>,
    ),
    String,
> {
    let parse = |key| {
        chrono::DateTime::parse_from_rfc3339(string(value, key))
            .map_err(|_| "加班时间须包含时区。".to_owned())
    };
    let start = parse("startsAt")?;
    let end = parse("endsAt")?;
    let minutes = (end - start).num_minutes();
    if minutes < 15 || minutes > 24 * 60 || (end - start).num_seconds() != minutes * 60 {
        return Err("加班时段须为 15 分钟至 24 小时，精确到分钟。".into());
    }
    Ok((start, end))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test]
    fn exact_amounts_and_calendar_half_days() {
        let today = date("2026-09-25").unwrap();
        let line = |amount| json!({"amount":amount,"spentOn":"2026-09-24","category":"Travel","description":"交通"});
        assert_eq!(
            expense_total(&json!([line("0.1"), line("0.2")]), "CNY", today).unwrap(),
            Decimal::new(3, 1)
        );
        for invalid in ["-1", "1e3", "0", "0.001", "NaN"] {
            assert!(expense_total(&json!([line(invalid)]), "CNY", today).is_err());
        }
        let mut leave = json!({"category":"Annual","startsOn":"2026-09-25","endsOn":"2026-09-25","startPeriod":"PM","endPeriod":"PM"});
        let (from, to) = leave_span(&leave).unwrap();
        assert_eq!(to - from, 1);
        leave["endPeriod"] = json!("AM");
        assert!(leave_span(&leave).is_err());
        assert!(date("2026-02-29").is_err());
        assert!(next_status("Approved", "submit", true).is_none());
        assert!(next_status("Pending", "settle", true).is_none());
    }
}
