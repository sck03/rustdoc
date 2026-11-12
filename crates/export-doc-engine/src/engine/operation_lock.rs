//! Notification-based admission for synchronous work with operation cancellation.
//! The standard RwLock owns the data and poison state; no business work holds the wait mutex.
use super::error::{Result, unavailable};
use std::{
    ops::{Deref, DerefMut},
    sync::{Condvar, Mutex, MutexGuard, RwLock, RwLockReadGuard, RwLockWriteGuard, TryLockError},
    time::{Duration, Instant},
};

#[derive(Debug, Default)]
pub(super) struct OperationLock<T> {
    value: RwLock<T>,
    writers: Mutex<usize>,
    released: Condvar,
}
#[derive(Debug)]
pub(super) struct ReadGuard<'a, T> {
    lock: &'a OperationLock<T>,
    value: Option<RwLockReadGuard<'a, T>>,
}
#[derive(Debug)]
pub(super) struct WriteGuard<'a, T> {
    lock: &'a OperationLock<T>,
    value: Option<RwLockWriteGuard<'a, T>>,
}
impl<T> OperationLock<T> {
    pub(super) fn is_poisoned(&self) -> bool {
        self.value.is_poisoned()
    }
    fn writers(&self) -> MutexGuard<'_, usize> {
        // Only the data lock is poisoned by a failed operation. Admission counters
        // are restored before a guard unwinds and hold no business state.
        self.writers
            .lock()
            .unwrap_or_else(|cause| cause.into_inner())
    }
    fn wait<'a>(
        &self,
        writers: MutexGuard<'a, usize>,
        deadline: Instant,
    ) -> Result<MutexGuard<'a, usize>> {
        crate::operation::check()?;
        let remaining = deadline.saturating_duration_since(Instant::now());
        if remaining.is_zero() {
            return Err(unavailable("并发操作等待超过时限。"));
        }
        Ok(self
            .released
            .wait_timeout(writers, remaining.min(Duration::from_millis(100)))
            .unwrap_or_else(|cause| cause.into_inner())
            .0)
    }
    pub(super) fn try_read(&self) -> Result<Option<ReadGuard<'_, T>>> {
        match self.value.try_read() {
            Ok(value) => Ok(Some(ReadGuard {
                lock: self,
                value: Some(value),
            })),
            Err(TryLockError::WouldBlock) => Ok(None),
            Err(TryLockError::Poisoned(_)) => Err(unavailable("共享操作状态异常。")),
        }
    }
    pub(super) fn read(&self, timeout: Duration) -> Result<ReadGuard<'_, T>> {
        let deadline = crate::operation::deadline().map_or_else(
            || Instant::now() + timeout,
            |end| end.min(Instant::now() + timeout),
        );
        let mut writers = self.writers();
        loop {
            crate::operation::check()?;
            // New readers cannot starve an already waiting writer.
            if *writers == 0 {
                if let Some(value) = self.try_read()? {
                    return Ok(value);
                }
            }
            writers = self.wait(writers, deadline)?;
        }
    }
    pub(super) fn write(&self, timeout: Duration) -> Result<WriteGuard<'_, T>> {
        let deadline = crate::operation::deadline().map_or_else(
            || Instant::now() + timeout,
            |end| end.min(Instant::now() + timeout),
        );
        let mut writers = self.writers();
        *writers += 1;
        let result = loop {
            if let Err(cause) = crate::operation::check() {
                break Err(cause);
            }
            match self.value.try_write() {
                Ok(value) => {
                    break Ok(WriteGuard {
                        lock: self,
                        value: Some(value),
                    });
                }
                Err(TryLockError::Poisoned(_)) => break Err(unavailable("共享操作状态异常。")),
                Err(TryLockError::WouldBlock) => {}
            }
            match self.wait(writers, deadline) {
                Ok(next) => writers = next,
                Err(cause) => {
                    writers = self.writers();
                    break Err(cause);
                }
            }
        };
        *writers -= 1;
        self.released.notify_all();
        result
    }
}
impl<T> Deref for ReadGuard<'_, T> {
    type Target = T;
    fn deref(&self) -> &T {
        self.value.as_deref().expect("live read guard")
    }
}
impl<T> Deref for WriteGuard<'_, T> {
    type Target = T;
    fn deref(&self) -> &T {
        self.value.as_deref().expect("live write guard")
    }
}
impl<T> DerefMut for WriteGuard<'_, T> {
    fn deref_mut(&mut self) -> &mut T {
        self.value.as_deref_mut().expect("live write guard")
    }
}
impl<T> Drop for ReadGuard<'_, T> {
    fn drop(&mut self) {
        let _writers = self.lock.writers();
        self.value.take();
        self.lock.released.notify_all();
    }
}
impl<T> Drop for WriteGuard<'_, T> {
    fn drop(&mut self) {
        let _writers = self.lock.writers();
        self.value.take();
        self.lock.released.notify_all();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn waiting_writer_is_cancelled_and_does_not_block_later_readers() {
        let lock = OperationLock::<u32>::default();
        let held = lock.read(Duration::from_secs(1)).unwrap();
        let scope = crate::operation::OperationScope::new(Duration::from_millis(20));
        assert_eq!(
            scope
                .run(|| lock.write(Duration::from_secs(1)))
                .unwrap_err()
                .status,
            Some(504)
        );
        drop(held);
        drop(lock.read(Duration::from_secs(1)).unwrap());
        assert_eq!(*lock.writers(), 0);
    }
    #[test]
    fn writer_poison_remains_closed_and_readers_release_waiters() {
        let lock = OperationLock::<u32>::default();
        let held = lock.read(Duration::from_secs(1)).unwrap();
        std::thread::scope(|threads| {
            let writer = threads.spawn(|| {
                let mut value = lock.write(Duration::from_secs(2)).unwrap();
                *value = 42;
            });
            drop(held);
            writer.join().unwrap();
        });
        assert_eq!(*lock.read(Duration::from_secs(1)).unwrap(), 42);
        let _ = std::panic::catch_unwind(|| {
            let _value = lock.write(Duration::from_secs(1)).unwrap();
            panic!("failed work");
        });
        assert!(lock.is_poisoned());
        assert!(lock.read(Duration::from_secs(1)).is_err());
        assert!(lock.write(Duration::from_secs(1)).is_err());
    }
}
