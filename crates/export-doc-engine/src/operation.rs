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
}

pub fn check() -> Result<(), ApiError> {
    CURRENT.with(|current| {
        if current.borrow().as_ref().is_some_and(|scope| {
            scope.cancelled.load(Ordering::Acquire) || Instant::now() >= scope.deadline
        }) {
            return Err(ApiError {
                status: Some(504),
                message: "操作已取消或超过时限，未提交后续修改。".into(),
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
