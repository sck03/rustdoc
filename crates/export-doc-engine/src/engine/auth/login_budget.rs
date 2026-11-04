//! Reserve all login budgets atomically; password work runs outside this lock.
use crate::engine::error::{Result, error, unavailable};
use std::{
    collections::HashMap,
    net::IpAddr,
    sync::Mutex,
    time::{Duration, Instant},
};
const WINDOW: Duration = Duration::from_secs(300);
#[derive(Default)]
struct Counter {
    active: u32,
    failures: u32,
    last: Option<Instant>,
}
impl Counter {
    fn expire(&mut self) {
        if self.last.is_some_and(|last| last.elapsed() >= WINDOW) {
            self.failures = 0;
            self.last = None;
        }
    }
    fn allowed(&self, failures: u32, active: u32) -> bool {
        self.failures + self.active < failures && self.active < active
    }
    fn finish(&mut self, failed: bool) {
        self.active -= 1;
        if failed {
            self.failures += 1;
            self.last = Some(Instant::now());
        }
    }
}
#[derive(Default)]
struct State {
    users: HashMap<String, Counter>,
    sources: HashMap<IpAddr, Counter>,
    global: Counter,
}
#[derive(Default)]
pub(super) struct Budget(Mutex<State>);
pub(super) struct Reservation<'a> {
    budget: &'a Budget,
    user: String,
    source: Option<IpAddr>,
    done: bool,
}
impl Budget {
    pub(super) fn reserve(&self, user: &str, source: Option<IpAddr>) -> Result<Reservation<'_>> {
        let mut state = self.0.lock().map_err(|_| unavailable("登录状态异常。"))?;
        state.global.expire();
        state.users.retain(|_, count| {
            count.expire();
            count.active > 0 || count.failures > 0
        });
        state.sources.retain(|_, count| {
            count.expire();
            count.active > 0 || count.failures > 0
        });
        if !state.global.allowed(120, 16)
            || state
                .users
                .get(user)
                .is_some_and(|count| !count.allowed(10, 10))
            || source.is_some_and(|source| {
                state
                    .sources
                    .get(&source)
                    .is_some_and(|count| !count.allowed(40, 8))
            })
            || state.users.len() >= 1024 && !state.users.contains_key(user)
            || source.is_some_and(|source| {
                state.sources.len() >= 1024 && !state.sources.contains_key(&source)
            })
        {
            return Err(error(429, "登录尝试过多，请稍后重试。"));
        }
        state.global.active += 1;
        state.users.entry(user.into()).or_default().active += 1;
        if let Some(source) = source {
            state.sources.entry(source).or_default().active += 1;
        }
        Ok(Reservation {
            budget: self,
            user: user.into(),
            source,
            done: false,
        })
    }
}
impl Reservation<'_> {
    pub(super) fn finish(mut self, success: bool) -> Result<()> {
        self.release(Some(success))?;
        self.done = true;
        Ok(())
    }
    fn release(&self, result: Option<bool>) -> Result<()> {
        let mut state = self
            .budget
            .0
            .lock()
            .map_err(|_| unavailable("登录状态异常。"))?;
        let failed = result == Some(false);
        state.global.finish(failed);
        if let Some(count) = state.users.get_mut(&self.user) {
            count.finish(failed);
            if result == Some(true) {
                count.failures = 0;
                count.last = None;
            }
        }
        if let Some(source) = self.source {
            if let Some(count) = state.sources.get_mut(&source) {
                count.finish(failed);
            }
        }
        Ok(())
    }
}
impl Drop for Reservation<'_> {
    fn drop(&mut self) {
        if !self.done {
            let _ = self.release(None);
        }
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn concurrent_reservations_never_overrun_username_or_source_and_errors_release() {
        let budget = Budget::default();
        let peer = "127.0.0.1".parse().unwrap();
        let permits: Vec<_> = (0..8)
            .map(|_| budget.reserve("user", Some(peer)).unwrap())
            .collect();
        assert_eq!(
            budget.reserve("another", Some(peer)).err().unwrap().status,
            Some(429)
        );
        drop(permits);
        let permits: Vec<_> = (0..10)
            .map(|_| budget.reserve("user", None).unwrap())
            .collect();
        assert_eq!(
            budget.reserve("user", None).err().unwrap().status,
            Some(429)
        );
        for permit in permits {
            permit.finish(false).unwrap();
        }
        assert_eq!(
            budget.reserve("user", None).err().unwrap().status,
            Some(429)
        );
        budget.0.lock().unwrap().users.get_mut("user").unwrap().last =
            Some(Instant::now() - WINDOW);
        budget.reserve("user", None).unwrap().finish(true).unwrap();
        assert_eq!(budget.0.lock().unwrap().global.active, 0);
    }
}
