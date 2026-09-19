//! Sales overview reads customers, follow-ups and opportunity totals in one snapshot.
use super::{
    auth, crm,
    error::{Result, unavailable},
    records::text,
    sales,
    store::{self, Actor, Store},
};
use crate::{clock::BusinessClock, contracts, generated_api::GET_CRM_DASHBOARD};
use chrono::{DateTime, Duration};
use export_doc_domain::sales as rules;
use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub fn query(store: &Store, actor: &Actor, clock: &BusinessClock) -> Result<Value> {
    let now = clock.now().map_err(unavailable)?.utc_now;
    store.transaction(|tx| {
        let customers: Vec<_> = store::all(tx, "crm-customers")?.into_iter().filter(|row| auth::visible(actor,"sales.customers","view",row)).collect();
        let contacts = store::all(tx, "crm-contacts")?.into_iter().filter(|row| customers.iter().any(|customer| customer["id"] == row["crmCustomerId"])).count();
        let follow_ups: Vec<_> = crm::sorted(crm::visible(tx, actor)?)?.into_iter().filter(|row| row["isCompleted"] != true).collect();
        let mut overdue = 0;
        let mut upcoming = 0;
        for row in &follow_ups {
            if let Some(date) = row["nextFollowUpAt"].as_str() {
                let date = DateTime::parse_from_rfc3339(date).map_err(|cause| unavailable(format!("跟进到期时间无效：{cause}")))?;
                overdue += usize::from(date < now);
                upcoming += usize::from(date >= now && date <= now + Duration::days(7));
            }
        }
        let mut result = contracts::object(GET_CRM_DASHBOARD.id, false);
        result["customerCount"] = json!(customers.len());
        result["contactCount"] = json!(contacts);
        result["pendingFollowUpCount"] = json!(follow_ups.len());
        result["overdueFollowUpCount"] = json!(overdue);
        result["dueNextSevenDaysCount"] = json!(upcoming);
        result["upcomingFollowUps"] = json!(follow_ups.into_iter().take(8).collect::<Vec<_>>());
        let opportunities = sales::visible(tx, actor)?;
        result["opportunityStages"] = json!(rules::STAGES.iter().map(|stage| json!({"stage":stage,"count":opportunities.iter().filter(|row| row["stage"] == *stage).count()})).collect::<Vec<_>>());
        let mut currencies: BTreeMap<String, (usize, Decimal, Decimal)> = BTreeMap::new();
        let mut closings = vec![];
        for row in opportunities.into_iter().filter(|row| !rules::closed(&text(row, "stage"))) {
            let amount: Decimal = serde_json::from_value(row["estimatedAmount"].clone())?;
            let probability = Decimal::from(row["probabilityPercent"].as_i64().unwrap_or(0));
            let entry = currencies.entry(text(&row, "currency")).or_default();
            entry.0 += 1;
            entry.1 = entry.1.checked_add(amount).ok_or_else(|| unavailable("商机金额汇总超出范围。"))?;
            let weighted = amount.checked_mul(probability).and_then(|value| value.checked_div(Decimal::from(100))).ok_or_else(|| unavailable("加权金额超出范围。"))?;
            entry.2 = entry.2.checked_add(weighted).ok_or_else(|| unavailable("加权金额汇总超出范围。"))?;
            if row["expectedCloseDate"].as_str().is_some_and(|date| !date.is_empty()) { closings.push(row); }
        }
        result["opportunityCurrencies"] = json!(currencies.into_iter().map(|(currency,(count,amount,weighted))| json!({"currency":currency,"count":count,"estimatedAmount":amount,"weightedAmount":weighted})).collect::<Vec<_>>());
        closings.sort_by(|a,b| a["expectedCloseDate"].as_str().cmp(&b["expectedCloseDate"].as_str()).then_with(|| b["id"].as_i64().cmp(&a["id"].as_i64())));
        result["upcomingOpportunityClosings"] = json!(closings.into_iter().take(8).map(|row| sales::details(tx, actor, row, "view")).collect::<Result<Vec<_>>>()?);
        Ok(result)
    })
}
