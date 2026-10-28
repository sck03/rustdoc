//! One sleeping worker per enabled service, independent of browser page lifetime.
use super::*;
use std::{
    sync::{Arc, Condvar, Mutex, OnceLock, Weak},
    thread::JoinHandle,
    time::Duration,
};

#[derive(Default)]
struct Signal {
    stopped: bool,
    generation: u64,
}
struct Worker {
    signal: Arc<(Mutex<Signal>, Condvar)>,
    handle: JoinHandle<()>,
}
#[derive(Default)]
pub(in crate::engine) struct Scheduler {
    service: OnceLock<Weak<NativeService>>,
    worker: Mutex<Option<Worker>>,
}
impl Scheduler {
    pub(in crate::engine) fn bind(service: &Arc<NativeService>) -> Result<()> {
        let _ = service
            .exchange_scheduler
            .service
            .set(Arc::downgrade(service));
        service.exchange_scheduler.refresh(service)
    }
    pub(in crate::engine) fn refresh(&self, service: &NativeService) -> Result<()> {
        let mut worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("汇率调度状态异常。"))?;
        if settings::current(&service.store)?["exchangeRate"]["autoRefreshEnabled"] != true {
            return stop_worker(&mut worker);
        }
        configuration(service)?;
        if let Some(worker) = worker.as_ref().filter(|w| !w.handle.is_finished()) {
            worker
                .signal
                .0
                .lock()
                .map_err(|_| unavailable("汇率调度状态异常。"))?
                .generation += 1;
            worker.signal.1.notify_all();
            return Ok(());
        }
        if let Some(old) = worker.take() {
            old.handle
                .join()
                .map_err(|_| unavailable("汇率调度线程异常退出。"))?;
        }
        let service = self
            .service
            .get()
            .ok_or_else(|| unavailable("汇率调度器未绑定。"))?
            .clone();
        let signal = Arc::new((Mutex::new(Signal::default()), Condvar::new()));
        let control = signal.clone();
        let handle = std::thread::Builder::new()
            .name("exportdoc-exchange-schedule".into())
            .spawn(move || {
                loop {
                    let generation = {
                        let signal = control.0.lock().unwrap_or_else(|e| e.into_inner());
                        if signal.stopped {
                            break;
                        }
                        signal.generation
                    };
                    let Some(service) = service.upgrade() else {
                        break;
                    };
                    let delay = run(&service, &control).unwrap_or(Duration::from_secs(300));
                    drop(service);
                    let signal = control.0.lock().unwrap_or_else(|e| e.into_inner());
                    drop(control.1.wait_timeout_while(signal, delay, |s| {
                        !s.stopped && s.generation == generation
                    }));
                }
            })
            .map_err(|_| unavailable("无法启动自动汇率获取，请重新保存设置或重启服务。"))?;
        *worker = Some(Worker { signal, handle });
        Ok(())
    }
    pub(in crate::engine) fn stop(&self) -> Result<()> {
        let mut worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("汇率调度状态异常。"))?;
        stop_worker(&mut worker)
    }
    pub(in crate::engine) fn request_stop(&self) -> Result<()> {
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("汇率调度状态异常。"))?;
        if let Some(worker) = worker.as_ref() {
            signal(worker)?;
        }
        Ok(())
    }
    pub(in crate::engine) fn health(&self) -> Result<()> {
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("汇率调度状态异常。"))?;
        if worker.as_ref().is_some_and(|w| w.handle.is_finished()) {
            return Err(unavailable(
                "汇率调度线程已退出，请重新保存设置或重启服务。",
            ));
        }
        Ok(())
    }
}
fn stop_worker(worker: &mut Option<Worker>) -> Result<()> {
    if let Some(worker) = worker.take() {
        signal(&worker)?;
        if worker.handle.thread().id() != std::thread::current().id() {
            worker
                .handle
                .join()
                .map_err(|_| unavailable("汇率调度线程异常退出。"))?;
        }
    }
    Ok(())
}
fn signal(worker: &Worker) -> Result<()> {
    worker
        .signal
        .0
        .lock()
        .map_err(|_| unavailable("汇率调度状态异常。"))?
        .stopped = true;
    worker.signal.1.notify_all();
    Ok(())
}
fn run(service: &NativeService, signal: &(Mutex<Signal>, Condvar)) -> Result<Duration> {
    let Ok(_access) = service.maintenance.try_read() else {
        return Ok(Duration::from_secs(30));
    };
    let config = configuration(service)?;
    if !config.automatic {
        return Ok(Duration::from_secs(300));
    }
    service.store.health()?;
    super::super::licensing::check_operation(service, LIST_EXCHANGE_RATES)?;
    let now = service.clock.now().map_err(unavailable)?.utc_now;
    let _ = service.exchange.get(&config, false, true, now, &|| {
        if signal.0.lock().map_or(true, |s| s.stopped) {
            return Err(export_doc_exchange::Error::new(
                export_doc_exchange::ErrorKind::Cancelled,
                "汇率自动获取已停止。",
            ));
        }
        Ok(())
    });
    let now = service.clock.now().map_err(unavailable)?.utc_now;
    let due = service
        .exchange
        .next_update(&config, now)
        .map_err(map_error)?;
    Ok((due - now)
        .to_std()
        .unwrap_or_default()
        .clamp(Duration::from_secs(1), Duration::from_secs(86400)))
}
impl Drop for Scheduler {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

#[cfg(test)]
mod tests;
