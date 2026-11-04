//! A request's deadline and cancellation follow synchronous application work.
//! Transactions check the scope before commit, including after a disconnected
//! HTTP request has dropped its waiter.
use crate::api::ApiError;
use std::{
    cell::RefCell,
    sync::{
        Arc, Mutex, Weak,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};

#[derive(Clone)]
pub struct OperationScope {
    cancelled: Arc<Cancellation>,
    deadline: Instant,
    parents: Vec<Arc<Cancellation>>,
    detached: bool,
    source: Option<std::net::IpAddr>,
}

#[derive(Default)]
struct Cancellation {
    flag: Arc<AtomicBool>,
    children: Mutex<Vec<Weak<AtomicBool>>>,
}
impl Cancellation {
    fn link(&self, child: &Arc<AtomicBool>) {
        let mut children = self
            .children
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        children.retain(|child| child.strong_count() > 0);
        let child_ref = Arc::downgrade(child);
        if !children.iter().any(|entry| entry.ptr_eq(&child_ref)) {
            children.push(child_ref);
        }
        if self.flag.load(Ordering::Acquire) {
            child.store(true, Ordering::Release);
        }
    }
    fn cancel(&self) {
        self.flag.store(true, Ordering::Release);
        // Optional libraries and pipe threads receive only an atomic flag. Fan out
        // synchronously so they observe cancellation without calling `check`.
        for child in self
            .children
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .iter()
            .filter_map(Weak::upgrade)
        {
            child.store(true, Ordering::Release);
        }
    }
}

thread_local! { static CURRENT: RefCell<Option<OperationScope>> = const { RefCell::new(None) }; }

impl OperationScope {
    pub fn new(timeout: Duration) -> Self {
        Self {
            cancelled: Arc::new(Cancellation::default()),
            deadline: Instant::now() + timeout,
            parents: Vec::new(),
            detached: false,
            source: None,
        }
    }
    /// Compensation and durable post-commit cleanup have their own bounded
    /// lifetime. Ordinary nested work must use `new` instead.
    pub fn detached(timeout: Duration) -> Self {
        Self {
            detached: true,
            ..Self::new(timeout)
        }
    }
    pub fn with_source(mut self, source: std::net::IpAddr) -> Self {
        self.source = Some(source);
        self
    }
    pub fn cancel(&self) {
        self.cancelled.cancel();
    }
    pub fn cancellation_flag(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.cancelled.flag)
    }
    pub fn run<T>(&self, operation: impl FnOnce() -> T) -> T {
        struct Restore(Option<OperationScope>);
        impl Drop for Restore {
            fn drop(&mut self) {
                CURRENT.with(|current| *current.borrow_mut() = self.0.take());
            }
        }
        let mut scope = self.clone();
        if !scope.detached {
            CURRENT.with(|current| {
                if let Some(parent) = current.borrow().as_ref() {
                    scope.source = scope.source.or(parent.source);
                    scope.deadline = scope.deadline.min(parent.deadline);
                    scope.parents.extend(parent.parents.iter().cloned());
                    if !Arc::ptr_eq(&scope.cancelled, &parent.cancelled) {
                        scope.parents.push(parent.cancelled.clone());
                    }
                    for ancestor in &scope.parents {
                        ancestor.link(&scope.cancelled.flag);
                    }
                }
            });
        }
        let restore = Restore(CURRENT.with(|current| current.replace(Some(scope.clone()))));
        let result = if scope.detached {
            export_doc_storage::operation::run_detached(
                scope.cancelled.flag.clone(),
                scope.deadline,
                operation,
            )
        } else {
            export_doc_storage::operation::run(
                scope.cancelled.flag.clone(),
                scope.deadline,
                operation,
            )
        };
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
            scope.cancelled.flag.load(Ordering::Acquire)
                || scope
                    .parents
                    .iter()
                    .any(|signal| signal.flag.load(Ordering::Acquire))
                || Instant::now() >= scope.deadline
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
/// Actual transport peer supplied by the HTTP host, never a forwarded header.
pub fn source() -> Option<std::net::IpAddr> {
    CURRENT.with(|current| current.borrow().as_ref().and_then(|scope| scope.source))
}

#[cfg(test)]
#[path = "tests/operation.rs"]
mod tests;
