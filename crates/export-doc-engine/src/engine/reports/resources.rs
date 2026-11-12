//! One heavyweight conversion per process bounds simultaneous SVG trees/PDF encoders.
//! The guard spans layout and encoding; waiting never owns a database connection.
use crate::engine::error::{Result, error, unavailable};
use std::{
    sync::{Condvar, Mutex},
    time::{Duration, Instant},
};

#[derive(Debug, Default)]
struct RenderGate {
    state: Mutex<(bool, bool)>, // occupied, poisoned by a panicking renderer
    released: Condvar,
}
static RENDERING: RenderGate = RenderGate {
    state: Mutex::new((false, false)),
    released: Condvar::new(),
};
#[derive(Debug)]
pub(in crate::engine) struct RenderPermit<'a>(&'a RenderGate);
impl Drop for RenderPermit<'_> {
    fn drop(&mut self) {
        let mut state = self.0.state.lock().unwrap_or_else(|e| e.into_inner());
        state.0 = false;
        state.1 |= std::thread::panicking();
        self.0.released.notify_one();
    }
}
pub(in crate::engine) const PREVIEW_BYTES: usize = 32 * 1024 * 1024;

pub(in crate::engine) fn acquire() -> Result<RenderPermit<'static>> {
    acquire_from(&RENDERING, Instant::now() + Duration::from_secs(5))
}

/// Accepted jobs already have bounded worker admission, cancellation and a deadline.
pub(in crate::engine) fn acquire_for_job() -> Result<RenderPermit<'static>> {
    acquire_in_scope(&RENDERING)
}
fn acquire_in_scope(lock: &RenderGate) -> Result<RenderPermit<'_>> {
    let deadline =
        crate::operation::deadline().ok_or_else(|| unavailable("后台渲染缺少任务执行期限。"))?;
    acquire_from(lock, deadline)
}
fn acquire_from(lock: &RenderGate, deadline: Instant) -> Result<RenderPermit<'_>> {
    let deadline = crate::operation::deadline().map_or(deadline, |end| end.min(deadline));
    let mut state = lock
        .state
        .lock()
        .map_err(|_| unavailable("报表渲染状态异常。"))?;
    loop {
        crate::operation::check()?;
        if state.1 {
            return Err(unavailable("报表渲染状态异常。"));
        }
        if !state.0 {
            state.0 = true;
            return Ok(RenderPermit(lock));
        }
        let remaining = deadline.saturating_duration_since(Instant::now());
        if remaining.is_zero() {
            return Err(error(429, "报表处理繁忙，请稍后重试。"));
        }
        // Release wakes immediately; cancellation is observed within 100 ms.
        state = lock
            .released
            .wait_timeout(state, remaining.min(Duration::from_millis(100)))
            .map_err(|_| unavailable("报表渲染状态异常。"))?
            .0;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn cancellation_while_waiting_releases_no_other_renderers_capacity() {
        let gate = RenderGate::default();
        let held = acquire_from(&gate, Instant::now() + Duration::from_secs(5)).unwrap();
        let scope = crate::operation::OperationScope::new(Duration::from_secs(5));
        std::thread::scope(|threads| {
            let worker = threads.spawn(|| scope.run(|| acquire_in_scope(&gate).map(drop)));
            scope.cancel();
            assert_eq!(worker.join().unwrap().unwrap_err().status, Some(504));
        });
        assert!(gate.state.lock().unwrap().0);
        drop(held);
        assert!(!gate.state.lock().unwrap().0);
    }
    #[test]
    fn waiting_conversion_honors_deadline_and_releases_capacity() {
        let lock = RenderGate::default();
        let held = acquire_from(&lock, Instant::now() + Duration::from_secs(5)).unwrap();
        let started = Instant::now();
        let scope = crate::operation::OperationScope::new(Duration::from_millis(25));
        assert_eq!(
            scope.run(|| acquire_in_scope(&lock)).unwrap_err().status,
            Some(504)
        );
        assert!(started.elapsed() < Duration::from_secs(1));
        drop(held);
        drop(acquire_from(&lock, Instant::now() + Duration::from_secs(5)).unwrap());
    }
    #[test]
    fn accepted_job_waits_past_preview_budget_and_missing_scope_is_rejected() {
        let lock = RenderGate::default();
        assert_eq!(acquire_in_scope(&lock).unwrap_err().status, Some(503));
        let held = acquire_from(&lock, Instant::now() + Duration::from_secs(5)).unwrap();
        std::thread::scope(|threads| {
            let waiter = threads.spawn(|| {
                crate::operation::OperationScope::new(Duration::from_secs(15))
                    .run_result(|| acquire_in_scope(&lock).map(drop))
            });
            std::thread::sleep(Duration::from_millis(5200));
            drop(held);
            waiter.join().unwrap().unwrap();
        });
    }
}
