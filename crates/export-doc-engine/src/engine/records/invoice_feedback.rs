//! Invoice selections are consumed by the invoice's transaction, never by preview or a second request.
use super::*;
use chrono::Datelike;
use std::collections::BTreeSet;

pub(super) fn apply(
    tx: &Connection,
    actor: &Actor,
    invoice: &mut Value,
    business_date: chrono::NaiveDate,
) -> Result<()> {
    let pending = invoice["pendingHsFeedback"].take();
    invoice["pendingHsFeedback"] = json!([]);
    let items = pending
        .as_array()
        .ok_or_else(|| invalid("HS 反馈内容须为数组。"))?;
    let mut seen = BTreeSet::new();
    for input in items {
        crate::operation::check()?;
        let key = serde_json::to_string(&[
            input["queryText"].clone(),
            input["productName"].clone(),
            input["specification"].clone(),
            input["candidateCode"].clone(),
            input["accepted"].clone(),
        ])?;
        if seen.insert(key) {
            crate::engine::hs_learning::feedback(tx, actor, input, business_date.year())?;
        }
    }
    Ok(())
}
