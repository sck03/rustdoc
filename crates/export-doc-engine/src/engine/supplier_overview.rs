//! Supplier confirmation is a sales operation; visibility is refreshed in the
//! read snapshot and storage projects one confirmed assessment per supplier.
use super::{
    auth,
    error::{Result, invalid, unavailable},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::GET_SUPPLIER_ASSESSMENT_OVERVIEW};
use rust_decimal::{Decimal, RoundingStrategy};
use serde_json::json;

pub fn query(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<serde_json::Value> {
    let (page, size, offset) = store::page_parameters(query);
    let filter = super::request_values::find(query, "filter").unwrap_or("all");
    if !matches!(filter, "all" | "attention" | "preferred") {
        return Err(invalid("供应商总览筛选无效。"));
    }
    store.read_for(actor, GET_SUPPLIER_ASSESSMENT_OVERVIEW, &[], |tx, actor| {
        let data = tx.supplier_overview(
            &auth::data_scope(actor, "sales.supplier-assessments", "view"),
            filter,
            offset,
            size as i64,
        )?;
        let suppliers = data["totalSuppliers"]
            .as_i64()
            .ok_or_else(|| unavailable("供应商统计投影损坏。"))?;
        let assessed = data["assessedSuppliers"]
            .as_i64()
            .ok_or_else(|| unavailable("供应商评价统计损坏。"))?;
        let mut result = contracts::object(GET_SUPPLIER_ASSESSMENT_OVERVIEW.id, false);
        result["totalSuppliers"] = json!(suppliers);
        result["assessedSuppliers"] = json!(assessed);
        result["unassessedSuppliers"] = json!(suppliers - assessed);
        for key in [
            "preferredCount",
            "qualifiedCount",
            "watchCount",
            "pausedCount",
            "totalCount",
        ] {
            result[key] = data[key].clone();
        }
        result["pageNumber"] = json!(page);
        result["pageSize"] = json!(size);
        for (source, target) in [
            ("qualityScore", "averageQualityScore"),
            ("deliveryScore", "averageDeliveryScore"),
            ("serviceScore", "averageServiceScore"),
            ("priceScore", "averagePriceScore"),
        ] {
            let sum = data[source]
                .as_i64()
                .ok_or_else(|| unavailable("供应商评分统计损坏。"))?;
            result[target] = json!(if assessed == 0 {
                Decimal::ZERO
            } else {
                (Decimal::from(sum) / Decimal::from(assessed))
                    .round_dp_with_strategy(2, RoundingStrategy::MidpointNearestEven)
            });
        }
        result["items"] = data["items"].clone();
        Ok(result)
    })
}
