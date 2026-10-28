//! Durable file tasks. Only running workers live in memory; snapshots and output
//! bytes commit together in the same application database as the business data.
mod admission;
mod completion;
mod output;
mod persistence;
mod queries;
pub mod retention;
pub(crate) mod retry;
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
}

impl Jobs {
    pub fn metrics(&self) -> Result<Value> {
        let (active, workers, stopping, failed) = {
            let runtime = self
                .runtime
                .lock()
                .map_err(|_| unavailable("文件任务状态异常。"))?;
            (
                runtime.active.len(),
                runtime.workers.len(),
                runtime.stopping,
                runtime.failure.is_some(),
            )
        };
        Ok(
            json!({"active":active,"workerHandles":workers,"stopping":stopping,"failed":failed,
            "persistedCounts":self.store.connection()?.job_counts()?,"retainedOutputBytes":self.store.connection()?.job_output_bytes()?,"retainedInputBytes":self.store.connection()?.job_input_bytes()?,"downloadMemory":self.download_budget.snapshot()}),
        )
    }
    pub fn open(store: Arc<Store>) -> Result<Self> {
        Self::with_retention(store, Default::default())
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
        Ok(())
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
        self.health()?;
        let mut runtime = self
            .runtime
            .lock()
            .map_err(|_| unavailable("文件任务状态异常。"))?;
        if runtime.stopping {
            return Err(error(503, "程序正在关闭，不能开始新任务。"));
        }
        if runtime.active.len() >= 2 {
            return Err(error(429, "同时最多运行两个文件任务。"));
        }
        for worker in runtime
            .workers
            .extract_if(.., |worker| worker.is_finished())
        {
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
        let scope = OperationScope::new(timeout);
        runtime.active.insert(job_id.clone(), scope.clone());
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
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    scope.run_result(|| work(&flag))
                }))
                .unwrap_or_else(|_| Err(unavailable("文件处理异常终止，未发布输出。")));
                // Persist terminal state outside the canceled execution scope.
                let finished = completion.finish(|| {
                    persistence::finish_controlled(&store, &owner, &worker_id, result, Some(&flag))
                        .and_then(|_| {
                            retention::prune(&store, retention, chrono::Utc::now()).map(|_| ())
                        })
                });
                if let Ok(mut runtime) = state.lock() {
                    runtime.active.remove(&worker_id);
                    if let Err(cause) = finished {
                        runtime.failure =
                            Some(format!("任务结果持久化失败，已停止接收新任务：{cause}"));
                    }
                }
            });
        match worker {
            Ok(worker) => runtime.workers.push(worker),
            Err(cause) => {
                runtime.active.remove(&job_id);
                persistence::finish(
                    &self.store,
                    actor,
                    &job_id,
                    Err(unavailable(cause.to_string())),
                )?;
                return Err(unavailable("无法启动文件处理线程。"));
            }
        }
        Ok(snapshot)
    }

    pub fn completed(&self, actor: &Actor, kind: &str, title: &str, detail: &str) -> Result<Value> {
        self.health()?;
        let job = persistence::create_completed(&self.store, actor, kind, title)?;
        let id = job["jobId"]
            .as_str()
            .ok_or_else(|| unavailable("任务编号缺失。"))?;
        persistence::finish(
            &self.store,
            actor,
            id,
            Ok(TaskOutput {
                file: None,
                detail: detail.into(),
                destination: None,
                directory: None,
                managed_file: None,
                publication: None,
                archive: None,
            }),
        )?;
        retention::prune(&self.store, self.retention, chrono::Utc::now())?;
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
        self.prepare_download(actor, id)?.into_memory()
    }
    pub(crate) fn authorize_download(&self, actor: &Actor, id: &str) -> Result<()> {
        self.health()?;
        let _completion = self.completion.read()?;
        persistence::authorize_download(&self.store, actor, id)
    }
    pub fn prepare_download(&self, actor: &Actor, id: &str) -> Result<super::backup_io::Download> {
        self.health()?;
        let _completion = self.completion.read()?;
        persistence::download(&self.store, actor, id, &self.download_budget)
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
        self.ensure_idle()?;
        persistence::recover(&self.store)?;
        retention::prune(&self.store, self.retention, chrono::Utc::now())?;
        Ok(())
    }

    pub fn close(&self) -> Result<()> {
        let mut failure = None;
        let workers = {
            let mut runtime = self
                .runtime
                .lock()
                .map_err(|_| unavailable("文件任务状态异常。"))?;
            runtime.stopping = true;
            for (id, scope) in &runtime.active {
                scope.cancel();
                if let Err(error) = persistence::cancel_system(&self.store, id) {
                    failure.get_or_insert(error);
                }
            }
            std::mem::take(&mut runtime.workers)
        };
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
