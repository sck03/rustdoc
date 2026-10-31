//! One workflow schedule worker shares the existing task lifetime and cancellation.
use super::super::{
    NativeService,
    error::{Result, unavailable},
    oa,
};
use crate::operation::OperationScope;
use std::{
    sync::{Arc, Condvar, Mutex, Weak},
    thread::JoinHandle,
    time::Duration,
};
struct Worker {
    signal: Arc<(Mutex<bool>, Condvar)>,
    handle: JoinHandle<()>,
}
#[derive(Default)]
pub(crate) struct Scheduler {
    worker: Mutex<Option<Worker>>,
    state: Mutex<(u64, Option<u16>)>,
}
impl Scheduler {
    pub(crate) fn bind(service: &Arc<NativeService>) -> Result<()> {
        if service.store.edition != export_doc_domain::permissions::ProductEdition::Full {
            return Ok(());
        }
        let weak: Weak<NativeService> = Arc::downgrade(service);
        let signal = Arc::new((Mutex::new(false), Condvar::new()));
        let stop = signal.clone();
        let handle = std::thread::Builder::new()
            .name("exportdoc-workflow-schedule".into())
            .spawn(move || {
                loop {
                    if *stop.0.lock().unwrap_or_else(|p| p.into_inner()) {
                        break;
                    }
                    let Some(service) = weak.upgrade() else {
                        break;
                    };
                    if let Ok(_guard) = service.maintenance.try_read() {
                        let scope = OperationScope::new(Duration::from_secs(30));
                        let result =
                            scope.run_result(|| oa::runtime::tick(&service, chrono::Utc::now()));
                        let mut state = service
                            .workflow_scheduler
                            .state
                            .lock()
                            .unwrap_or_else(|p| p.into_inner());
                        state.0 += 1;
                        state.1 = result.err().map(|e| e.status.unwrap_or(503));
                    }
                    drop(service);
                    let guard = stop.0.lock().unwrap_or_else(|p| p.into_inner());
                    let (guard, _) = stop
                        .1
                        .wait_timeout(guard, Duration::from_secs(60))
                        .unwrap_or_else(|p| p.into_inner());
                    if *guard {
                        break;
                    }
                }
            })?;
        *service
            .workflow_scheduler
            .worker
            .lock()
            .map_err(|_| unavailable("流程调度器状态异常。"))? = Some(Worker { signal, handle });
        Ok(())
    }
    pub(crate) fn request_stop(&self) -> Result<()> {
        if let Some(worker) = self
            .worker
            .lock()
            .map_err(|_| unavailable("流程调度器状态异常。"))?
            .as_ref()
        {
            *worker
                .signal
                .0
                .lock()
                .map_err(|_| unavailable("流程调度器停止状态异常。"))? = true;
            worker.signal.1.notify_all();
        }
        Ok(())
    }
    pub(crate) fn stop(&self) -> Result<()> {
        self.request_stop()?;
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("流程调度器状态异常。"))?
            .take();
        if let Some(worker) = worker {
            if worker.handle.thread().id() != std::thread::current().id() {
                worker
                    .handle
                    .join()
                    .map_err(|_| unavailable("流程调度线程异常退出。"))?;
            }
        }
        Ok(())
    }
    pub(crate) fn metrics(&self) -> serde_json::Value {
        let state = self.state.lock().unwrap_or_else(|p| p.into_inner());
        serde_json::json!({"ticks":state.0,"lastErrorStatus":state.1})
    }
}
impl Drop for Scheduler {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}
