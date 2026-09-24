//! Journalled replacement of the few filesystem roots belonging to a recovery package.
use super::{
    error::{Result, invalid, unavailable},
    package, sealed,
};
use crate::paths::{self, RuntimePaths};
use serde::{Deserialize, Serialize};
use std::{fs, path::Path};

#[derive(Serialize, Deserialize)]
struct Entry {
    target: String,
    source: Option<String>,
    existed: bool,
}
pub(super) fn install(paths: &RuntimePaths, staging: &Path, sqlite: bool) -> Result<()> {
    let journal = staging.join("install-journal.json");
    if journal.try_exists()? {
        rollback(paths, staging)?;
    }
    let mut targets = vec![
        (package::KEY, Some(package::KEY)),
        ("Templates", Some("Templates")),
    ];
    if sqlite {
        targets.extend([
            ("exportdoc-native.db", Some(package::SQLITE)),
            ("exportdoc-native.db-wal", None),
            ("exportdoc-native.db-shm", None),
        ]);
    }
    let entries: Vec<_> = targets
        .into_iter()
        .map(|(target, source)| {
            let path = paths.data_root.join(target);
            paths::ensure_safe_absolute(&path).map_err(invalid)?;
            Ok(Entry {
                target: target.into(),
                source: source.map(str::to_owned),
                existed: path.try_exists()?,
            })
        })
        .collect::<Result<_>>()?;
    package::private_directory(&staging.join("previous"))?;
    sealed::atomic_write(&journal, &serde_json::to_vec(&entries)?)?;
    let result: Result<()> = (|| {
        for entry in &entries {
            let target = paths.data_root.join(&entry.target);
            let backup = staging.join("previous").join(&entry.target);
            if entry.existed {
                fs::create_dir_all(backup.parent().unwrap())?;
                fs::rename(&target, &backup)?;
            }
            if let Some(source) = &entry.source {
                let source = staging.join(source);
                if source.try_exists()? {
                    copy(&source, &target)?;
                }
            }
        }
        if sqlite {
            export_doc_storage::verify_sqlite_backup(&paths.data_root.join("exportdoc-native.db"))?;
        }
        Ok(())
    })();
    if let Err(cause) = result {
        rollback(paths, staging).map_err(|rollback| {
            unavailable(format!(
                "恢复失败且回滚失败：{rollback}；原错误：{cause}。保留恢复目录并停止启动。"
            ))
        })?;
        return Err(cause);
    }
    Ok(())
}
pub(super) fn rollback(paths: &RuntimePaths, staging: &Path) -> Result<()> {
    let journal = staging.join("install-journal.json");
    let entries: Vec<Entry> =
        serde_json::from_slice(&fs::read(&journal)?).map_err(|_| invalid("恢复日志无效。"))?;
    for entry in entries.iter().rev() {
        if !matches!(
            entry.target.as_str(),
            package::KEY
                | "Templates"
                | "exportdoc-native.db"
                | "exportdoc-native.db-wal"
                | "exportdoc-native.db-shm"
        ) {
            return Err(invalid("恢复日志路径无效。"));
        }
        let target = paths.data_root.join(&entry.target);
        let backup = staging.join("previous").join(&entry.target);
        paths::ensure_safe_absolute(&target).map_err(invalid)?;
        paths::ensure_safe_absolute(&backup).map_err(invalid)?;
        if backup.try_exists()? || !entry.existed {
            remove(&target)?;
            if entry.existed {
                fs::rename(backup, target)?;
            }
        }
    }
    fs::remove_file(journal)?;
    Ok(())
}
fn remove(path: &Path) -> Result<()> {
    paths::ensure_safe_absolute(path).map_err(invalid)?;
    if path.is_dir() {
        fs::remove_dir_all(path)?;
    } else if path.try_exists()? {
        fs::remove_file(path)?;
    }
    Ok(())
}
fn copy(source: &Path, target: &Path) -> Result<()> {
    paths::ensure_safe_absolute(source).map_err(invalid)?;
    paths::ensure_safe_absolute(target).map_err(invalid)?;
    if source.is_dir() {
        package::private_directory(target)?;
        for entry in fs::read_dir(source)? {
            let entry = entry?;
            copy(&entry.path(), &target.join(entry.file_name()))?;
        }
    } else {
        package::private_directory(target.parent().unwrap())?;
        fs::copy(source, target)?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            fs::set_permissions(target, fs::Permissions::from_mode(0o600))?;
        }
    }
    Ok(())
}
