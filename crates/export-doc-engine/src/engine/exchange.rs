use super::{
    NativeService, auth,
    error::{Result, error, unavailable},
    settings,
    store::Actor,
};
use crate::generated_api::*;
use serde_json::{Value, json};
mod scheduler;
pub(super) use scheduler::Scheduler;

pub(super) fn map_error(cause: export_doc_exchange::Error) -> crate::api::ApiError {
    error(
        match cause.kind {
            export_doc_exchange::ErrorKind::Invalid => 400,
            export_doc_exchange::ErrorKind::Unavailable => 503,
            export_doc_exchange::ErrorKind::Timeout => 504,
            export_doc_exchange::ErrorKind::Cancelled => 499,
        },
        cause.message,
    )
}

pub(super) fn configuration(service: &NativeService) -> Result<export_doc_exchange::Configuration> {
    export_doc_exchange::Configuration::read(&settings::current(&service.store)?["exchangeRate"])
        .map_err(map_error)
}

pub(super) fn settings_saved(
    service: &NativeService,
    operation: Operation,
    mut response: Value,
) -> Value {
    if operation == UPDATE_SETTINGS {
        if let Err(cause) = service.exchange_scheduler.refresh(service) {
            response["message"] = json!(format!(
                "设置已保存，但自动汇率获取未启动：{}",
                cause.message
            ));
            response["requiresRestart"] = json!(true);
        }
    }
    response
}

fn check() -> export_doc_exchange::Result<()> {
    crate::operation::check().map_err(|cause| {
        export_doc_exchange::Error::new(export_doc_exchange::ErrorKind::Cancelled, cause.message)
    })
}

pub const OPERATIONS: &[Operation] =
    &[LIST_EXCHANGE_RATES, LIST_AVAILABLE_EXCHANGE_RATE_CURRENCIES];
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize_operation(actor, operation, query)?;
    let config = configuration(service)?;
    let force = query
        .iter()
        .any(|(key, value)| *key == "forceRefresh" && value.eq_ignore_ascii_case("true"));
    let all = operation == LIST_AVAILABLE_EXCHANGE_RATE_CURRENCIES;
    let time = service.clock.now().map_err(unavailable)?;
    let quotation = service
        .exchange
        .get(&config, force, all, time.utc_now, &check)
        .map_err(map_error)?;
    let policy = "报价为每 100 外币对应的人民币金额，仅供参考，不自动改写单据汇率。服务运行期间共享内存缓存；失败保留同一来源的上次成功报价，重启后重新获取。";
    if all {
        let mut currencies: Vec<_> = quotation
            .rates
            .iter()
            .map(|rate| rate.currency_name.clone())
            .collect();
        currencies.sort();
        currencies.dedup();
        Ok(
            json!({"currencies":currencies,"sourceUrl":config.source.url,"fetchedAt":quotation.fetched_at,"storagePolicy":policy,
                "isStale":quotation.is_stale,"lastError":quotation.last_error}),
        )
    } else {
        Ok(
            json!({"statusText":if quotation.is_stale {"显示上次成功报价，请留意更新时间。".into()} else {format!("已读取 {} 种常用货币。",quotation.rates.len())},"rates":quotation.rates,
            "selectedCurrencies":config.selected,"sourceUrl":config.source.url,"sourceFormat":config.source.format.name(),"fetchedAt":quotation.fetched_at,
            "cacheDurationMinutes":config.minutes,"autoRefreshEnabled":config.automatic,"storagePolicy":policy,
            "availableCurrencies":quotation.available,"missingCurrencies":quotation.missing,
            "isStale":quotation.is_stale,"lastAttemptAt":quotation.last_attempt_at,"nextRefreshAt":quotation.next_refresh_at,"lastError":quotation.last_error}),
        )
    }
}
