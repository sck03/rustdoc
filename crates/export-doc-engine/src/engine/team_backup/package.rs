//! One bounded, authenticated package contract for SQLite disaster recovery and PG migration.
use super::{
    error::{Result, error, invalid, unavailable},
    sealed,
};
use crate::paths::{self, RuntimePaths};
use fs2::FileExt;
use serde::{Deserialize, Serialize};
use std::{
    fs,
    path::{Path, PathBuf},
};

pub(super) const KEY: &str = "Security/native-master-key.bin";
pub(super) const SQLITE: &str = crate::paths::SQLITE_DATABASE_RELATIVE_PATH;
pub(super) const POSTGRES: &str = "Database/postgresql-physical.dump";
pub(in crate::engine) fn lock(paths: &RuntimePaths) -> Result<fs::File> {
    let path = paths.data_root.join("Locks").join("backup-restore.lock");
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    fs::create_dir_all(path.parent().unwrap())?;
    let file = fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)?;
    file.try_lock_exclusive().map_err(|e| {
        if e.raw_os_error() == fs2::lock_contended_error().raw_os_error() {
            error(429, "备份或恢复正在进行，请稍后重试。")
        } else {
            unavailable(e.to_string())
        }
    })?;
    super::scratch::recover(paths)?;
    Ok(file)
}
pub(super) fn private_directory(path: &Path) -> Result<()> {
    crate::secrets::private_directory(path).map_err(unavailable)
}
pub(in crate::engine::team_backup) fn collect(
    root: &Path,
    directory: &Path,
    sources: &mut Vec<(String, PathBuf)>,
) -> Result<()> {
    paths::ensure_safe_absolute(directory).map_err(invalid)?;
    if !directory.try_exists()? {
        return Ok(());
    }
    for entry in fs::read_dir(directory)? {
        crate::operation::check()?;
        let path = entry?.path();
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if path.is_dir() {
            collect(root, &path, sources)?;
        } else {
            if !path.is_file() || sources.len() >= sealed::MAX_FILES - 1 {
                return Err(invalid("模板文件数量或类型无效。"));
            }
            let name = path
                .strip_prefix(root)
                .map_err(|_| invalid("模板路径越界。"))?
                .components()
                .map(|p| p.as_os_str().to_string_lossy().into_owned())
                .collect::<Vec<_>>()
                .join("/");
            if name.split('/').any(|p| !paths::valid_file_name(p)) {
                return Err(invalid("模板文件名无效。"));
            }
            sources.push((name, path));
        }
    }
    Ok(())
}
#[derive(Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub(super) enum SnapshotRestore {
    Repository {
        provider: String,
        sha256: String,
        point_id: String,
    },
    Sqlite {
        sha256: String,
    },
    Postgres {
        file_name: Option<String>,
        sha256: String,
    },
}
pub(in crate::engine) struct PreparedRestore {
    staging: PathBuf,
    snapshot: Option<SnapshotRestore>,
    pub(in crate::engine) marker: PathBuf,
}
impl PreparedRestore {
    pub(super) fn verify_postgres(&self, paths: &RuntimePaths) -> Result<()> {
        super::postgres::verify_dump(paths, &self.staging.join(POSTGRES))
    }
    pub(super) fn into_task(
        self,
        lock: fs::File,
        action: &'static str,
        detail: &str,
    ) -> super::tasks::TaskOutput {
        let note = detail.to_owned();
        super::tasks::TaskOutput {
            committed: false,
            file: None,
            destination: None,
            directory: None,
            managed_file: None,
            archive: None,
            detail: note.clone(),
            publication: Some(
                super::tasks::PublicationCheck::new(move |tx, actor| {
                    super::audit_in(tx, actor, action, &note)
                })
                .with_restore(self)
                .with_file_lock(lock),
            ),
        }
    }
    pub(in crate::engine) fn marker_content(&self, job: Option<&str>) -> Result<Vec<u8>> {
        serde_json::to_vec(&serde_json::json!({"stagingDirectoryName":self.staging.file_name().unwrap().to_string_lossy(),
            "scheduledAtUtc":chrono::Utc::now().to_rfc3339(),"jobId":job,"snapshot":self.snapshot})).map_err(Into::into)
    }
}
pub(super) fn prepare_snapshot(
    marker: &Path,
    prepare: impl FnOnce(&Path) -> Result<SnapshotRestore>,
) -> Result<PreparedRestore> {
    if marker.try_exists()? {
        return Err(error(409, "已有恢复等待处理，不能覆盖。"));
    }
    let root = marker.parent().ok_or_else(|| invalid("恢复目录无效。"))?;
    let staging = super::scratch::create(root, "pending-")?;
    let mut prepared = PreparedRestore {
        staging,
        marker: marker.into(),
        snapshot: None,
    };
    prepared.snapshot = Some(prepare(&prepared.staging)?);
    Ok(prepared)
}
pub(super) fn snapshot(marker: &Path) -> Result<Option<SnapshotRestore>> {
    serde_json::from_value(super::scratch::read_json(marker)?["snapshot"].clone())
        .map_err(|_| invalid("数据库恢复标记无效。"))
}
pub(super) fn staging_directory(marker: &Path) -> Result<PathBuf> {
    paths::ensure_safe_absolute(marker).map_err(invalid)?;
    let value = super::scratch::read_json(marker)?;
    let name = value["stagingDirectoryName"]
        .as_str()
        .filter(|name| name.starts_with("pending-") && paths::valid_file_name(name))
        .ok_or_else(|| invalid("恢复暂存目录无效。"))?;
    let staging = marker
        .parent()
        .ok_or_else(|| invalid("恢复目录无效。"))?
        .join(name);
    paths::ensure_safe_absolute(&staging).map_err(invalid)?;
    Ok(staging)
}
impl Drop for PreparedRestore {
    fn drop(&mut self) {
        if self.marker.try_exists().is_ok_and(|exists| !exists) {
            let _ = fs::remove_dir_all(&self.staging);
        }
    }
}
pub(super) fn confirm_task_marker(
    cache: &Path,
    marker: &Path,
    succeeded: impl FnOnce(&str) -> Result<bool>,
) -> Result<bool> {
    if !marker.try_exists()? {
        return Ok(false);
    }
    let mut value = super::scratch::read_json(marker)?;
    let Some(job) = value["jobId"].as_str() else {
        return Ok(true);
    };
    if job.len() != 32 || !job.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err(invalid("恢复任务标识无效。"));
    }
    if value["taskCommitted"] == true {
        super::tasks::resolve_control(cache, marker, job, true)?;
        return Ok(true);
    }
    let committed = succeeded(job)?;
    super::tasks::resolve_control(cache, marker, job, committed)?;
    if !committed {
        if marker.try_exists()? {
            fs::remove_file(marker)?;
        }
        return Ok(false);
    }
    value["taskCommitted"] = serde_json::json!(true);
    sealed::atomic_write(marker, &serde_json::to_vec(&value)?)?;
    Ok(true)
}
/// A durable decision survives task-history pruning before the next restart.
pub(in crate::engine) fn commit_task_marker(marker: &Path, job: &str) -> Result<()> {
    let mut value = super::scratch::read_json(marker)?;
    if value["jobId"] != job {
        return Err(unavailable("恢复标记与已提交任务不匹配。")
            .coded(crate::api::ErrorCode::DependencyUnavailable));
    }
    if value["taskCommitted"] != true {
        value["taskCommitted"] = serde_json::json!(true);
        sealed::atomic_write(marker, &serde_json::to_vec(&value)?)?;
    }
    Ok(())
}
pub(super) fn staged(
    paths: &RuntimePaths,
    marker: &Path,
    database: &str,
    _space: &std::sync::Arc<crate::temporary_space::Budget>,
) -> Result<Option<PathBuf>> {
    paths::ensure_safe_absolute(marker).map_err(invalid)?;
    if !marker.try_exists()? {
        return Ok(None);
    }
    let staging = staging_directory(marker)?;
    if let Some(SnapshotRestore::Repository {
        provider, sha256, ..
    }) = snapshot(marker)?
    {
        if (provider == "SQLite") != (database == SQLITE) {
            return Err(invalid("恢复点数据库类型不匹配。"));
        }
        super::repository::restore::validate(paths, &staging, &sha256, &provider)?;
        if database == SQLITE {
            export_doc_storage::verify_sqlite_backup(&staging.join(SQLITE))?;
            let db = export_doc_storage::Connection::sqlite_read_only(&staging.join(SQLITE))?;
            verify_inventory(paths, &staging, |cursor| {
                Ok(db.file_objects(cursor, 200, false)?)
            })?;
        }
        return Ok(Some(staging));
    }
    Err(invalid(
        "恢复标记不是本版本恢复点，已保留现场；旧格式请使用原版本工具处理。",
    ))
}

pub(super) fn verify_inventory(
    paths: &RuntimePaths,
    staging: &Path,
    rows: impl FnMut(&str) -> Result<Vec<export_doc_storage::FileObject>>,
) -> Result<()> {
    let mut staged = paths.clone();
    staged.data_root = staging.into();
    super::super::file_store::FileStore::open(&staged, true)?.verify_inventory(rows)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn restore_publication_obeys_cancellation_and_final_authorization() {
        use crate::{
            engine::test_support::{ScratchDirectory, administrator},
            generated_api::*,
        };
        use std::{
            sync::mpsc,
            time::{Duration, Instant},
        };
        for scenario in ["success", "cancel", "denied"] {
            let root = ScratchDirectory::new();
            let service = root.service();
            let paths = root.paths();
            let directory = paths.data_root.join("DisasterRecovery");
            fs::create_dir_all(&directory).unwrap();
            let guard = lock(&paths).unwrap();
            let staging = super::super::scratch::create(&directory, "pending-").unwrap();
            let marker = directory.join(".pending-restore.json");
            let prepared = PreparedRestore {
                staging: staging.clone(),
                snapshot: None,
                marker: marker.clone(),
            };
            assert!(!marker.exists());
            let (release, wait) = mpsc::channel();
            let job = service
                .jobs
                .start(
                    &administrator(),
                    GET_JOB,
                    "Restore",
                    "恢复提交",
                    move |_| {
                        wait.recv_timeout(Duration::from_secs(5)).unwrap();
                        Ok(prepared.into_task(guard, "restore-test", "恢复提交"))
                    },
                )
                .unwrap();
            let id = job["jobId"].as_str().unwrap();
            if scenario == "cancel" {
                service
                    .jobs
                    .operation(CANCEL_JOB, &administrator(), id)
                    .unwrap();
            }
            if scenario == "denied" {
                service
                    .store
                    .transaction(|tx| {
                        let actor = administrator();
                        let mut user = crate::engine::store::get(tx, "users", actor.id)?;
                        user["isActive"] = serde_json::json!(false);
                        crate::engine::store::save(
                            tx,
                            "users",
                            actor.id,
                            user,
                            Some(actor.name.clone()),
                            &actor,
                            "disable",
                        )?;
                        Ok(())
                    })
                    .unwrap();
            }
            release.send(()).unwrap();
            let deadline = Instant::now() + Duration::from_secs(5);
            loop {
                let job = service.jobs.get(&administrator(), id).unwrap();
                if job["status"] != "Running" {
                    assert_eq!(
                        job["status"],
                        match scenario {
                            "success" => "Succeeded",
                            "cancel" => "Canceled",
                            _ => "Failed",
                        }
                    );
                    break;
                }
                assert!(Instant::now() < deadline);
                std::thread::sleep(Duration::from_millis(10));
            }
            assert_eq!(marker.exists(), scenario == "success");
            assert_eq!(staging.exists(), scenario == "success");
            if scenario == "success" {
                service
                    .jobs
                    .operation(DELETE_JOB, &administrator(), id)
                    .unwrap();
                assert!(confirm_task_marker(&paths.cache_root, &marker, |_| Ok(true)).unwrap());
                assert!(
                    confirm_task_marker(&paths.cache_root, &marker, |_| panic!(
                        "must not query replaced DB"
                    ))
                    .unwrap()
                );
            }
            service.close().unwrap();
        }
    }

    #[test]
    fn failed_task_marker_is_removed_and_unknown_commit_stops_recovery() {
        let root = crate::engine::test_support::ScratchDirectory::new();
        let marker = root.paths().data_root.join("marker.json");
        let bytes = serde_json::to_vec(&serde_json::json!({"jobId":"a".repeat(32)})).unwrap();
        fs::write(&marker, &bytes).unwrap();
        assert!(
            confirm_task_marker(&root.paths().cache_root, &marker, |_| Err(unavailable(
                "DB offline"
            )))
            .is_err()
        );
        assert_eq!(fs::read(&marker).unwrap(), bytes);
        assert!(!confirm_task_marker(&root.paths().cache_root, &marker, |_| Ok(false)).unwrap());
        assert!(!marker.exists());
    }
}
