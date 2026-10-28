//! Keep terminal snapshots and cleanup behind one publication boundary.
use crate::engine::error::{Result, unavailable};
use std::sync::{RwLock, RwLockReadGuard, TryLockError};
use std::time::{Duration, Instant};

#[derive(Default)]
pub(super) struct Completion(RwLock<Option<String>>);

impl Completion {
    pub(super) fn finish(&self, work: impl FnOnce() -> Result<()>) -> Result<()> {
        let mut failure = self
            .0
            .write()
            .map_err(|_| unavailable("任务发布状态异常。"))?;
        let result = work();
        if let Err(cause) = &result {
            *failure = Some(format!("任务发布或清理未完成，请重启核对恢复状态：{cause}"));
        }
        result
    }

    pub(super) fn read(&self) -> Result<RwLockReadGuard<'_, Option<String>>> {
        let deadline = Instant::now() + Duration::from_secs(30);
        loop {
            crate::operation::check()?;
            match self.0.try_read() {
                Ok(guard) => {
                    if let Some(failure) = guard.as_ref() {
                        return Err(unavailable(failure.clone()));
                    }
                    return Ok(guard);
                }
                Err(TryLockError::Poisoned(_)) => return Err(unavailable("任务发布状态异常。")),
                Err(TryLockError::WouldBlock) if Instant::now() < deadline => {
                    std::thread::sleep(Duration::from_millis(5));
                }
                Err(TryLockError::WouldBlock) => {
                    return Err(unavailable("任务正在完成输出清理，请稍后重试。"));
                }
            }
        }
    }
}
