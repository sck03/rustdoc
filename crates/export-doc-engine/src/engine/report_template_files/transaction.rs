//! Narrow rollback boundary for managed report-template file mutations.
//!
//! A database transaction cannot undo filesystem writes. This coordinator
//! snapshots only the paths touched by one report-template operation, then
//! restores those paths if the database transaction or a later file step
//! fails. Snapshots live under the managed cache root and are removed after a
//! successful commit.
use super::*;

struct Snapshot {
    target: PathBuf,
    backup: Option<PathBuf>,
}

pub(crate) struct FileTransaction {
    root: PathBuf,
    snapshot_root: Option<PathBuf>,
    snapshots: Vec<Snapshot>,
}

impl FileTransaction {
    pub(super) fn new(paths: &RuntimePaths) -> Result<Self> {
        Ok(Self {
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
        let backup = if target.is_file() {
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
                    fs::create_dir_all(&root)?;
                    paths::ensure_safe_absolute(&root).map_err(invalid)?;
                    self.snapshot_root = Some(root.clone());
                    root
                }
            };
            let backup = snapshot_root.join(format!("{}.bak", self.snapshots.len()));
            fs::copy(&target, &backup)?;
            Some(backup)
        } else {
            None
        };
        self.snapshots.push(Snapshot { target, backup });
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
        self.cleanup();
        if failures.is_empty() {
            Ok(())
        } else {
            Err(failures.join(";"))
        }
    }

    fn cleanup(&mut self) {
        if let Some(root) = self.snapshot_root.take() {
            let _ = fs::remove_dir_all(root);
        }
        self.snapshots.clear();
    }
}
