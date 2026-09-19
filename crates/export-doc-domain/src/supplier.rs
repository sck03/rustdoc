use crate::{crm, invoice::valid_date};
use rust_decimal::Decimal;
use serde_json::{Value, json};
pub const ASSESSMENT_KINDS: &[&str] = &["定期评价", "订单复盘", "样品评估", "其它"];
pub const CONCLUSIONS: &[&str] = &["优先合作", "合格", "观察", "暂停合作"];
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
