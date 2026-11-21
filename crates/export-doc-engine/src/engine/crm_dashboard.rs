//! Sales overview aggregates bounded field projections inside one authorized snapshot.
use super::{
    auth, crm_queries,
    error::{Result, unavailable},
    records::text,
    sales,
    store::{Actor, Store},
};
use crate::{clock::BusinessClock, contracts, generated_api::GET_CRM_DASHBOARD};
use chrono::Duration;
use export_doc_domain::sales as rules;
use export_doc_storage::{CrmList, GenericQuery};
use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub fn query(store: &Store, actor: &Actor, clock: &BusinessClock) -> Result<Value> {
    let time = clock.now().map_err(unavailable)?;
    let now = time.utc_now;
    let start = time.today.to_string();
    let end = time
        .today
        .checked_add_days(chrono::Days::new(30))
        .ok_or_else(|| unavailable("业务日期超出范围。"))?
        .to_string();
    store.read_for(actor, GET_CRM_DASHBOARD, &[], |tx, actor| {
        let customer_count = tx.count_generic(&GenericQuery {
            kind: "crm-customers",
            scope: auth::data_scope(actor, "sales.customers", "view"),
            filters: &[], keyword: "", search_root: None, fields: &[("id", "id")],
            offset: 0, limit: 1,
        })?;
        let contacts = crm_queries::count(tx, actor, CrmList::Contacts)?;
        let (reminders, follow_ups) = crm_queries::reminders(tx, actor, &now.to_rfc3339(), &(now + Duration::days(7)).to_rfc3339())?;
        let opportunities = CrmList::Opportunities { customer_id: None, stage: "", keyword: "", include_archived: false, closing_window: None };
        let mut stages: BTreeMap<String, usize> = BTreeMap::new();
        let mut currencies: BTreeMap<String, (usize, Decimal, Decimal)> = BTreeMap::new();
        crm_queries::scan(tx, actor, opportunities, &[
            ("stage", "stage"), ("estimatedAmount", "estimatedAmount"),
            ("probabilityPercent", "probabilityPercent"), ("currency", "currency"),
        ], |row| {
            *stages.entry(text(&row, "stage")).or_default() += 1;
            if rules::closed(&text(&row, "stage")) { return Ok(()); }
            let amount: Decimal = serde_json::from_value(row["estimatedAmount"].clone())?;
            let probability = Decimal::from(row["probabilityPercent"].as_i64()
                .filter(|value| (0..=100).contains(value))
                .ok_or_else(|| unavailable("商机成交概率缺失或超出 0 至 100，请核对记录。"))?);
            let entry = currencies.entry(text(&row, "currency")).or_default();
            entry.0 += 1;
            entry.1 = entry.1.checked_add(amount).ok_or_else(|| unavailable("商机金额汇总超出范围。"))?;
            let weighted = amount.checked_mul(probability).and_then(|value| value.checked_div(Decimal::from(100))).ok_or_else(|| unavailable("加权金额超出范围。"))?;
            entry.2 = entry.2.checked_add(weighted).ok_or_else(|| unavailable("加权金额汇总超出范围。"))?;
            Ok(())
        })?;
        let (_, closings) = crm_queries::page(tx, actor, CrmList::Opportunities { customer_id: None, stage: "", keyword: "", include_archived: false, closing_window: Some((&start, &end)) },
            &crm_queries::fields("ApiSalesOpportunityDto"), 0, 8)?;
        if closings.iter().any(|row| row["productId"].as_i64().is_some_and(|id| id > 0)) {
            auth::authorize(actor, "common.product-reference", "view")?;
        }
        let mut result = contracts::object(GET_CRM_DASHBOARD.id, false);
        result["customerCount"] = json!(customer_count);
        result["contactCount"] = json!(contacts);
        result["pendingFollowUpCount"] = json!(reminders.pending);
        result["overdueFollowUpCount"] = json!(reminders.overdue);
        result["dueNextSevenDaysCount"] = json!(reminders.due_soon);
        result["upcomingFollowUps"] = json!(follow_ups.into_iter().map(|row| contracts::project(contracts::schema("ApiCrmFollowUpDto"), row)).collect::<Vec<_>>());
        result["opportunityStages"] = json!(rules::STAGES.iter().map(|stage| json!({"stage":stage,"count":stages.get(*stage).copied().unwrap_or(0)})).collect::<Vec<_>>());
        result["opportunityCurrencies"] = json!(currencies.into_iter().map(|(currency,(count,amount,weighted))| json!({"currency":currency,"count":count,"estimatedAmount":amount.to_string(),"weightedAmount":weighted.to_string()})).collect::<Vec<_>>());
        result["upcomingOpportunityClosings"] = json!(closings.into_iter().map(sales::projected).collect::<Vec<_>>());
        Ok(result)
    })
}
