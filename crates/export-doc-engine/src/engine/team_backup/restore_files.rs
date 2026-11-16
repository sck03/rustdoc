//! Journalled replacement of the few filesystem roots belonging to a recovery package.
use super::{
    error::{Result, invalid, unavailable},
    package, sealed,
};
use crate::paths::{self, RuntimePaths};
use serde::{Deserialize, Serialize};
use std::{fs, path::Path};
#[cfg(test)]
#[path = "tests/restore_files.rs"]
mod tests;

#[derive(Serialize, Deserialize)]
struct Entry {
    target: String,
    source: Option<String>,
    existed: bool,
}
pub(super) fn install(
    paths: &RuntimePaths,
    staging: &Path,
    sqlite: bool,
    mut inventory: impl FnMut(&str) -> Result<Vec<export_doc_storage::FileObject>>,
) -> Result<()> {
    let journal = staging.join("install-journal.json");
    if journal.try_exists()? {
        rollback(paths, staging)?;
    }
    let mut targets = vec![
        (package::KEY, Some(package::KEY)),
        ("Templates", Some("Templates")),
        ("Files", Some("Files")),
        ("Configuration/file-migration.json", None),
        ("Configuration/FileUploads", None),
        (
            "Configuration/file-storage.json",
            Some("Configuration/file-storage.json"),
        ),
    ];
    if sqlite {
        targets.extend([
            (package::SQLITE, Some(package::SQLITE)),
            ("Database/exportdoc-native.db-wal", None),
            ("Database/exportdoc-native.db-shm", None),
            ("Database/exportdoc-native.db-journal", None),
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
                    if entry.target == "Files" {
                        // Only objects registered in the restored snapshot enter the live store.
                        // Leave the authenticated stage intact so interrupted installs can retry.
                        package::private_directory(&target)?;
                        copy(
                            &source.join(".store-id"),
                            &target.join(".store-id"),
                            &paths.disk_space,
                        )?;
                        let mut cursor = String::new();
                        loop {
                            let objects = inventory(&cursor)?;
                            if objects.is_empty() {
                                break;
                            }
                            for object in objects {
                                if object.digest.len() != 64
                                    || !object.digest.bytes().all(|b| b.is_ascii_hexdigit())
                                    || object.digest <= cursor
                                {
                                    return Err(invalid("恢复文件对象清单无效。"));
                                }
                                let relative =
                                    format!("objects/{}/{}", &object.digest[..2], object.digest);
                                copy(
                                    &source.join(&relative),
                                    &target.join(relative),
                                    &paths.disk_space,
                                )?;
                                cursor = object.digest;
                            }
                        }
                    } else {
                        copy(&source, &target, &paths.disk_space)?;
                    }
                }
            }
        }
        if sqlite {
            let database = paths.sqlite_database_path();
            export_doc_storage::verify_sqlite_backup(&database)?;
            // Keep upgrades inside the installation journal so failure restores all original files.
            export_doc_storage::Connection::sqlite(&database)?.checkpoint()?;
        }
        Ok(())
    })();
    if let Err(cause) = result {
        crate::operation::OperationScope::detached(std::time::Duration::from_secs(
            super::super::backup_io::TIMEOUT_SECONDS,
        ))
        .run_result(|| rollback(paths, staging))
        .map_err(|rollback| {
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
    let entries: Vec<Entry> = serde_json::from_value(super::scratch::read_json(&journal)?)
        .map_err(|_| invalid("恢复日志无效。"))?;
    for entry in entries.iter().rev() {
        if !matches!(
            entry.target.as_str(),
            package::KEY
                | "Templates"
                | "Files"
                | "Configuration/file-migration.json"
                | "Configuration/FileUploads"
                | "Configuration/file-storage.json"
                | package::SQLITE
                | "Database/exportdoc-native.db-wal"
                | "Database/exportdoc-native.db-shm"
                | "Database/exportdoc-native.db-journal"
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
fn copy(
    source: &Path,
    target: &Path,
    space: &std::sync::Arc<crate::temporary_space::Budget>,
) -> Result<()> {
    crate::operation::check()?;
    paths::ensure_safe_absolute(source).map_err(invalid)?;
    paths::ensure_safe_absolute(target).map_err(invalid)?;
    if source.is_dir() {
        package::private_directory(target)?;
        for entry in fs::read_dir(source)? {
            let entry = entry?;
            copy(&entry.path(), &target.join(entry.file_name()), space)?;
        }
    } else {
        package::private_directory(target.parent().unwrap())?;
        let mut source = fs::File::open(source)?;
        let mut output = fs::OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(target)?;
        super::super::backup_io::copy_chunks(
            &mut source,
            &mut space
                .at(target.parent().unwrap().to_path_buf())
                .writer(&mut output)?,
            u64::MAX,
            |_| {},
        )?;
        output.sync_all()?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            fs::set_permissions(target, fs::Permissions::from_mode(0o600))?;
        }
    }
    Ok(())
}
