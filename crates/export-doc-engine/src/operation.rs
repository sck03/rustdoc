//! A request's deadline and cancellation follow synchronous application work.
//! Transactions check the scope before commit, including after a disconnected
//! HTTP request has dropped its waiter.
use crate::api::ApiError;
use std::{
    cell::RefCell,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};

#[derive(Clone)]
pub struct OperationScope {
    cancelled: Arc<AtomicBool>,
    deadline: Instant,
}

thread_local! { static CURRENT: RefCell<Option<OperationScope>> = const { RefCell::new(None) }; }

impl OperationScope {
    pub fn new(timeout: Duration) -> Self {
        Self {
            cancelled: Arc::new(AtomicBool::new(false)),
            deadline: Instant::now() + timeout,
        }
    }
    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::Release);
    }
    pub fn cancellation_flag(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.cancelled)
    }
    pub fn run<T>(&self, operation: impl FnOnce() -> T) -> T {
        struct Restore(Option<OperationScope>);
        impl Drop for Restore {
            fn drop(&mut self) {
                CURRENT.with(|current| *current.borrow_mut() = self.0.take());
            }
        }
        let restore = Restore(CURRENT.with(|current| current.replace(Some(self.clone()))));
        let result = operation();
        drop(restore);
        result
    }

    /// Preserve the execution boundary even when an optional capability erases
    /// the structured cancellation error while converting its own result.
    pub fn run_result<T>(
        &self,
        operation: impl FnOnce() -> Result<T, ApiError>,
    ) -> Result<T, ApiError> {
        self.run(|| {
            check()?;
            let result = operation();
            check()?;
            result
        })
    }
}

pub fn check() -> Result<(), ApiError> {
    CURRENT.with(|current| {
        if current.borrow().as_ref().is_some_and(|scope| {
            scope.cancelled.load(Ordering::Acquire) || Instant::now() >= scope.deadline
        }) {
            return Err(ApiError {
                status: Some(504),
                message: "操作已取消或超过时限，未提交后续修改。".into(),
                diagnostic: Some("cancelled-or-timeout"),
            });
        }
        Ok(())
    })
}

pub fn cancellation_flag() -> Arc<AtomicBool> {
    CURRENT.with(|current| {
        current
            .borrow()
            .as_ref()
            .map(OperationScope::cancellation_flag)
            .unwrap_or_else(|| Arc::new(AtomicBool::new(false)))
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn result_boundary_preserves_cancellation_after_an_adapter_erases_the_error() {
        let scope = OperationScope::new(Duration::from_secs(30));
        let result: Result<(), ApiError> = scope.run_result(|| {
            scope.cancel();
            Err(ApiError {
                status: Some(400),
                message: "stringified cancellation".into(),
                diagnostic: None,
            })
        });
        assert_eq!(result.unwrap_err().status, Some(504));
        check().unwrap(); // The caller's scope is restored on error too.
    }

    #[test]
    fn expired_work_does_not_start_and_genuine_validation_errors_keep_their_category() {
        let mut started = false;
        let result = OperationScope::new(Duration::ZERO).run_result(|| {
            started = true;
            Ok(())
        });
        assert!(!started);
        assert_eq!(result.unwrap_err().status, Some(504));
        let result: Result<(), ApiError> =
            OperationScope::new(Duration::from_secs(30)).run_result(|| {
                Err(ApiError {
                    status: Some(400),
                    message: "invalid format".into(),
                    diagnostic: None,
                })
            });
        assert_eq!(result.unwrap_err().status, Some(400));
    }
}
