use crate::{business_date::is_valid as valid_date, crm};
use rust_decimal::Decimal;
use serde_json::{Value, json};
pub const ASSESSMENT_KINDS: &[&str] = &["定期评价", "订单复盘", "样品评估", "其它"];
pub const CONCLUSIONS: &[&str] = &["优先合作", "合格", "观察", "暂停合作"];
pub fn reference_price(value: &Value) -> Result<String, String> {
    let text = value.as_str().ok_or("参考价必须是精确十进制文本。")?;
    if text.len() > 60 {
        return Err("参考价最多 60 个字符。".into());
    }
    let price = crate::number::parse_decimal_text(text).ok_or("参考价格式或精度无效。")?;
    Ok(price.normalize().to_string())
}
pub fn assessment(value: &mut Value, today: &str) -> Result<(), String> {
    let date = value["assessmentDate"].as_str().unwrap_or("");
    if !valid_date(date) || date > today {
        return Err("评价日期必须有效且不能晚于今天。".into());
    }
    for (key, choices, label) in [
        ("assessmentKind", ASSESSMENT_KINDS, "评价类型"),
        ("conclusion", CONCLUSIONS, "评价结论"),
    ] {
        let text = crm::clean(value[key].as_str().unwrap_or(""));
        if !choices.contains(&text.as_str()) {
            return Err(format!("{label}无效。"));
        }
        value[key] = json!(text);
    }
    let mut total = 0;
    for (key, label) in [
        ("qualityScore", "质量"),
        ("deliveryScore", "交期"),
        ("serviceScore", "服务"),
        ("priceScore", "价格"),
    ] {
        let score = value[key]
            .as_i64()
            .filter(|score| (1..=5).contains(score))
            .ok_or_else(|| format!("{label}评分必须在 1 至 5 分之间。"))?;
        total += score;
    }
    value["averageScore"] = json!(Decimal::from(total) / Decimal::from(4));
    value["notes"] = json!(crm::text(
        value["notes"].as_str().unwrap_or(""),
        "评价备注",
        1000,
        false
    )?);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn reference_price_preserves_precision_and_rejects_rounding() {
        assert_eq!(
            reference_price(&json!("9007199254740993.0100")).unwrap(),
            "9007199254740993.01"
        );
        assert_eq!(reference_price(&json!("1.005")).unwrap(), "1.005");
        for (text, expected) in [
            ("-1000", "-1000"),
            ("-0.01", "-0.01"),
            ("+001.00", "1"),
            ("01", "1"),
            ("-0", "0"),
            ("-9007199254740993.0100", "-9007199254740993.01"),
        ] {
            assert_eq!(reference_price(&json!(text)).unwrap(), expected);
        }
        for value in [
            json!(-1),
            json!(1.005),
            json!("--1"),
            json!("+-1"),
            json!("1_0"),
            json!(".5"),
            json!("1."),
            json!("1e3"),
            json!(" 1"),
            json!("0.00000000000000000000000000001"),
        ] {
            assert!(reference_price(&value).is_err(), "{value}");
        }
    }
}
