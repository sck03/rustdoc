//! Sales overview aggregates bounded field projections inside one authorized snapshot.
use super::{
    auth, crm_queries,
    error::{Result, unavailable},
    records::text,
    sales,
    store::{Actor, Store},
};
use crate::{clock::BusinessClock, contracts, generated_api::GET_CRM_DASHBOARD};
use chrono::{DateTime, Duration};
use export_doc_domain::sales as rules;
use export_doc_storage::{CrmList, GenericQuery};
use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub fn query(store: &Store, actor: &Actor, clock: &BusinessClock) -> Result<Value> {
    let now = clock.now().map_err(unavailable)?.utc_now;
    store.read_for(actor, GET_CRM_DASHBOARD, &[], |tx, actor| {
        let customer_count = tx.query_generic(&GenericQuery {
            kind: "crm-customers",
            scope: auth::data_scope(actor, "sales.customers", "view"),
            filters: &[], keyword: "", search_root: None, fields: &[("id", "id")],
            offset: i64::MAX, limit: 1,
        })?.0;
        let contacts = crm_queries::page(tx, actor, CrmList::Contacts, &[("id", "id")], i64::MAX, 1)?.0;
        let pending = CrmList::FollowUps { include_completed: false, customer_id: None, record_id: None };
        let (follow_up_count, follow_ups) = crm_queries::page(tx, actor, pending, &crm_queries::fields("ApiCrmFollowUpDto"), 0, 8)?;
        let (mut overdue, mut upcoming) = (0, 0);
        crm_queries::scan(tx, actor, pending, &[("nextFollowUpAt", "nextFollowUpAt")], |row| {
            if let Some(date) = row["nextFollowUpAt"].as_str().filter(|value| !value.is_empty()) {
                let date = DateTime::parse_from_rfc3339(date).map_err(|cause| unavailable(format!("跟进到期时间无效：{cause}")))?;
                overdue += usize::from(date < now);
                upcoming += usize::from(date >= now && date <= now + Duration::days(7));
            }
            Ok(())
        })?;
        let opportunities = CrmList::Opportunities { customer_id: None, stage: "", keyword: "", upcoming_closings: false };
        let mut stages: BTreeMap<String, usize> = BTreeMap::new();
        let mut currencies: BTreeMap<String, (usize, Decimal, Decimal)> = BTreeMap::new();
        crm_queries::scan(tx, actor, opportunities, &[
            ("stage", "stage"), ("estimatedAmount", "estimatedAmount"),
            ("probabilityPercent", "probabilityPercent"), ("currency", "currency"),
        ], |row| {
            *stages.entry(text(&row, "stage")).or_default() += 1;
            if rules::closed(&text(&row, "stage")) { return Ok(()); }
            let amount: Decimal = serde_json::from_value(row["estimatedAmount"].clone())?;
            let probability = Decimal::from(row["probabilityPercent"].as_i64().unwrap_or(0));
            let entry = currencies.entry(text(&row, "currency")).or_default();
            entry.0 += 1;
            entry.1 = entry.1.checked_add(amount).ok_or_else(|| unavailable("商机金额汇总超出范围。"))?;
            let weighted = amount.checked_mul(probability).and_then(|value| value.checked_div(Decimal::from(100))).ok_or_else(|| unavailable("加权金额超出范围。"))?;
            entry.2 = entry.2.checked_add(weighted).ok_or_else(|| unavailable("加权金额汇总超出范围。"))?;
            Ok(())
        })?;
        let (_, closings) = crm_queries::page(tx, actor, CrmList::Opportunities { customer_id: None, stage: "", keyword: "", upcoming_closings: true },
            &crm_queries::fields("ApiSalesOpportunityDto"), 0, 8)?;
        if closings.iter().any(|row| row["productId"].as_i64().is_some_and(|id| id > 0)) {
            auth::authorize(actor, "common.product-reference", "view")?;
        }
        let mut result = contracts::object(GET_CRM_DASHBOARD.id, false);
        result["customerCount"] = json!(customer_count);
        result["contactCount"] = json!(contacts);
        result["pendingFollowUpCount"] = json!(follow_up_count);
        result["overdueFollowUpCount"] = json!(overdue);
        result["dueNextSevenDaysCount"] = json!(upcoming);
        result["upcomingFollowUps"] = json!(follow_ups.into_iter().map(|row| contracts::project(contracts::schema("ApiCrmFollowUpDto"), row)).collect::<Vec<_>>());
        result["opportunityStages"] = json!(rules::STAGES.iter().map(|stage| json!({"stage":stage,"count":stages.get(*stage).copied().unwrap_or(0)})).collect::<Vec<_>>());
        result["opportunityCurrencies"] = json!(currencies.into_iter().map(|(currency,(count,amount,weighted))| json!({"currency":currency,"count":count,"estimatedAmount":amount,"weightedAmount":weighted})).collect::<Vec<_>>());
        result["upcomingOpportunityClosings"] = json!(closings.into_iter().map(sales::projected).collect::<Vec<_>>());
        Ok(result)
    })
}
