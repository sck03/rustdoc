use super::{
    NativeService, auth,
    error::{Result, error, unavailable},
    settings,
    store::Actor,
};
use crate::generated_api::*;
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] =
    &[LIST_EXCHANGE_RATES, LIST_AVAILABLE_EXCHANGE_RATE_CURRENCIES];
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize_operation(actor, operation, query)?;
    let config = settings::current(&service.store)?;
    let force = query
        .iter()
        .any(|(key, value)| *key == "forceRefresh" && value.eq_ignore_ascii_case("true"));
    let all = operation == LIST_AVAILABLE_EXCHANGE_RATE_CURRENCIES;
    let time = service.clock.now().map_err(unavailable)?;
    let quotation = service
        .exchange
        .get(&config["exchangeRate"], force, all, time.utc_now, &|| {
            crate::operation::check().map_err(|cause| {
                export_doc_exchange::Error::new(
                    if cause.status == Some(504) {
                        export_doc_exchange::ErrorKind::Timeout
                    } else {
                        export_doc_exchange::ErrorKind::Cancelled
                    },
                    cause.message,
                )
            })
        })
        .map_err(|cause| {
            error(
                match cause.kind {
                    export_doc_exchange::ErrorKind::Invalid => 400,
                    export_doc_exchange::ErrorKind::Unavailable => 503,
                    export_doc_exchange::ErrorKind::Timeout => 504,
                    export_doc_exchange::ErrorKind::Cancelled => 499,
                },
                cause.message,
            )
        })?;
    let policy = "查询结果仅缓存在内存；货币报价为每 100 外币对应的人民币金额。";
    if all {
        let mut currencies: Vec<_> = quotation
            .rates
            .iter()
            .map(|rate| rate.currency_name.clone())
            .collect();
        currencies.sort();
        currencies.dedup();
        Ok(
            json!({"currencies":currencies,"sourceUrl":quotation.source,"fetchedAt":quotation.fetched_at,"storagePolicy":policy}),
        )
    } else {
        Ok(
            json!({"statusText":format!("获取成功，共 {} 种货币。",quotation.rates.len()),"rates":quotation.rates,
            "selectedCurrencies":quotation.selected,"sourceUrl":quotation.source,"fetchedAt":quotation.fetched_at,
            "cacheDurationMinutes":quotation.cache_minutes,"storagePolicy":policy}),
        )
    }
}
