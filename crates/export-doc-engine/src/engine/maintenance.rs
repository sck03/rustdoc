use super::{
    auth,
    error::{Result, invalid, unavailable},
    store::{Actor, Store},
};
use crate::{
    contracts,
    paths::{self, RuntimePaths},
};
use serde_json::{Value, json};
use std::{
    fs,
    path::{Path, PathBuf},
};

pub(super) fn acquire(lock: &std::sync::RwLock<()>) -> Result<std::sync::RwLockWriteGuard<'_, ()>> {
    wait(|| lock.try_write())
}

pub(super) fn acquire_read(
    lock: &std::sync::RwLock<()>,
) -> Result<std::sync::RwLockReadGuard<'_, ()>> {
    wait(|| lock.try_read())
}

fn wait<T>(mut acquire: impl FnMut() -> std::sync::TryLockResult<T>) -> Result<T> {
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(30);
    loop {
        crate::operation::check()?;
        match acquire() {
            Ok(guard) => return Ok(guard),
            Err(std::sync::TryLockError::Poisoned(_)) => {
                return Err(unavailable("数据库维护状态异常。"));
            }
            Err(std::sync::TryLockError::WouldBlock) if std::time::Instant::now() < deadline => {
                std::thread::sleep(std::time::Duration::from_millis(5));
            }
            Err(std::sync::TryLockError::WouldBlock) => {
                return Err(super::error::error(504, "等待数据库维护超时，请稍后重试。"));
            }
        }
    }
}

#[cfg(test)]
mod lock_tests {
    use super::*;
    use std::{
        sync::RwLock,
        time::{Duration, Instant},
    };

    #[test]
    fn reads_and_writes_honor_cancellation_while_maintenance_is_held() {
        let lock = RwLock::new(());
        let held = lock.write().unwrap();
        let scope = crate::operation::OperationScope::new(Duration::from_secs(5));
        std::thread::scope(|threads| {
            let waiter = threads.spawn(|| scope.run(|| acquire_read(&lock).map(drop)));
            std::thread::sleep(Duration::from_millis(25));
            let canceled = Instant::now();
            scope.cancel();
            assert_eq!(waiter.join().unwrap().unwrap_err().status, Some(504));
            assert!(canceled.elapsed() < Duration::from_secs(1));
        });
        assert_eq!(scope.run(|| acquire(&lock)).unwrap_err().status, Some(504));
        drop(held);
        drop(acquire_read(&lock).unwrap());
    }
}

pub fn root(paths: &RuntimePaths) -> Result<PathBuf> {
    let root = paths.data_root.join("Backups");
    paths::ensure_safe_absolute(&root).map_err(invalid)?;
    fs::create_dir_all(&root)?;
    Ok(root)
}
pub fn list(store: &Store, paths: &RuntimePaths, actor: &Actor) -> Result<Value> {
    let _ = store;
    auth::authorize(actor, "system.backup", "manage")?;
    let root = root(paths)?;
    let mut backups = vec![];
    for entry in fs::read_dir(&root)? {
        let entry = entry?;
        let path = entry.path();
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if path
            .extension()
            .is_none_or(|extension| extension != "sqlite3")
        {
            continue;
        }
        let metadata = entry.metadata()?;
        if !metadata.is_file() {
            continue;
        }
        let created = chrono::DateTime::<chrono::Utc>::from(metadata.modified()?).to_rfc3339();
        let mut item = contracts::initial(contracts::schema("ApiBackupItemDto"));
        for (key, value) in [
            ("fileName", json!(entry.file_name().to_string_lossy())),
            ("fullPath", json!(path)),
            ("sizeBytes", json!(metadata.len())),
            ("length", json!(metadata.len())),
            ("lastWriteTime", json!(created)),
        ] {
            item[key] = value;
        }
        backups.push(item);
    }
    backups.sort_by(|a, b| {
        b["lastWriteTime"]
            .as_str()
            .cmp(&a["lastWriteTime"].as_str())
            .then_with(|| b["fileName"].as_str().cmp(&a["fileName"].as_str()))
    });
    Ok(json!({"backups":backups,"backupRoot":root,"storagePolicy":"备份保存在受管数据目录内"}))
}
pub fn create(store: &Store, paths: &RuntimePaths, actor: &Actor) -> Result<PathBuf> {
    auth::authorize(actor, "system.backup", "manage")?;
    create_verified(store, paths)
}
fn create_verified(store: &Store, paths: &RuntimePaths) -> Result<PathBuf> {
    let root = root(paths)?;
    let name = format!(
        "ExportDoc-{}-{}.sqlite3",
        chrono::Utc::now().format("%Y%m%d-%H%M%S"),
        &paths::nonce().map_err(unavailable)?[..8]
    );
    let target = root.join(name);
    let temporary = root.join(format!(
        ".backup-{}.tmp",
        paths::nonce().map_err(unavailable)?
    ));
    let result = (|| {
        paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
        store.connection()?.backup(&temporary)?;
        paths::ensure_safe_absolute(&target).map_err(invalid)?;
        fs::rename(&temporary, &target)?;
        Ok(target)
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result
}
pub fn restore(store: &Store, paths: &RuntimePaths, actor: &Actor, body: &Value) -> Result<()> {
    auth::authorize(actor, "system.backup", "manage")?;
    let name = body["backupFileName"].as_str().unwrap_or("");
    if !paths::valid_file_name(name)
        || Path::new(name)
            .extension()
            .is_none_or(|extension| extension != "sqlite3")
    {
        return Err(invalid("备份文件名无效。"));
    }
    if body["confirmationText"] != "RESTORE" {
        return Err(invalid("请输入 RESTORE 确认覆盖当前数据库。"));
    }
    let source_path = root(paths)?.join(name);
    paths::ensure_safe_absolute(&source_path).map_err(invalid)?;
    store.connection()?.verify_backup(&source_path)?;
    let working = paths
        .cache_root
        .join("DatabaseRestore")
        .join(paths::nonce().map_err(unavailable)?);
    paths::ensure_safe_absolute(&working).map_err(unavailable)?;
    crate::secrets::private_directory(&working).map_err(unavailable)?;
    let result: Result<()> = (|| {
        let snapshot = working.join("restore.sqlite3");
        export_doc_storage::prepare_sqlite_restore(&source_path, &snapshot)?;
        // Upgrade the snapshot first; an incompatible backup cannot replace the running database.
        create_verified(store, paths)?;
        let connection = store.connection()?;
        connection.restore(&snapshot)?;
        connection.checkpoint()?;
        Ok(())
    })();
    let cleanup = fs::remove_dir_all(&working);
    result?;
    cleanup?;
    Ok(())
}

pub fn cleanup(store: &Store, paths: &RuntimePaths, actor: &Actor, body: &Value) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let days = body["daysToKeep"]
        .as_i64()
        .filter(|days| (1..=3650).contains(days))
        .ok_or_else(|| invalid("保留天数须为 1–3650 的整数。"))?;
    let cutoff = chrono::Utc::now() - chrono::Duration::days(days);
    let snapshot = list(store, paths, actor)?;
    let entries = snapshot["backups"]
        .as_array()
        .ok_or_else(|| unavailable("备份目录格式无效。"))?;
    let mut removed = 0;
    // Always retain the newest backup, even when every backup predates retention.
    for entry in entries.iter().skip(1) {
        crate::operation::check()?;
        let modified = chrono::DateTime::parse_from_rfc3339(
            entry["lastWriteTime"]
                .as_str()
                .ok_or_else(|| unavailable("备份时间缺失。"))?,
        )
        .map_err(|_| unavailable("备份时间无效。"))?;
        if modified >= cutoff {
            continue;
        }
        let name = entry["fileName"]
            .as_str()
            .filter(|name| paths::valid_file_name(name))
            .ok_or_else(|| invalid("备份文件名无效。"))?;
        let candidate = root(paths)?.join(name);
        paths::ensure_safe_absolute(&candidate).map_err(invalid)?;
        fs::remove_file(candidate)?;
        removed += 1;
    }
    let mut result = list(store, paths, actor)?;
    result["success"] = json!(true);
    result["message"] = json!(format!(
        "已清理 {removed} 个旧备份，保留最近 {days} 天及最新一份备份。"
    ));
    Ok(result)
}
