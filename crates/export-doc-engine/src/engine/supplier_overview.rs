//! Supplier confirmation is a sales operation; visibility is refreshed in the
//! read snapshot and storage projects one confirmed assessment per supplier.
use super::{
    auth,
    error::{Result, unavailable},
    store::{Actor, Store},
};
use crate::{contracts, generated_api::GET_SUPPLIER_ASSESSMENT_OVERVIEW};
use rust_decimal::{Decimal, RoundingStrategy};
use serde_json::json;

pub fn query(store: &Store, actor: &Actor) -> Result<serde_json::Value> {
    store.read_for(actor, GET_SUPPLIER_ASSESSMENT_OVERVIEW, &[], |tx, actor| {
        let data = tx.supplier_overview(&auth::data_scope(
            actor,
            "sales.supplier-assessments",
            "view",
        ))?;
        let suppliers = data["totalSuppliers"]
            .as_i64()
            .ok_or_else(|| unavailable("供应商统计投影损坏。"))?;
        let items = data["items"]
            .as_array()
            .ok_or_else(|| unavailable("供应商评价投影损坏。"))?;
        let mut result = contracts::object(GET_SUPPLIER_ASSESSMENT_OVERVIEW.id, false);
        result["totalSuppliers"] = json!(suppliers);
        result["assessedSuppliers"] = json!(items.len());
        result["unassessedSuppliers"] = json!(suppliers - items.len() as i64);
        for (key, conclusion) in [
            ("preferredCount", "优先合作"),
            ("qualifiedCount", "合格"),
            ("watchCount", "观察"),
            ("pausedCount", "暂停合作"),
        ] {
            result[key] = json!(
                items
                    .iter()
                    .filter(|row| row["conclusion"] == conclusion)
                    .count()
            );
        }
        for (source, target) in [
            ("qualityScore", "averageQualityScore"),
            ("deliveryScore", "averageDeliveryScore"),
            ("serviceScore", "averageServiceScore"),
            ("priceScore", "averagePriceScore"),
        ] {
            let sum: i64 = items.iter().filter_map(|row| row[source].as_i64()).sum();
            result[target] = json!(if items.is_empty() {
                Decimal::ZERO
            } else {
                (Decimal::from(sum) / Decimal::from(items.len()))
                    .round_dp_with_strategy(2, RoundingStrategy::MidpointNearestEven)
            });
        }
        result["items"] = json!(items);
        Ok(result)
    })
}
