//! Optional exchange-rate capability. Hosts supply settings, time and cancellation.
mod endpoint;
mod parser;
mod transport;
use chrono::{DateTime, Utc};
pub use endpoint::{DEFAULT_URL, normalize, public_address};
use export_doc_contracts::generated_api::ApiExchangeRateDto;
pub use parser::parse;
use serde_json::Value;
use std::{
    sync::{Mutex, TryLockError},
    time::{Duration, Instant},
};
pub use transport::{Fetch, Https};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ErrorKind {
    Invalid,
    Unavailable,
    Timeout,
    Cancelled,
}
#[derive(Debug)]
pub struct Error {
    pub kind: ErrorKind,
    pub message: String,
}
impl Error {
    pub fn new(kind: ErrorKind, message: impl Into<String>) -> Self {
        Self {
            kind,
            message: message.into(),
        }
    }
}
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}
impl std::error::Error for Error {}
pub type Result<T> = std::result::Result<T, Error>;
pub type Check<'a> = &'a dyn Fn() -> Result<()>;
pub struct Quotation {
    pub rates: Vec<ApiExchangeRateDto>,
    pub selected: Vec<String>,
    pub source: String,
    pub fetched_at: DateTime<Utc>,
    pub cache_minutes: u64,
}
struct Cache {
    url: String,
    at: Instant,
    fetched_at: DateTime<Utc>,
    rates: Vec<ApiExchangeRateDto>,
}
pub struct ExchangeRates<F = Https> {
    fetch: F,
    cache: Mutex<Option<Cache>>,
    refresh: Mutex<()>,
}
impl Default for ExchangeRates<Https> {
    fn default() -> Self {
        Self::new(Https::default())
    }
}
impl<F: Fetch> ExchangeRates<F> {
    pub fn new(fetch: F) -> Self {
        Self {
            fetch,
            cache: Mutex::new(None),
            refresh: Mutex::new(()),
        }
    }
    pub fn get(
        &self,
        configuration: &Value,
        force: bool,
        all: bool,
        now: DateTime<Utc>,
        check: Check<'_>,
    ) -> Result<Quotation> {
        check()?;
        let url = normalize(configuration["url"].as_str().unwrap_or(DEFAULT_URL))?.to_string();
        let minutes = configuration["cacheDurationMinutes"].as_i64().unwrap_or(30);
        if minutes < 0 || minutes > i64::from(i32::MAX) {
            return Err(Error::new(
                ErrorKind::Invalid,
                "汇率缓存时间必须是非负整数。",
            ));
        }
        let mut selected = vec![];
        let configured = configuration["selectedCurrencies"]
            .as_array()
            .filter(|rows| !rows.is_empty())
            .or_else(|| configuration["allSupportedCurrencies"].as_array());
        for name in configured
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
            .map(str::trim)
            .filter(|name| !name.is_empty())
        {
            if name.chars().count() > 80 || selected.len() >= 128 {
                return Err(Error::new(ErrorKind::Invalid, "货币配置超出容量。"));
            }
            if !selected.iter().any(|value| value == name) {
                selected.push(name.to_owned());
            }
        }
        let quote = |cache: &Cache| {
            let rates = if all {
                cache.rates.clone()
            } else {
                selected
                    .iter()
                    .filter_map(|name| {
                        cache
                            .rates
                            .iter()
                            .find(|rate| &rate.currency_name == name)
                            .cloned()
                    })
                    .collect()
            };
            Quotation {
                rates,
                selected: selected.clone(),
                source: url.clone(),
                fetched_at: cache.fetched_at,
                cache_minutes: minutes as u64,
            }
        };
        if !all && selected.is_empty() {
            return Ok(Quotation {
                rates: vec![],
                selected,
                source: url,
                fetched_at: now,
                cache_minutes: minutes as u64,
            });
        }
        if !force {
            let cache = self
                .cache
                .lock()
                .map_err(|_| Error::new(ErrorKind::Unavailable, "汇率缓存状态异常。"))?;
            if let Some(cache) = cache.as_ref().filter(|cache| {
                cache.url == url && cache.at.elapsed() < Duration::from_secs(minutes as u64 * 60)
            }) {
                return Ok(quote(cache));
            }
        }
        let started = Instant::now();
        let _refresh = loop {
            check()?;
            match self.refresh.try_lock() {
                Ok(guard) => break guard,
                Err(TryLockError::Poisoned(_)) => {
                    return Err(Error::new(ErrorKind::Unavailable, "汇率更新状态异常。"));
                }
                Err(TryLockError::WouldBlock) if started.elapsed() < Duration::from_secs(30) => {
                    std::thread::sleep(Duration::from_millis(20))
                }
                Err(_) => return Err(Error::new(ErrorKind::Timeout, "等待汇率更新超时。")),
            }
        };
        {
            let mut cache = self
                .cache
                .lock()
                .map_err(|_| Error::new(ErrorKind::Unavailable, "汇率缓存状态异常。"))?;
            if force {
                *cache = None;
            } else if let Some(cache) = cache.as_ref().filter(|cache| {
                cache.url == url && cache.at.elapsed() < Duration::from_secs(minutes as u64 * 60)
            }) {
                return Ok(quote(cache));
            }
        }
        let html = self.fetch.html(&url, check)?;
        check()?;
        let rates = parse(&html)?;
        let cache = Cache {
            url: url.clone(),
            at: Instant::now(),
            fetched_at: now,
            rates,
        };
        let result = quote(&cache);
        if result.rates.is_empty() {
            return Err(Error::new(
                ErrorKind::Unavailable,
                "汇率源没有返回已配置币种的有效报价。",
            ));
        }
        *self
            .cache
            .lock()
            .map_err(|_| Error::new(ErrorKind::Unavailable, "汇率缓存状态异常。"))? = Some(cache);
        Ok(result)
    }
}

#[cfg(test)]
mod tests;
