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
#[derive(Default)]
struct Control {
    stopping: bool,
    active: Option<OperationScope>,
}
type Signal = Arc<(Mutex<Control>, Condvar)>;
struct Worker {
    signal: Signal,
    handle: JoinHandle<()>,
}
#[derive(Default)]
pub(crate) struct Scheduler {
    worker: Mutex<Option<Worker>>,
    state: crate::engine::scheduler_health::Health,
}
impl Scheduler {
    pub(crate) fn bind(service: &Arc<NativeService>) -> Result<()> {
        if service.store.edition != export_doc_domain::permissions::ProductEdition::Full {
            return Ok(());
        }
        let weak: Weak<NativeService> = Arc::downgrade(service);
        let signal = Arc::new((Mutex::new(Control::default()), Condvar::new()));
        let stop = signal.clone();
        let handle = std::thread::Builder::new()
            .name("exportdoc-workflow-schedule".into())
            .spawn(move || {
                loop {
                    if stop.0.lock().unwrap_or_else(|p| p.into_inner()).stopping {
                        break;
                    }
                    let Some(service) = weak.upgrade() else {
                        break;
                    };
                    if let Ok(_guard) = service.maintenance.try_read() {
                        let scope = OperationScope::new(Duration::from_secs(30));
                        {
                            let mut control = stop.0.lock().unwrap_or_else(|p| p.into_inner());
                            if control.stopping {
                                break;
                            }
                            control.active = Some(scope.clone());
                        }
                        let _result = service.workflow_scheduler.state.run(
                            || scope.run_result(|| oa::runtime::tick(&service, chrono::Utc::now())),
                            || service.store.health().is_ok(),
                        );
                        stop.0.lock().unwrap_or_else(|p| p.into_inner()).active = None;
                        if service.workflow_scheduler.state.check().is_err() {
                            break;
                        }
                    }
                    drop(service);
                    if wait_for_stop(&stop, Duration::from_secs(60)) {
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
            let mut control = worker
                .signal
                .0
                .lock()
                .map_err(|_| unavailable("流程调度器停止状态异常。"))?;
            control.stopping = true;
            if let Some(scope) = &control.active {
                scope.cancel();
            }
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
        let mut metrics = self.state.metrics();
        metrics["threadAlive"] = serde_json::json!(self.worker.lock().is_ok_and(|worker| {
            worker
                .as_ref()
                .is_some_and(|worker| !worker.handle.is_finished())
        }));
        metrics
    }
    pub(crate) fn health(&self) -> Result<()> {
        self.state.check()?;
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("流程调度器状态异常。"))?;
        if worker.as_ref().is_some_and(|worker| {
            worker.handle.is_finished()
                && worker
                    .signal
                    .0
                    .lock()
                    .is_ok_and(|control| !control.stopping)
        }) {
            return Err(unavailable("流程调度线程已异常退出，请核对状态后重启。"));
        }
        Ok(())
    }
}
fn wait_for_stop(signal: &Signal, timeout: Duration) -> bool {
    let control = signal.0.lock().unwrap_or_else(|p| p.into_inner());
    let (control, _) = signal
        .1
        .wait_timeout_while(control, timeout, |control| !control.stopping)
        .unwrap_or_else(|p| p.into_inner());
    control.stopping
}
impl Drop for Scheduler {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn stop_during_a_tick_cancels_it_and_skips_the_next_wait() {
        let signal = Arc::new((Mutex::new(Control::default()), Condvar::new()));
        let scope = OperationScope::new(Duration::from_secs(30));
        signal.0.lock().unwrap().active = Some(scope.clone());
        let worker_signal = signal.clone();
        let (ready, started) = std::sync::mpsc::channel();
        let (continue_tick, finish_tick) = std::sync::mpsc::channel();
        let handle = std::thread::spawn(move || {
            ready.send(()).unwrap();
            finish_tick.recv().unwrap();
            assert_eq!(scope.run_result(|| Ok(())).unwrap_err().status, Some(504));
            assert!(wait_for_stop(&worker_signal, Duration::from_secs(60)));
        });
        started.recv().unwrap();
        let scheduler = Scheduler::default();
        *scheduler.worker.lock().unwrap() = Some(Worker { signal, handle });
        scheduler.request_stop().unwrap();
        let before = std::time::Instant::now();
        continue_tick.send(()).unwrap();
        scheduler.stop().unwrap();
        assert!(before.elapsed() < Duration::from_secs(2));
    }
}
