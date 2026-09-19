use super::{
    auth,
    error::Result,
    records::text,
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::GET_SUPPLIER_ASSESSMENT_OVERVIEW};
use rust_decimal::{Decimal, RoundingStrategy};
use serde_json::{Value, json};

pub fn query(store: &Store, actor: &Actor) -> Result<Value> {
    store.transaction(|tx| {
        let suppliers: Vec<_> = store::all(tx, "suppliers")?
            .into_iter()
            .filter(|row| auth::visible(actor, "sales.supplier-assessments", "view", row))
            .collect();
        let mut assessments: Vec<_> = store::all(tx, "supplier-assessments")?
            .into_iter()
            .filter(|row| row["status"] == "Confirmed")
            .collect();
        assessments.sort_by(|a, b| {
            b["assessmentDate"]
                .as_str()
                .cmp(&a["assessmentDate"].as_str())
                .then_with(|| b["id"].as_i64().cmp(&a["id"].as_i64()))
        });
        let mut items = vec![];
        for supplier in &suppliers {
            let rows: Vec<_> = assessments
                .iter()
                .filter(|row| row["supplierCompanyId"] == supplier["id"])
                .collect();
            if let Some(latest) = rows.first() {
                let mut value =
                    contracts::initial(contracts::schema("ApiSupplierAssessmentOverviewItemDto"));
                for key in [
                    "supplierCompanyId",
                    "qualityScore",
                    "deliveryScore",
                    "serviceScore",
                    "priceScore",
                    "averageScore",
                    "conclusion",
                    "notes",
                ] {
                    value[key] = latest[key].clone();
                }
                value["supplierName"] = supplier["name"].clone();
                value["supplierStatus"] = supplier["status"].clone();
                value["category"] = supplier["category"].clone();
                value["assessmentCount"] = json!(rows.len());
                value["latestAssessmentDate"] = latest["assessmentDate"].clone();
                value["latestAssessmentKind"] = latest["assessmentKind"].clone();
                items.push(value);
            }
        }
        let priority = |row: &Value| {
            ["暂停合作", "观察", "合格", "优先合作"]
                .iter()
                .position(|value| row["conclusion"] == *value)
                .unwrap_or(4)
        };
        items.sort_by(|a, b| {
            priority(a)
                .cmp(&priority(b))
                .then_with(|| {
                    b["latestAssessmentDate"]
                        .as_str()
                        .cmp(&a["latestAssessmentDate"].as_str())
                })
                .then_with(|| text(a, "supplierName").cmp(&text(b, "supplierName")))
        });
        let mut result = contracts::object(GET_SUPPLIER_ASSESSMENT_OVERVIEW.id, false);
        result["totalSuppliers"] = json!(suppliers.len());
        result["assessedSuppliers"] = json!(items.len());
        result["unassessedSuppliers"] = json!(suppliers.len() - items.len());
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
            let mean = if items.is_empty() {
                Decimal::ZERO
            } else {
                (Decimal::from(sum) / Decimal::from(items.len()))
                    .round_dp_with_strategy(2, RoundingStrategy::MidpointNearestEven)
            };
            result[target] = json!(mean);
        }
        result["items"] = json!(items);
        Ok(result)
    })
}
