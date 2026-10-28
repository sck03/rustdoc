use super::*;
use chrono::{Datelike, NaiveTime, TimeZone};
use std::sync::{Arc, Condvar, OnceLock, Weak};
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
struct Worker {
    stop: Arc<(Mutex<bool>, Condvar)>,
    handle: JoinHandle<()>,
}
#[derive(Default)]
pub(in crate::engine) struct Scheduler {
    service: OnceLock<Weak<NativeService>>,
    worker: Mutex<Option<Worker>>,
    pub(super) gate: Mutex<()>,
}
impl Scheduler {
    pub(in crate::engine) fn bind(service: &Arc<NativeService>) -> Result<()> {
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
        let stop = Arc::new((Mutex::new(false), Condvar::new()));
        let signal = stop.clone();
        let handle = std::thread::Builder::new()
            .name("exportdoc-backup-schedule".into())
            .spawn(move || {
                loop {
                    if *signal.0.lock().unwrap_or_else(|poison| poison.into_inner()) {
                        break;
                    }
                    let Some(service) = weak.upgrade() else {
                        break;
                    };
                    if let Ok(_access) = service.maintenance.try_read() {
                        if let Err(cause) = service.backup_scheduler.run(&service, None, Utc::now())
                        {
                            let _ = update_state(
                                &service.store,
                                None,
                                json!({"lastResult":"Failed", "lastError":cause.message}),
                            );
                        }
                    }
                    drop(service);
                    let stopped = signal.0.lock().unwrap_or_else(|poison| poison.into_inner());
                    drop(
                        signal
                            .1
                            .wait_timeout_while(stopped, Duration::from_secs(30), |stop| !*stop),
                    );
                }
            })
            .map_err(|_| unavailable("无法启动日常灾备调度器。"))?;
        *worker = Some(Worker { stop, handle });
        Ok(())
    }
    pub(in crate::engine) fn stop(&self) -> Result<()> {
        let worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("灾备调度器状态异常。"))?
            .take();
        if let Some(worker) = worker {
            signal(&worker)?;
            if worker.handle.thread().id() != std::thread::current().id() {
                worker
                    .handle
                    .join()
                    .map_err(|_| unavailable("灾备调度线程异常退出。"))?;
            }
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
        let _gate = self
            .gate
            .lock()
            .map_err(|_| unavailable("灾备执行状态异常。"))?;
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
            let job = run::start(service, &actor, &saved, id.clone(), password, package_lock)?;
            update_state(
                &service.store,
                Some(&id),
                json!({"runningJobId":job["jobId"]}),
            )?;
            Ok(Some(job))
        })();
        if let Err(cause) = &start {
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
    *worker
        .stop
        .0
        .lock()
        .map_err(|_| unavailable("灾备停止状态异常。"))? = true;
    worker.stop.1.notify_all();
    Ok(())
}
impl Drop for Scheduler {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}
