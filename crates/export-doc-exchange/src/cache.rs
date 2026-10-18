use crate::{Check, Configuration, Error, ErrorKind, Fetch, Https, Result, Source, parser};
use chrono::{DateTime, Utc};
use export_doc_contracts::generated_api::ApiExchangeRateDto;
use std::{
    sync::{Mutex, TryLockError},
    time::{Duration, Instant},
};

pub struct Quotation {
    pub rates: Vec<ApiExchangeRateDto>,
    pub available: Vec<String>,
    pub missing: Vec<String>,
    pub fetched_at: DateTime<Utc>,
    pub last_attempt_at: Option<DateTime<Utc>>,
    pub next_refresh_at: Option<DateTime<Utc>>,
    pub is_stale: bool,
    pub last_error: String,
}
struct Cache {
    source: Source,
    fetched_at: DateTime<Utc>,
    rates: Vec<ApiExchangeRateDto>,
}
struct Attempt {
    source: Source,
    at: DateTime<Utc>,
    retry_at: DateTime<Utc>,
    failures: u32,
    error: Option<Error>,
}
#[derive(Default)]
struct State {
    cache: Option<Cache>,
    attempt: Option<Attempt>,
}
impl State {
    fn quote(&self, config: &Configuration, all: bool, now: DateTime<Utc>) -> Option<Quotation> {
        let cache = self
            .cache
            .as_ref()
            .filter(|cache| cache.source == config.source)?;
        let attempt = self
            .attempt
            .as_ref()
            .filter(|attempt| attempt.source == config.source);
        let due = cache.fetched_at + chrono::Duration::minutes(i64::from(config.minutes));
        let available: Vec<_> = cache
            .rates
            .iter()
            .map(|rate| rate.currency_name.clone())
            .collect();
        let missing = config
            .selected
            .iter()
            .filter(|name| !available.contains(name))
            .cloned()
            .collect();
        Some(Quotation {
            rates: if all {
                cache.rates.clone()
            } else {
                config
                    .selected
                    .iter()
                    .filter_map(|name| {
                        cache
                            .rates
                            .iter()
                            .find(|rate| &rate.currency_name == name)
                            .cloned()
                    })
                    .collect()
            },
            available,
            missing,
            fetched_at: cache.fetched_at,
            last_attempt_at: attempt.map(|a| a.at),
            next_refresh_at: config.automatic.then(|| {
                attempt
                    .filter(|a| a.error.is_some())
                    .map_or(due, |a| a.retry_at)
            }),
            is_stale: now >= due || attempt.is_some_and(|a| a.error.is_some()),
            last_error: attempt
                .and_then(|a| a.error.as_ref())
                .map(|e| e.message.clone())
                .unwrap_or_default(),
        })
    }
    fn reusable(
        &self,
        config: &Configuration,
        force: bool,
        all: bool,
        now: DateTime<Utc>,
    ) -> Option<Result<Quotation>> {
        let attempt = self.attempt.as_ref().filter(|a| a.source == config.source);
        let quote = self.quote(config, all, now);
        if let Some(attempt) = attempt.filter(|a| now < a.retry_at) {
            return match quote {
                Some(quote) => Some(Ok(quote)),
                None => attempt.error.clone().map(Err),
            };
        }
        quote.filter(|q| !force && !q.is_stale).map(Ok)
    }
}

pub struct ExchangeRates<F = Https> {
    pub(crate) fetch: F,
    state: Mutex<State>,
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
            state: Mutex::new(State::default()),
            refresh: Mutex::new(()),
        }
    }
    pub fn next_update(&self, config: &Configuration, now: DateTime<Utc>) -> Result<DateTime<Utc>> {
        let state = self.state.lock().map_err(state_error)?;
        if let Some(attempt) = state
            .attempt
            .as_ref()
            .filter(|a| a.source == config.source && a.error.is_some())
        {
            return Ok(attempt.retry_at.max(now));
        }
        Ok(state
            .cache
            .as_ref()
            .filter(|c| c.source == config.source)
            .map_or(now, |c| {
                (c.fetched_at + chrono::Duration::minutes(i64::from(config.minutes))).max(now)
            }))
    }
    pub fn get(
        &self,
        config: &Configuration,
        force: bool,
        all: bool,
        now: DateTime<Utc>,
        check: Check<'_>,
    ) -> Result<Quotation> {
        check()?;
        if let Some(result) = self
            .state
            .lock()
            .map_err(state_error)?
            .reusable(config, force, all, now)
        {
            return result;
        }
        let started = Instant::now();
        let _refresh = loop {
            check()?;
            match self.refresh.try_lock() {
                Ok(guard) => break guard,
                Err(TryLockError::Poisoned(_)) => return Err(state_error(())),
                Err(TryLockError::WouldBlock) if started.elapsed() < Duration::from_secs(35) => {
                    std::thread::sleep(Duration::from_millis(20));
                }
                Err(_) => return Err(Error::new(ErrorKind::Timeout, "等待汇率更新超时。")),
            }
        };
        if let Some(result) = self
            .state
            .lock()
            .map_err(state_error)?
            .reusable(config, force, all, now)
        {
            return result;
        }
        let result = self
            .fetch
            .html(crate::banks::endpoint(&config.source), check)
            .and_then(|body| {
                check()?;
                parser::parse_source(&body, config.source.format)
            });
        check()?;
        let completed = now + chrono::Duration::from_std(started.elapsed()).unwrap_or_default();
        let mut state = self.state.lock().map_err(state_error)?;
        let mut attempt = Attempt {
            source: config.source.clone(),
            at: completed,
            retry_at: completed + chrono::Duration::seconds(60),
            failures: 0,
            error: None,
        };
        match result {
            Ok(rates) => {
                state.cache = Some(Cache {
                    source: config.source.clone(),
                    fetched_at: completed,
                    rates,
                })
            }
            Err(cause) => {
                if cause.kind == ErrorKind::Cancelled {
                    return Err(cause);
                }
                attempt.failures = state
                    .attempt
                    .as_ref()
                    .filter(|a| a.source == config.source)
                    .map_or(1, |a| a.failures.saturating_add(1));
                let backoff =
                    Duration::from_secs((300u64 << (attempt.failures - 1).min(4)).min(3600));
                let delay = cause.retry_after.unwrap_or_default().max(backoff);
                attempt.retry_at = completed
                    .checked_add_signed(
                        chrono::Duration::from_std(delay).unwrap_or(chrono::Duration::MAX),
                    )
                    .unwrap_or(DateTime::<Utc>::MAX_UTC);
                attempt.error = Some(cause);
            }
        }
        state.attempt = Some(attempt);
        state.quote(config, all, completed).ok_or_else(|| {
            state
                .attempt
                .as_ref()
                .and_then(|a| a.error.clone())
                .unwrap_or_else(|| state_error(()))
        })
    }
}
fn state_error(_: impl std::fmt::Debug) -> Error {
    Error::new(ErrorKind::Unavailable, "汇率缓存状态异常。")
}
