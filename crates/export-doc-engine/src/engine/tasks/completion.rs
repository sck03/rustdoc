//! Keep terminal snapshots and publication recovery behind one boundary.
use crate::engine::error::{Result, unavailable};
use std::sync::{RwLock, RwLockReadGuard, RwLockWriteGuard, TryLockError};
use std::time::{Duration, Instant};

#[derive(Default)]
pub(super) struct Completion(RwLock<Option<String>>);

impl Completion {
    pub(super) fn health(&self) -> Result<()> {
        match self.0.try_read() {
            Ok(guard) => match guard.as_ref() {
                Some(failure) => Err(unavailable(failure.clone())),
                None => Ok(()),
            },
            Err(TryLockError::WouldBlock) => Ok(()),
            Err(TryLockError::Poisoned(_)) => Err(unavailable("任务发布状态异常。")),
        }
    }

    pub(super) fn recover(&self, work: impl FnOnce() -> Result<()>) -> Result<()> {
        let mut failure = self.write()?;
        // Only a fully verified recovery may clear the publication latch.
        work()?;
        *failure = None;
        Ok(())
    }

    pub(super) fn finish(&self, work: impl FnOnce() -> Result<()>) -> Result<()> {
        // Terminal persistence must outlive execution cancellation, but lock
        // admission, SQL and output recovery share one independent deadline.
        crate::operation::OperationScope::detached(Duration::from_secs(30)).run(|| {
            let mut failure = self.write()?;
            let result = work();
            if let Err(cause) = &result {
                *failure = Some(format!("任务发布或清理未完成，请重启核对恢复状态：{cause}"));
            }
            result
        })
    }

    fn write(&self) -> Result<RwLockWriteGuard<'_, Option<String>>> {
        let deadline = Instant::now() + Duration::from_secs(30);
        loop {
            crate::operation::check()?;
            match self.0.try_write() {
                Ok(guard) => return Ok(guard),
                Err(TryLockError::Poisoned(_)) => return Err(unavailable("任务发布状态异常。")),
                Err(TryLockError::WouldBlock) if Instant::now() < deadline => {
                    std::thread::sleep(Duration::from_millis(5));
                }
                Err(TryLockError::WouldBlock) => return Err(unavailable("任务发布等待超过时限。")),
            }
        }
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn completion_wait_is_cancellable_and_terminal_work_outlives_cancellation() {
        let completion = Completion::default();
        let reader = completion.read().unwrap();
        let scope = crate::operation::OperationScope::new(Duration::from_millis(25));
        let started = Instant::now();
        assert_eq!(
            scope.run(|| completion.write()).unwrap_err().status,
            Some(504)
        );
        assert!(started.elapsed() < Duration::from_secs(1));
        drop(reader);
        scope.cancel();
        scope
            .run(|| completion.finish(crate::operation::check))
            .unwrap();
        drop(completion.read().unwrap());
    }

    #[test]
    fn only_successful_recovery_reopens_publication() {
        let completion = Completion::default();
        assert!(
            completion
                .finish(|| Err(unavailable("cleanup failed")))
                .is_err()
        );
        assert!(completion.health().is_err());
        assert!(completion.read().is_err());
        completion.finish(|| Ok(())).unwrap();
        assert!(completion.read().is_err());
        assert!(
            completion
                .recover(|| Err(unavailable("still damaged")))
                .is_err()
        );
        assert!(completion.health().is_err());
        completion.recover(|| Ok(())).unwrap();
        completion.health().unwrap();
        drop(completion.read().unwrap());
    }
}
