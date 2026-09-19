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
            ("createdAt", json!(created)),
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
    // Preserve a verified copy of the current database before the atomic restore.
    create_verified(store, paths)?;
    let connection = store.connection()?;
    connection.restore(&source_path)?;
    connection.checkpoint()?;
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
