//! Narrow rollback boundary for managed report-template file mutations.
//!
//! A database transaction cannot undo filesystem writes. This coordinator
//! snapshots only the paths touched by one report-template operation, then
//! restores those paths if the database transaction or a later file step
//! fails. Snapshots live under the managed cache root and are removed after a
//! successful commit.
use super::*;
use fs2::FileExt;
use std::time::{Duration, Instant};

/// Always acquired before the database mutex; protects files through rollback.
pub(crate) fn lock(paths: &RuntimePaths) -> Result<fs::File> {
    let path = paths
        .data_root
        .join("Locks")
        .join("report-template-storage.lock");
    paths::ensure_safe_absolute(&path).map_err(unavailable)?;
    fs::create_dir_all(path.parent().unwrap())?;
    let file = fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)?;
    let started = Instant::now();
    loop {
        crate::operation::check()?;
        match file.try_lock_exclusive() {
            Ok(()) => {
                let recovery = paths.data_root.join("Cache").join("TemplateTransactions");
                paths::ensure_safe_absolute(&recovery).map_err(unavailable)?;
                match fs::read_dir(recovery) {
                    Ok(mut entries) => {
                        if let Some(entry) = entries.next() {
                            entry?;
                            return Err(unavailable(
                                "发现未完成的模板文件事务，请由管理员核对恢复快照后再继续操作。",
                            ));
                        }
                    }
                    Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => (),
                    Err(cause) => return Err(cause.into()),
                }
                return Ok(file);
            }
            Err(cause) if cause.raw_os_error() == fs2::lock_contended_error().raw_os_error() => {
                if started.elapsed() >= Duration::from_secs(5) {
                    return Err(error(429, "模板存储正在使用，请稍后重试。"));
                }
                std::thread::sleep(Duration::from_millis(10));
            }
            Err(cause) => return Err(cause.into()),
        }
    }
}

struct Snapshot {
    target: PathBuf,
    backup: Option<PathBuf>,
}

pub(crate) struct FileTransaction {
    _lock: fs::File,
    root: PathBuf,
    snapshot_root: Option<PathBuf>,
    snapshots: Vec<Snapshot>,
}

impl FileTransaction {
    pub(super) fn new(paths: &RuntimePaths) -> Result<Self> {
        Ok(Self {
            _lock: lock(paths)?,
            root: user_root(paths),
            snapshot_root: None,
            snapshots: Vec::new(),
        })
    }

    pub(super) fn execute<T>(
        &mut self,
        operation: impl FnOnce(&mut Self) -> Result<T>,
    ) -> Result<T> {
        match operation(self) {
            Ok(value) => {
                self.cleanup();
                Ok(value)
            }
            Err(original) => match self.rollback() {
                Ok(()) => Err(original),
                Err(rollback) => Err(unavailable(format!(
                    "报表模板文件事务回滚失败:{rollback}。原始错误:{original}"
                ))),
            },
        }
    }

    pub(super) fn capture(&mut self, target: &Path) -> Result<()> {
        let target = target.to_path_buf();
        ensure_managed(&target, &self.root)?;
        if self
            .snapshots
            .iter()
            .any(|snapshot| snapshot.target == target)
        {
            return Ok(());
        }
        if target.is_dir() {
            return Err(invalid("报表模板事务目标必须是文件。"));
        }
        let snapshot_root = match &self.snapshot_root {
            Some(root) => root.clone(),
            None => {
                let root = self
                    .root
                    .parent()
                    .ok_or_else(|| unavailable("模板事务快照目录无效。"))?
                    .join("Cache")
                    .join("TemplateTransactions")
                    .join(format!("template-{}", paths::nonce().map_err(unavailable)?));
                paths::ensure_safe_absolute(&root).map_err(invalid)?;
                fs::create_dir_all(&root)?;
                self.snapshot_root = Some(root.clone());
                root
            }
        };
        let backup = if target.is_file() {
            let backup = snapshot_root.join(format!("{}.bak", self.snapshots.len()));
            fs::copy(&target, &backup)?;
            Some(backup)
        } else {
            None
        };
        self.snapshots.push(Snapshot { target, backup });
        let manifest = json!({"schemaVersion":1,"files":self.snapshots.iter().map(|snapshot| json!({
            "target":normalize_relative(snapshot.target.strip_prefix(&self.root).expect("managed target")),
            "backup":snapshot.backup.as_ref().and_then(|path| path.file_name()).and_then(|name| name.to_str())
        })).collect::<Vec<_>>()});
        paths::atomic_write(
            &snapshot_root.join("manifest.json"),
            &serde_json::to_vec(&manifest)?,
        )
        .map_err(unavailable)?;
        Ok(())
    }

    fn rollback(&mut self) -> std::result::Result<(), String> {
        let mut failures = Vec::new();
        for snapshot in self.snapshots.iter().rev() {
            let result = match &snapshot.backup {
                Some(backup) => fs::read(backup)
                    .map_err(|error| error.to_string())
                    .and_then(|bytes| {
                        paths::atomic_write(&snapshot.target, &bytes)
                            .map_err(|error| error.to_string())
                    }),
                None => {
                    if snapshot.target.exists() {
                        fs::remove_file(&snapshot.target).map_err(|error| error.to_string())
                    } else {
                        Ok(())
                    }
                }
            };
            if let Err(error) = result {
                failures.push(format!("{}:{error}", snapshot.target.display()));
            }
        }
        if failures.is_empty() {
            self.cleanup();
            Ok(())
        } else {
            Err(format!(
                "{}；恢复快照保留于 {}",
                failures.join(";"),
                self.snapshot_root
                    .as_ref()
                    .map(|path| path.display().to_string())
                    .unwrap_or_default()
            ))
        }
    }

    fn cleanup(&mut self) {
        if let Some(root) = self.snapshot_root.take() {
            let _ = fs::remove_dir_all(root);
        }
        self.snapshots.clear();
    }
}
