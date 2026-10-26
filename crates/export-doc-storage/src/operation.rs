//! Caller-supplied cancellation and deadline; no application or host dependency.
use crate::{Error, ErrorKind, Result};
use std::{
    cell::RefCell,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::Instant,
};

#[derive(Clone)]
struct Control {
    cancelled: Arc<AtomicBool>,
    deadline: Instant,
}
thread_local! { static CURRENT: RefCell<Option<Control>> = const { RefCell::new(None) }; }

fn scoped<T>(control: Option<Control>, work: impl FnOnce() -> T) -> T {
    struct Restore(Option<Control>);
    impl Drop for Restore {
        fn drop(&mut self) {
            CURRENT.with(|current| *current.borrow_mut() = self.0.take());
        }
    }
    let _restore = Restore(CURRENT.with(|current| current.replace(control)));
    work()
}
pub fn run<T>(cancelled: Arc<AtomicBool>, deadline: Instant, work: impl FnOnce() -> T) -> T {
    scoped(
        Some(Control {
            cancelled,
            deadline,
        }),
        work,
    )
}
pub(crate) fn without_control<T>(work: impl FnOnce() -> T) -> T {
    scoped(None, work)
}
pub(crate) fn cancelled() -> bool {
    CURRENT.with(|current| {
        current.borrow().as_ref().is_some_and(|control| {
            control.cancelled.load(Ordering::Acquire) || Instant::now() >= control.deadline
        })
    })
}
pub(crate) fn check() -> Result<()> {
    if cancelled() {
        Err(Error::new(ErrorKind::Timeout, "存储操作已取消或超过时限。"))
    } else {
        Ok(())
    }
}
