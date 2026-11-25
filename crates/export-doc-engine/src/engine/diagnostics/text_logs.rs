use super::super::{
    auth,
    error::{Result, invalid},
    settings,
    store::{Actor, Store},
};
use crate::{
    contracts,
    paths::{RuntimePaths, ensure_safe_absolute},
};
use chrono::Utc;
use serde_json::{Value, json};
use std::{fs, path::PathBuf, time::SystemTime};

pub(super) fn cleanup(store: &Store, paths: &RuntimePaths, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.settings", "manage")?;
    let settings = settings::current(store)?;
    let log_days = settings
        .pointer("/system/logRetentionDays")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let log_count = settings
        .pointer("/system/logRetainedFileCount")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let (by_age, by_count) = cleanup_text_logs(paths, log_days, log_count)?;
    let mut response = contracts::initial(contracts::schema("ApiSystemLogCleanupResponse"));
    response["success"] = json!(true);
    response["message"] = json!(format!("文本日志清理已完成：{} 个。", by_age + by_count));
    response["deletedTextLogs"] = json!(by_age + by_count);
    response["deletedTextLogsByAge"] = json!(by_age);
    response["deletedTextLogsByCount"] = json!(by_count);
    response["logRoot"] = json!(paths.log_root);
    response["storagePolicy"] =
        json!("手动清理仅处理运行数据根 Logs 目录内的文本日志，审计记录由独立审计页面管理。");
    Ok(response)
}
/// Age-based deletion first; the retained-file-count cap then keeps the newest logs.
fn cleanup_text_logs(paths: &RuntimePaths, days: i64, retained: i64) -> Result<(i64, i64)> {
    if days <= 0 && retained <= 0 {
        return Ok((0, 0));
    }
    let cutoff: SystemTime = (Utc::now() - chrono::Duration::days(days.max(0))).into();
    let mut files: Vec<(PathBuf, SystemTime)> = Vec::new();
    for item in fs::read_dir(&paths.log_root)? {
        crate::operation::check()?;
        let item = item?;
        let name = item.file_name();
        let name = name.to_string_lossy();
        if !(name.ends_with(".log")
            || name.ends_with(".jsonl")
            || name
                .rsplit_once(".log.")
                .is_some_and(|(_, suffix)| suffix.parse::<u32>().is_ok()))
        {
            continue;
        }
        let file_path = item.path();
        ensure_safe_absolute(&file_path).map_err(invalid)?;
        let metadata = item.metadata()?;
        if metadata.is_file() {
            files.push((file_path, metadata.modified()?));
        }
    }
    files.sort_by(|left, right| right.1.cmp(&left.1));
    let (mut by_age, mut by_count) = (0, 0);
    for (index, (path, modified)) in files.iter().enumerate() {
        crate::operation::check()?;
        let aged = days > 0 && *modified < cutoff;
        let surplus = retained > 0 && index as i64 >= retained;
        if aged || surplus {
            ensure_safe_absolute(path).map_err(invalid)?;
            fs::remove_file(path)?;
            if aged { by_age += 1 } else { by_count += 1 }
        }
    }
    Ok((by_age, by_count))
}
