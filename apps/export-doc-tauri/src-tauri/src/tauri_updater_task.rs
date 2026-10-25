//! One cancellable update operation, including manifest lookup and bounded download.
use std::{
    future::Future,
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, AtomicU8, Ordering},
        mpsc,
    },
    time::Duration,
};

pub(crate) const CHECK_TIMEOUT: Duration = Duration::from_secs(30);
pub(crate) const DOWNLOAD_TIMEOUT: Duration = Duration::from_secs(15 * 60);
pub(crate) const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);
pub(crate) const READ_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Default)]
pub(crate) struct TauriUpdaterState {
    active: Mutex<Option<Arc<UpdaterCancellation>>>,
}

pub(crate) struct UpdaterLease<'a> {
    state: &'a TauriUpdaterState,
    pub(crate) cancellation: Arc<UpdaterCancellation>,
}

impl Drop for UpdaterLease<'_> {
    fn drop(&mut self) {
        self.cancellation.request_cancel();
        let mut active = self
            .state
            .active
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if active
            .as_ref()
            .is_some_and(|current| Arc::ptr_eq(current, &self.cancellation))
        {
            *active = None;
        }
    }
}

impl TauriUpdaterState {
    pub(crate) fn begin(&self) -> Result<UpdaterLease<'_>, String> {
        let mut active = self
            .active
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if active.is_some() {
            return Err("已有软件更新任务正在运行。".to_owned());
        }
        let cancellation = Arc::new(UpdaterCancellation::new());
        *active = Some(cancellation.clone());
        Ok(UpdaterLease {
            state: self,
            cancellation,
        })
    }

    pub(crate) fn request_cancel(&self) -> bool {
        let active = self
            .active
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone();
        active.is_some_and(|operation| operation.request_cancel())
    }
}

pub(crate) struct UpdaterCancellation {
    phase: AtomicU8,
    abort: Mutex<Option<Box<dyn Fn() + Send + Sync>>>,
}

impl UpdaterCancellation {
    const DOWNLOADING: u8 = 0;
    const CANCEL_REQUESTED: u8 = 1;
    const INSTALLING: u8 = 2;

    fn new() -> Self {
        Self {
            phase: AtomicU8::new(Self::DOWNLOADING),
            abort: Mutex::new(None),
        }
    }

    fn request_cancel(&self) -> bool {
        if self
            .phase
            .compare_exchange(
                Self::DOWNLOADING,
                Self::CANCEL_REQUESTED,
                Ordering::AcqRel,
                Ordering::Acquire,
            )
            .is_err()
        {
            return false;
        }
        if let Some(abort) = self
            .abort
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .take()
        {
            abort();
        }
        true
    }

    fn set_abort(&self, abort: impl Fn() + Send + Sync + 'static) {
        let abort: Box<dyn Fn() + Send + Sync> = Box::new(abort);
        let mut slot = self.abort.lock().unwrap_or_else(|error| error.into_inner());
        if self.is_cancel_requested() {
            drop(slot);
            abort();
        } else {
            *slot = Some(abort);
        }
    }

    pub(crate) fn begin_install(&self) -> bool {
        self.phase
            .compare_exchange(
                Self::DOWNLOADING,
                Self::INSTALLING,
                Ordering::AcqRel,
                Ordering::Acquire,
            )
            .is_ok()
    }

    pub(crate) fn is_cancel_requested(&self) -> bool {
        self.phase.load(Ordering::Acquire) == Self::CANCEL_REQUESTED
    }

    pub(crate) async fn run<T: Send + 'static>(
        &self,
        timeout: Duration,
        operation: impl Future<Output = Result<T, String>> + Send + 'static,
    ) -> Result<T, String> {
        if self.is_cancel_requested() {
            return Err("软件更新操作已取消。".to_owned());
        }
        let task = tauri::async_runtime::spawn(operation);
        let abort = task.inner().abort_handle();
        let abort_on_cancel = abort.clone();
        self.set_abort(move || abort_on_cancel.abort());
        let (finished, receiver) = mpsc::channel();
        let _cleanup = TaskCleanup {
            finished,
            abort: Box::new({
                let abort = abort.clone();
                move || abort.abort()
            }),
        };
        let timed_out = Arc::new(AtomicBool::new(false));
        let deadline = timed_out.clone();
        // A single update lease bounds this to one sleeping deadline thread.
        // It exits on every success, failure, cancellation, or dropped IPC future.
        std::thread::Builder::new()
            .name("desktop-updater-deadline".into())
            .spawn(move || {
                if receiver.recv_timeout(timeout) == Err(mpsc::RecvTimeoutError::Timeout) {
                    deadline.store(true, Ordering::Release);
                    abort.abort();
                }
            })
            .map_err(|cause| format!("无法启动更新超时控制：{cause}"))?;
        let result = task.await;
        self.abort
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .take();
        if self.is_cancel_requested() {
            return Err("软件更新操作已取消。".to_owned());
        }
        if timed_out.load(Ordering::Acquire) {
            return Err("软件更新网络操作超过时限，已停止传输，请稍后重试。".to_owned());
        }
        result.map_err(|cause| format!("软件更新任务失败：{cause}"))?
    }
}

struct TaskCleanup {
    finished: mpsc::Sender<()>,
    abort: Box<dyn Fn() + Send + Sync>,
}

impl Drop for TaskCleanup {
    fn drop(&mut self) {
        let _ = self.finished.send(());
        (self.abort)();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pending_manifest_check_can_be_cancelled_and_the_next_update_can_start() {
        let state = TauriUpdaterState::default();
        let lease = state.begin().unwrap();
        let cancellation = lease.cancellation.clone();
        let (ready, running) = mpsc::channel();
        let task = tauri::async_runtime::spawn(async move {
            cancellation
                .run(Duration::from_secs(3), async move {
                    ready.send(()).unwrap();
                    std::future::pending::<Result<(), String>>().await
                })
                .await
        });
        running.recv_timeout(Duration::from_secs(3)).unwrap();
        assert!(state.request_cancel());
        assert!(
            tauri::async_runtime::block_on(task)
                .unwrap()
                .unwrap_err()
                .contains("取消")
        );
        drop(lease);
        assert!(state.begin().is_ok());
    }

    #[test]
    fn stalled_update_times_out_and_releases_its_future_and_active_slot() {
        let state = TauriUpdaterState::default();
        let lease = state.begin().unwrap();
        let dropped = Arc::new(AtomicBool::new(false));
        struct DropFlag(Arc<AtomicBool>);
        impl Drop for DropFlag {
            fn drop(&mut self) {
                self.0.store(true, Ordering::Release);
            }
        }
        let resource = DropFlag(dropped.clone());
        let result = tauri::async_runtime::block_on(lease.cancellation.run(
            Duration::from_millis(20),
            async move {
                let _resource = resource;
                std::future::pending::<Result<(), String>>().await
            },
        ));
        assert!(result.unwrap_err().contains("超过时限"));
        assert!(dropped.load(Ordering::Acquire));
        drop(lease);
        assert!(state.begin().is_ok());
    }

    #[test]
    fn early_cancellation_and_dropped_leases_do_not_leave_busy_state() {
        let state = TauriUpdaterState::default();
        let lease = state.begin().unwrap();
        assert!(state.begin().is_err());
        assert!(state.request_cancel());
        let result = tauri::async_runtime::block_on(lease.cancellation.run(
            Duration::from_secs(3),
            std::future::pending::<Result<(), String>>(),
        ));
        assert!(result.unwrap_err().contains("取消"));
        drop(lease);
        let lease = state.begin().unwrap();
        assert!(lease.cancellation.begin_install());
        assert!(!state.request_cancel());
        drop(lease);
        assert!(state.begin().is_ok());
    }
}
