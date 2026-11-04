//! Shared tick isolation and bounded recovery; lifecycle remains with each owner.
use super::error::{Result, unavailable};
use serde_json::{Value, json};
use std::sync::Mutex;
#[derive(Clone, Default)]
struct Snapshot {
    ticks: u64,
    last_error: Option<u16>,
    last_success: Option<String>,
    panics: u64,
    consecutive_panics: u32,
    blocked: bool,
}
#[derive(Default)]
pub(super) struct Health(Mutex<Snapshot>);
impl Health {
    pub(super) fn run<T>(
        &self,
        work: impl FnOnce() -> Result<T>,
        safe: impl FnOnce() -> bool,
    ) -> Result<T> {
        self.check()?;
        let result =
            std::panic::catch_unwind(std::panic::AssertUnwindSafe(work)).unwrap_or_else(|_| {
                let mut error = unavailable("调度执行异常终止。");
                error.diagnostic = Some("scheduler-panic");
                Err(error)
            });
        let mut state = self
            .0
            .lock()
            .map_err(|_| unavailable("调度健康状态异常。"))?;
        state.ticks += 1;
        state.last_error = result.as_ref().err().map(|e| e.status.unwrap_or(503));
        let panicked = result
            .as_ref()
            .err()
            .is_some_and(|e| matches!(e.diagnostic, Some("operation-panic" | "scheduler-panic")));
        if panicked {
            state.panics += 1;
            state.consecutive_panics += 1;
            state.blocked = !safe() || state.consecutive_panics >= 3;
        } else if result.is_ok() {
            state.consecutive_panics = 0;
            state.last_success = Some(super::store::timestamp());
        }
        result
    }
    pub(super) fn check(&self) -> Result<()> {
        if self
            .0
            .lock()
            .map_err(|_| unavailable("调度健康状态异常。"))?
            .blocked
        {
            return Err(unavailable(
                "调度异常无法安全恢复或已达到重试上限，请核对数据库并重启。",
            ));
        }
        Ok(())
    }
    pub(super) fn metrics(&self) -> Value {
        let state = self
            .0
            .lock()
            .map(|state| state.clone())
            .unwrap_or(Snapshot {
                blocked: true,
                last_error: Some(503),
                ..Default::default()
            });
        json!({"ticks":state.ticks,"lastErrorStatus":state.last_error,
            "lastSuccessAt":state.last_success,"panics":state.panics,
            "consecutivePanics":state.consecutive_panics,"blocked":state.blocked})
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn panic_is_observable_safe_ticks_resume_and_unsafe_or_repeated_panics_block() {
        let health = Health::default();
        assert!(
            health
                .run::<()>(|| panic!("injected tick"), || true)
                .is_err()
        );
        health.run(|| Ok(()), || true).unwrap();
        assert_eq!(health.metrics()["panics"], 1);
        assert!(health.metrics()["lastSuccessAt"].is_string());
        assert!(
            health
                .run::<()>(|| panic!("unsafe tick"), || false)
                .is_err()
        );
        assert!(health.check().is_err());
        let repeated = Health::default();
        for _ in 0..3 {
            assert!(
                repeated
                    .run::<()>(|| panic!("repeated tick"), || true)
                    .is_err()
            );
        }
        assert!(repeated.check().is_err());
    }
}
