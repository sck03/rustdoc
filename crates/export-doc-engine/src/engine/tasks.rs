//! Durable file tasks. Only running workers live in memory; snapshots and output
//! bytes commit together in the same application database as the business data.
mod admission;
mod completion;
mod output;
mod persistence;
mod queries;
pub mod retention;
pub(crate) mod retry;
pub(crate) mod scheduled;
#[cfg(test)]
mod tests;

use super::{
    error::{Result, error, unavailable},
    store::{Actor, Connection, Store},
};
use crate::{generated_api::*, operation::OperationScope};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::{Arc, Mutex, atomic::AtomicBool},
    thread::JoinHandle,
    time::Duration,
};

pub struct FileOutput {
    pub file_name: String,
    pub media_type: String,
    pub content: Vec<u8>,
}

mod managed_output;
pub struct TaskOutput {
    pub(crate) archive: Option<ArchiveOutput>,
    pub managed_file: Option<ManagedOutput>,
    pub file: Option<FileOutput>,
    pub detail: String,
    pub destination: Option<std::path::PathBuf>,
    pub directory: Option<DirectoryOutput>,
    pub publication: Option<PublicationCheck>,
}
/// Durable business file, excluded from disposable task-download retention.
pub(crate) struct ArchiveOutput {
    pub record_id: i64,
    pub kind: &'static str,
    pub file: FileOutput,
}
pub struct PublicationCheck {
    check: Box<dyn Fn(&Connection, &Actor) -> Result<()> + Send>,
    _file_lock: Option<std::fs::File>,
}
impl PublicationCheck {
    pub(crate) fn new(check: impl Fn(&Connection, &Actor) -> Result<()> + Send + 'static) -> Self {
        Self {
            check: Box::new(check),
            _file_lock: None,
        }
    }
    pub(crate) fn with_file_lock(mut self, file: std::fs::File) -> Self {
        self._file_lock = Some(file);
        self
    }
    pub(super) fn validate(&self, tx: &Connection, actor: &Actor) -> Result<()> {
        (self.check)(tx, actor)
    }
}
pub struct ManagedOutput {
    pub path: std::path::PathBuf,
    pub size: u64,
    pub digest: String,
}
pub struct DirectoryOutput {
    pub path: std::path::PathBuf,
    pub files: Vec<FileOutput>,
}
impl TaskOutput {
    pub fn managed_file(path: std::path::PathBuf, detail: String) -> Result<Self> {
        let (size, digest) = super::backup_io::digest_file(&path)?;
        Ok(Self {
            file: None,
            detail,
            destination: None,
            directory: None,
            managed_file: Some(ManagedOutput { path, size, digest }),
            publication: None,
            archive: None,
        })
    }
    pub fn file(file_name: String, media_type: &str, content: Vec<u8>) -> Self {
        Self {
            file: Some(FileOutput {
                file_name,
                media_type: media_type.into(),
                content,
            }),
            detail: "文件已生成，可下载或保存。".into(),
            destination: None,
            directory: None,
            managed_file: None,
            publication: None,
            archive: None,
        }
    }
    pub(crate) fn check_on_publish(mut self, check: PublicationCheck) -> Self {
        self.publication = Some(check);
        self
    }
}

#[derive(Default)]
struct Runtime {
    workers: Vec<JoinHandle<()>>,
    active: HashMap<String, OperationScope>,
    preparing_retries: usize,
    stopping: bool,
    failure: Option<String>,
    retention_failure: Option<String>,
}
struct TaskSpec<'a> {
    operation: Operation,
    kind: &'a str,
    title: &'a str,
    replay: Option<retry::Replay>,
    timeout: Duration,
}

pub struct Jobs {
    store: Arc<Store>,
    runtime: Arc<Mutex<Runtime>>,
    completion: Arc<completion::Completion>,
    retention: retention::Retention,
    download_budget: Arc<super::backup_io::Budget>,
    admissions: std::sync::RwLock<()>,
}

impl Jobs {
    pub fn metrics(&self) -> Result<Value> {
        let (active, workers, stopping, failed, retention_failure) = {
            let runtime = self
                .runtime
                .lock()
                .map_err(|_| unavailable("文件任务状态异常。"))?;
            (
                runtime.active.len(),
                runtime.workers.len(),
                runtime.stopping,
                runtime.failure.is_some(),
                runtime.retention_failure.clone(),
            )
        };
        Ok(
            json!({"active":active,"workerHandles":workers,"stopping":stopping,"failed":failed,"retentionFailure":retention_failure,
            "persistedCounts":self.store.connection()?.job_counts()?,"retainedOutputBytes":self.store.connection()?.job_output_bytes()?,"retainedInputBytes":self.store.connection()?.job_input_bytes()?,"downloadMemory":self.download_budget.snapshot()}),
        )
    }
    pub fn with_retention(store: Arc<Store>, retention: retention::Retention) -> Result<Self> {
        let retention = retention.validate()?;
        output::recover(&store)?;
        persistence::recover(&store)?;
        retention::prune(&store, retention, chrono::Utc::now())?;
        Ok(Self {
            store,
            runtime: Arc::default(),
            completion: Arc::default(),
            retention,
            download_budget: Arc::default(),
            admissions: Default::default(),
        })
    }

    pub fn health(&self) -> Result<()> {
        let runtime = self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?;
        if let Some(failure) = &runtime.failure {
            return Err(unavailable(failure.clone()));
        }
        drop(runtime);
        self.completion.health()
    }

    pub fn start(
        &self,
        actor: &Actor,
        operation: Operation,
        kind: &str,
        title: &str,
        work: impl FnOnce(&AtomicBool) -> Result<TaskOutput> + Send + 'static,
    ) -> Result<Value> {
        self.start_replayable(actor, operation, kind, title, None, work)
    }
    pub(crate) fn start_replayable(
        &self,
        actor: &Actor,
        operation: Operation,
        kind: &str,
        title: &str,
        replay: Option<retry::Replay>,
        work: impl FnOnce(&AtomicBool) -> Result<TaskOutput> + Send + 'static,
    ) -> Result<Value> {
        self.start_task(
            actor,
            TaskSpec {
                operation,
                kind,
                title,
                replay,
                timeout: Duration::from_secs(180),
            },
            work,
        )
    }
    pub(crate) fn start_backup(
        &self,
        actor: &Actor,
        operation: Operation,
        kind: &str,
        title: &str,
        work: impl FnOnce(&AtomicBool) -> Result<TaskOutput> + Send + 'static,
    ) -> Result<Value> {
        self.start_task(
            actor,
            TaskSpec {
                operation,
                kind,
                title,
                replay: None,
                timeout: Duration::from_secs(super::backup_io::TIMEOUT_SECONDS),
            },
            work,
        )
    }
    fn start_task(
        &self,
        actor: &Actor,
        spec: TaskSpec<'_>,
        work: impl FnOnce(&AtomicBool) -> Result<TaskOutput> + Send + 'static,
    ) -> Result<Value> {
        let TaskSpec {
            operation,
            kind,
            title,
            replay,
            timeout,
        } = spec;
        let _admission = self
            .admissions
            .read()
            .map_err(|_| unavailable("任务登记状态异常。"))?;
        self.health()?;
        let scope = OperationScope::detached(timeout);
        let mut admission = admission::Starting::acquire(&self.runtime, scope.clone())?;
        let finished: Vec<_> = self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?
            .workers
            .extract_if(.., |worker| worker.is_finished())
            .collect();
        for worker in finished {
            worker
                .join()
                .map_err(|_| unavailable("文件任务异常退出。"))?;
        }
        let snapshot = persistence::create_replayable(
            &self.store,
            actor,
            operation,
            kind,
            title,
            replay.as_ref(),
        )?;
        let job_id = snapshot["jobId"]
            .as_str()
            .ok_or_else(|| unavailable("任务编号缺失。"))?
            .to_owned();
        let mut runtime = self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?;
        if runtime.stopping || runtime.failure.is_some() {
            drop(runtime);
            OperationScope::detached(Duration::from_secs(30)).run_result(|| {
                persistence::finish(
                    &self.store,
                    actor,
                    &job_id,
                    Err(unavailable("任务登记后服务已停止，未启动处理。")),
                )
            })?;
            return Err(unavailable("程序正在关闭，任务已保留失败回执。"));
        }
        admission.bind(&mut runtime, &job_id);
        let store = self.store.clone();
        let state = self.runtime.clone();
        let completion = self.completion.clone();
        let owner = actor.clone();
        let retention = self.retention;
        let worker_id = job_id.clone();
        let worker = std::thread::Builder::new()
            .name(format!("exportdoc-task-{kind}"))
            .spawn(move || {
                let flag = scope.cancellation_flag();
                let mut publication = None;
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    scope.run_result(|| {
                        let output = work(&flag)?;
                        publication = output::prepare(&store, &worker_id, &output)?;
                        persistence::prepare_output(&store, output)
                    })
                }))
                .unwrap_or_else(|_| Err(unavailable("文件处理异常终止，未发布输出。")));
                // Persist terminal state outside the canceled execution scope.
                let finished = completion.finish(|| {
                    persistence::finish_controlled(
                        &store,
                        &owner,
                        &worker_id,
                        result,
                        Some(&flag),
                        publication,
                    )
                });
                // Publication is already durable. Maintenance has its own deadline and
                // short transactions; it must not hold the output completion gate.
                let maintenance = if finished.is_ok() {
                    OperationScope::detached(Duration::from_secs(30))
                        .run_result(|| retention::prune(&store, retention, chrono::Utc::now()))
                        .err()
                        .map(|cause| cause.to_string())
                } else {
                    None
                };
                if let Ok(mut runtime) = state.lock() {
                    runtime.retention_failure = maintenance;
                    runtime.active.remove(&worker_id);
                    if let Err(cause) = finished {
                        runtime.failure =
                            Some(format!("任务结果持久化失败，已停止接收新任务：{cause}"));
                    }
                }
            });
        match worker {
            Ok(worker) => {
                runtime.workers.push(worker);
                admission.started();
            }
            Err(cause) => {
                runtime.active.remove(&job_id);
                drop(runtime);
                OperationScope::detached(Duration::from_secs(30)).run_result(|| {
                    persistence::finish(
                        &self.store,
                        actor,
                        &job_id,
                        Err(unavailable(cause.to_string())),
                    )
                })?;
                return Err(unavailable("无法启动文件处理线程。"));
            }
        }
        Ok(snapshot)
    }

    pub fn completed(&self, actor: &Actor, kind: &str, title: &str, detail: &str) -> Result<Value> {
        self.health()?;
        retention::prune(&self.store, self.retention, chrono::Utc::now())?;
        let job = persistence::create_completed(&self.store, actor, kind, title, detail)?;
        let id = job["jobId"]
            .as_str()
            .ok_or_else(|| unavailable("任务编号缺失。"))?;
        self.get(actor, id)
    }

    pub fn get(&self, actor: &Actor, id: &str) -> Result<Value> {
        self.health()?;
        let _completion = self.completion.read()?;
        persistence::get(&self.store, actor, id)
    }

    pub fn list(&self, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
        self.health()?;
        let _completion = self.completion.read()?;
        persistence::list(&self.store, actor, query)
    }

    pub fn download(&self, actor: &Actor, id: &str) -> Result<FileOutput> {
        let (pending, snapshot) = self.capture_download(actor, id)?;
        let download = pending.verify()?;
        self.authorize_download(actor, id, Some(&snapshot))?;
        download.into_memory()
    }
    pub(crate) fn authorize_download(
        &self,
        actor: &Actor,
        id: &str,
        expected: Option<&Value>,
    ) -> Result<()> {
        self.health()?;
        let _completion = self.completion.read()?;
        persistence::authorize_download(&self.store, actor, id, expected)
    }
    pub(crate) fn capture_download(
        &self,
        actor: &Actor,
        id: &str,
    ) -> Result<(super::backup_io::PendingDownload, Value)> {
        self.health()?;
        let _completion = self.completion.read()?;
        persistence::download(&self.store, actor, id, &self.download_budget)
    }
    pub(super) fn reserve_download(
        &self,
        bytes: usize,
        working: bool,
    ) -> Result<super::backup_io::Reservation> {
        if working {
            self.download_budget.reserve_working(bytes)
        } else {
            self.download_budget.reserve(bytes)
        }
    }

    pub fn operation(&self, operation: Operation, actor: &Actor, id: &str) -> Result<Vec<u8>> {
        self.health()?;
        let value = match operation {
            GET_JOB => self.get(actor, id)?,
            DOWNLOAD_JOB_RESULT => return Ok(self.download(actor, id)?.content),
            CANCEL_JOB => {
                // The database flag is authoritative even when completion races cancellation.
                persistence::cancel(&self.store, actor, id)?;
                if let Some(scope) = self
                    .runtime
                    .lock()
                    .map_err(|_| unavailable("文件任务状态异常。"))?
                    .active
                    .get(id)
                {
                    scope.cancel();
                }
                json!({"success":true,"message":"已请求取消"})
            }
            DELETE_JOB => {
                let _completion = self.completion.read()?;
                persistence::delete(&self.store, actor, id)?;
                json!({"success":true,"message":"任务输出已清理"})
            }
            _ => return Err(super::error::invalid("文件任务操作无效。")),
        };
        serde_json::to_vec(&value).map_err(Into::into)
    }

    pub fn clear_finished(&self, actor: &Actor) -> Result<Value> {
        let failed = self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?
            .failure
            .is_some();
        if failed {
            super::auth::authorize(actor, "system.settings", "manage")?;
            self.recover()?;
        }
        self.health()?;
        let _completion = self.completion.read()?;
        let count = persistence::clear_finished(&self.store, actor)?;
        Ok(
            json!({"success":true,"message":format!("已清理 {count} 个已结束任务。"),"affectedCount":count}),
        )
    }
    pub fn ensure_idle(&self) -> Result<()> {
        self.health()?;
        if !self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?
            .active
            .is_empty()
        {
            return Err(error(
                409,
                "请先等待或取消正在执行的文件任务，再恢复数据库。",
            ));
        }
        Ok(())
    }
    pub fn recover(&self) -> Result<()> {
        let _admissions = self
            .admissions
            .write()
            .map_err(|_| unavailable("任务登记状态异常。"))?;
        {
            let runtime = self
                .runtime
                .lock()
                .map_err(|_| unavailable("文件任务状态异常。"))?;
            if runtime.stopping || !runtime.active.is_empty() || runtime.preparing_retries != 0 {
                return Err(error(409, "请先停止任务，再核验输出恢复状态。"));
            }
        }
        self.completion.recover(|| {
            self.store.health()?;
            output::recover(&self.store)?;
            persistence::recover(&self.store)?;
            retention::prune(&self.store, self.retention, chrono::Utc::now())?;
            self.runtime
                .lock()
                .map_err(|_| unavailable("文件任务状态异常。"))?
                .failure = None;
            Ok(())
        })
    }

    pub fn close(&self) -> Result<()> {
        self.request_stop()?;
        let _admissions = self
            .admissions
            .write()
            .map_err(|_| unavailable("任务登记状态异常。"))?;
        let mut failure = None;
        let (workers, ids) = {
            let mut runtime = self
                .runtime
                .lock()
                .map_err(|_| unavailable("文件任务状态异常。"))?;
            runtime.stopping = true;
            for scope in runtime.active.values() {
                scope.cancel();
            }
            (
                std::mem::take(&mut runtime.workers),
                runtime.active.keys().cloned().collect::<Vec<_>>(),
            )
        };
        for id in ids {
            if let Err(error) = persistence::cancel_system(&self.store, &id) {
                failure.get_or_insert(error);
            }
        }
        for worker in workers {
            if worker.join().is_err() {
                failure.get_or_insert_with(|| unavailable("文件任务异常退出。"));
            }
        }
        if let Some(error) = failure {
            Err(error)
        } else {
            self.health()
        }
    }
    pub(crate) fn request_stop(&self) -> Result<()> {
        let mut runtime = self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?;
        runtime.stopping = true;
        for scope in runtime.active.values() {
            scope.cancel();
        }
        Ok(())
    }
}

impl Drop for Jobs {
    fn drop(&mut self) {
        // Explicit shutdown reports errors. Drop still cancels and joins workers,
        // preserving the instance lock until their final transaction finishes.
        let _ = self.close();
    }
}
