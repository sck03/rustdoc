//! One heavyweight conversion per process bounds simultaneous SVG trees/PDF encoders.
//! The guard spans layout and encoding; waiting never owns a database connection.
use crate::engine::error::{Result, error, unavailable};
use std::{
    sync::{Mutex, MutexGuard, TryLockError},
    time::{Duration, Instant},
};

static RENDERING: Mutex<()> = Mutex::new(());
pub(in crate::engine) const PREVIEW_BYTES: usize = 32 * 1024 * 1024;

pub(in crate::engine) fn acquire() -> Result<MutexGuard<'static, ()>> {
    acquire_from(&RENDERING)
}

fn acquire_from(lock: &Mutex<()>) -> Result<MutexGuard<'_, ()>> {
    let deadline = Instant::now() + Duration::from_secs(5);
    loop {
        crate::operation::check()?;
        match lock.try_lock() {
            Ok(guard) => return Ok(guard),
            Err(TryLockError::Poisoned(_)) => return Err(unavailable("报表渲染状态异常。")),
            Err(TryLockError::WouldBlock) if Instant::now() < deadline => {
                std::thread::sleep(Duration::from_millis(5));
            }
            Err(TryLockError::WouldBlock) => return Err(error(429, "报表处理繁忙，请稍后重试。")),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn waiting_conversion_honors_deadline_and_releases_capacity() {
        let lock = Mutex::new(());
        let held = lock.lock().unwrap();
        let started = Instant::now();
        let scope = crate::operation::OperationScope::new(Duration::from_millis(25));
        assert_eq!(
            scope.run(|| acquire_from(&lock)).unwrap_err().status,
            Some(504)
        );
        assert!(started.elapsed() < Duration::from_secs(1));
        drop(held);
        drop(acquire_from(&lock).unwrap());
    }
}
