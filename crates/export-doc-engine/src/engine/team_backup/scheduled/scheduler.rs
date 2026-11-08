use super::*;
use crate::operation::{self, OperationScope};
use chrono::{Datelike, NaiveTime, TimeZone};
use std::sync::{Arc, Condvar, Mutex, MutexGuard, OnceLock, TryLockError, Weak};
use std::thread::JoinHandle;

fn zone(service: &NativeService, now: DateTime<Utc>) -> Result<chrono_tz::Tz> {
    service
        .clock
        .at(now)
        .map_err(invalid)?
        .time_zone
        .parse()
        .map_err(|_| invalid("业务时区无效。"))
}
pub(super) fn next_run(service: &NativeService, plan: &BackupPlan) -> Result<Option<String>> {
    if !plan.enabled {
        return Ok(None);
    }
    let now = Utc::now();
    let zone = zone(service, now)?;
    let today = now.with_timezone(&zone).date_naive();
    let time =
        NaiveTime::parse_from_str(&plan.time, "%H:%M").map_err(|_| invalid("备份时间无效。"))?;
    for offset in 0..8 {
        let date = today + chrono::Duration::days(offset);
        if plan.frequency == "Weekly" && date.weekday().num_days_from_sunday() != plan.weekday {
            continue;
        }
        // A missing DST wall time advances to the first valid minute. A repeated
        // wall time has one local slot and therefore produces at most one backup.
        for delay in 0..=180 {
            if let Some(candidate) = zone
                .from_local_datetime(&(date.and_time(time) + chrono::Duration::minutes(delay)))
                .earliest()
            {
                if candidate.with_timezone(&Utc) > now {
                    return Ok(Some(candidate.to_rfc3339()));
                }
                break;
            }
        }
    }
    Ok(None)
}
#[derive(Default)]
struct Control {
    stopping: bool,
    active: Option<OperationScope>,
}
struct Worker {
    stop: Arc<(Mutex<Control>, Condvar)>,
    handle: JoinHandle<()>,
}
#[derive(Default)]
pub(in crate::engine) struct Scheduler {
    service: OnceLock<Weak<NativeService>>,
    worker: Mutex<Option<Worker>>,
    gate: Mutex<()>,
}
impl Scheduler {
    pub(super) fn lock(&self) -> Result<MutexGuard<'_, ()>> {
        let deadline = std::time::Instant::now() + Duration::from_secs(5);
        loop {
            operation::check()?;
            match self.gate.try_lock() {
                Ok(guard) => return Ok(guard),
                Err(TryLockError::Poisoned(_)) => return Err(unavailable("灾备执行状态异常。")),
                Err(TryLockError::WouldBlock) => {
                    if std::time::Instant::now() >= deadline {
                        return Err(error(429, "灾备计划正在处理中，请稍后重试。"));
                    }
                    std::thread::sleep(Duration::from_millis(10));
                }
            }
        }
    }
    pub(in crate::engine) fn health(&self) -> Result<()> {
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("灾备调度器状态异常。"))?;
        if worker
            .as_ref()
            .is_some_and(|worker| worker.handle.is_finished())
        {
            return Err(unavailable(
                "灾备调度线程已异常退出，请检查日志并重启服务。",
            ));
        }
        Ok(())
    }

    pub(in crate::engine) fn bind(service: &Arc<NativeService>) -> Result<()> {
        drop(package::lock(&service.paths)?);
        let _ = service
            .backup_scheduler
            .service
            .set(Arc::downgrade(service));
        let mut saved = load(&service.store)?;
        if saved.plan.enabled
            && !repository::identity(&service.paths).is_ok_and(|id| id == saved.repository)
        {
            // Restoring a machine must not inherit active cleanup for its source's repository.
            saved.plan.enabled = false;
            saved.revision += 1;
            service.store.transaction(|tx| {
                tx.set_settings(PLAN, saved.revision, &serde_json::to_value(&saved)?)?;
                Ok(())
            })?;
            update_state(
                &service.store,
                None,
                json!({"lastResult":"NeedsReview", "lastError":"灾备仓库与恢复后的计划不匹配，已关闭自动计划；请复核并重新保存。", "runningJobId":null}),
            )?;
        }
        service.backup_scheduler.refresh(service)
    }
    pub(in crate::engine) fn refresh(&self, service: &NativeService) -> Result<()> {
        if !load(&service.store)?.plan.enabled {
            return self.stop();
        }
        let mut worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("灾备调度器状态异常。"))?;
        if worker
            .as_ref()
            .is_some_and(|worker| !worker.handle.is_finished())
        {
            return Ok(());
        }
        if let Some(old) = worker.take() {
            old.handle
                .join()
                .map_err(|_| unavailable("灾备调度线程异常退出。"))?;
        }
        let weak = self
            .service
            .get()
            .ok_or_else(|| unavailable("灾备调度器未绑定。"))?
            .clone();
        let stop = Arc::new((Mutex::new(Control::default()), Condvar::new()));
        let signal = stop.clone();
        let handle = std::thread::Builder::new()
            .name("exportdoc-backup-schedule".into())
            .spawn(move || {
                loop {
                    if signal
                        .0
                        .lock()
                        .unwrap_or_else(|poison| poison.into_inner())
                        .stopping
                    {
                        break;
                    }
                    let Some(service) = weak.upgrade() else {
                        break;
                    };
                    if let Ok(_access) = service.maintenance.try_read() {
                        let scope = OperationScope::new(Duration::from_secs(30));
                        {
                            let mut control = signal.0.lock().unwrap_or_else(|p| p.into_inner());
                            if control.stopping {
                                break;
                            }
                            control.active = Some(scope.clone());
                        }
                        let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                            scope.run(|| service.backup_scheduler.run(&service, None, Utc::now()))
                        }));
                        let stopping = {
                            let mut control = signal.0.lock().unwrap_or_else(|p| p.into_inner());
                            control.active = None;
                            control.stopping
                        };
                        if stopping {
                            break;
                        }
                        match result {
                            Ok(Ok(_)) => {}
                            Ok(Err(cause)) => {
                                record_failure(&service, &cause.message);
                            }
                            Err(_) => {
                                record_failure(&service, "灾备调度线程异常退出，请重启服务。");
                                break;
                            }
                        }
                    }
                    drop(service);
                    let stopped = signal.0.lock().unwrap_or_else(|poison| poison.into_inner());
                    drop(
                        signal
                            .1
                            .wait_timeout_while(stopped, Duration::from_secs(30), |stop| {
                                !stop.stopping
                            }),
                    );
                }
            })
            .map_err(|_| unavailable("无法启动日常灾备调度器。"))?;
        *worker = Some(Worker { stop, handle });
        Ok(())
    }
    pub(in crate::engine) fn stop(&self) -> Result<()> {
        let mut worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("灾备调度器状态异常。"))?;
        if let Some(active) = worker.as_ref() {
            signal(active)?;
            if active.handle.thread().id() == std::thread::current().id() {
                return Ok(());
            }
            let deadline = std::time::Instant::now() + Duration::from_secs(10);
            while !active.handle.is_finished() {
                if std::time::Instant::now() >= deadline {
                    return Err(unavailable("灾备调度线程未在关闭时限内退出。"));
                }
                std::thread::sleep(Duration::from_millis(10));
            }
        }
        if let Some(finished) = worker.take() {
            finished
                .handle
                .join()
                .map_err(|_| unavailable("灾备调度线程异常退出。"))?;
        }
        Ok(())
    }
    pub(in crate::engine) fn request_stop(&self) -> Result<()> {
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("灾备调度器状态异常。"))?;
        if let Some(worker) = worker.as_ref() {
            signal(worker)?;
        }
        Ok(())
    }
    pub(super) fn run(
        &self,
        service: &NativeService,
        manual: Option<&Actor>,
        now: DateTime<Utc>,
    ) -> Result<Option<Value>> {
        let _gate = self.lock()?;
        let saved = load(&service.store)?;
        let previous = state(&service.store)?;
        let slot = saved
            .plan
            .due_slot(now.with_timezone(&zone(service, now)?).naive_local());
        if manual.is_none() {
            if slot.is_none() {
                return Ok(None);
            }
            if previous["slot"].as_str() == slot.as_deref()
                && (previous["lastResult"] != "Deferred"
                    || previous["retryAt"]
                        .as_str()
                        .and_then(|value| DateTime::parse_from_rfc3339(value).ok())
                        .is_none_or(|at| at > now))
            {
                return Ok(None);
            }
        }
        let start = (|| {
            let actor = if let Some(actor) = manual {
                actor.clone()
            } else {
                auth::current_actor(&service.store, saved.owner)?
            };
            administrator(&actor)?;
            if let Some(id) = previous["runningJobId"].as_str() {
                if service.jobs.get(&actor, id).is_ok_and(|job| {
                    matches!(
                        job["status"].as_str(),
                        Some("Running" | "Pending" | "Queued")
                    )
                }) {
                    return if manual.is_some() {
                        Err(error(429, "已有日常灾备任务正在执行。"))
                    } else {
                        Ok(None)
                    };
                }
            }
            if saved.password.is_empty() {
                return Err(invalid("请先保存灾备包密码。"));
            }
            if repository::identity(&service.paths)? != saved.repository {
                return Err(unavailable("灾备目录与计划不匹配，请复核后重新保存。"));
            }
            let password = service
                .protector
                .unprotect(PASSWORD, &saved.password)
                .map_err(unavailable)?;
            let id = crate::paths::nonce().map_err(unavailable)?;
            let package_lock = package::lock(&service.paths)?;
            update_state(
                &service.store,
                None,
                json!({"archiveId":id, "slot":slot, "lastRunAt":now, "runningJobId":null, "lastResult":"Running", "lastError":""}),
            )?;
            let job = match run::start(service, &actor, &saved, id.clone(), password, package_lock)
            {
                Ok(job) => job,
                Err(cause) => {
                    OperationScope::detached(Duration::from_secs(2)).run(|| {
                        update_state(
                            &service.store,
                            Some(&id),
                            json!({"lastResult":"Failed", "lastError":cause.message}),
                        )
                    })?;
                    return Err(cause);
                }
            };
            // A submitted job has its own lifetime; publish its identity even if
            // the initiating request/tick was canceled just after submission.
            OperationScope::detached(Duration::from_secs(2)).run(|| {
                update_state(
                    &service.store,
                    Some(&id),
                    json!({"runningJobId":job["jobId"]}),
                )
            })?;
            Ok(Some(job))
        })();
        if let Err(cause) = &start {
            operation::check()?;
            if manual.is_none() || previous["lastResult"] != "Running" {
                update_state(
                    &service.store,
                    None,
                    json!({"slot":slot, "lastResult":if cause.status == Some(429) {"Deferred"} else {"Failed"},
                    "lastError":cause.message, "retryAt":now + chrono::Duration::minutes(5)}),
                )?;
            }
            if manual.is_none() {
                return Ok(None);
            }
        }
        start
    }
}
fn signal(worker: &Worker) -> Result<()> {
    let mut control = worker
        .stop
        .0
        .lock()
        .map_err(|_| unavailable("灾备停止状态异常。"))?;
    control.stopping = true;
    if let Some(scope) = &control.active {
        scope.cancel();
    }
    worker.stop.1.notify_all();
    Ok(())
}
fn record_failure(service: &NativeService, message: &str) {
    if let Err(cause) = OperationScope::detached(Duration::from_secs(1)).run(|| {
        update_state(
            &service.store,
            None,
            json!({"lastResult":"Failed", "lastError":message}),
        )
    }) {
        eprintln!("灾备调度失败：{message}；状态记录失败：{}", cause.message);
    }
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
    fn stop_cancels_an_active_tick_waiting_for_the_plan_lock() {
        let scheduler = Arc::new(Scheduler::default());
        let gate = scheduler.gate.lock().unwrap();
        let scope = OperationScope::new(Duration::from_secs(30));
        let stop = Arc::new((
            Mutex::new(Control {
                stopping: false,
                active: Some(scope.clone()),
            }),
            Condvar::new(),
        ));
        let active = scheduler.clone();
        let (ready, started) = std::sync::mpsc::channel();
        let handle = std::thread::spawn(move || {
            scope.run(|| {
                ready.send(()).unwrap();
                assert_eq!(active.lock().unwrap_err().status, Some(504));
            });
        });
        *scheduler.worker.lock().unwrap() = Some(Worker { stop, handle });
        started.recv().unwrap();
        let before = std::time::Instant::now();
        scheduler.stop().unwrap();
        assert!(before.elapsed() < Duration::from_secs(2));
        drop(gate);
    }
    #[test]
    fn deadline_cancels_plan_lock_waits() {
        let scheduler = Scheduler::default();
        let _gate = scheduler.gate.lock().unwrap();
        let cause =
            OperationScope::new(Duration::from_millis(30)).run(|| scheduler.lock().unwrap_err());
        assert_eq!(cause.status, Some(504));
    }
}
