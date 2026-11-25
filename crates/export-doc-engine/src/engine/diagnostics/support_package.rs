//! Bounded diagnostic summaries; business documents and credentials are never included.
use super::super::{
    auth,
    error::{Result, invalid, unavailable},
    settings,
    store::{Actor, Store},
};
use crate::{
    contracts,
    paths::{RuntimePaths, atomic_write, ensure_safe_absolute, nonce},
};
use chrono::Utc;
use serde_json::{Value, json};
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    time::SystemTime,
};
use zip::{CompressionMethod, ZipWriter, write::SimpleFileOptions};
const SUPPORT_PACKAGE_POLICY: &str = "支持包只包含诊断摘要（版本、数据库类型、受管目录、脱敏配置摘要与审计统计）；不收集密码、令牌、业务数据或数据库内容。";

fn support_root(paths: &RuntimePaths) -> Result<PathBuf> {
    let root = paths.data_root.join("SupportPackages");
    ensure_safe_absolute(&root).map_err(invalid)?;
    fs::create_dir_all(&root)?;
    Ok(root)
}
fn entry(
    writer: &mut ZipWriter<std::io::Cursor<Vec<u8>>>,
    name: &str,
    value: &Value,
) -> Result<()> {
    writer
        .start_file(
            name,
            SimpleFileOptions::default().compression_method(CompressionMethod::Deflated),
        )
        .map_err(|error| unavailable(error.to_string()))?;
    writer
        .write_all(&serde_json::to_vec(value)?)
        .map_err(|error| unavailable(error.to_string()))?;
    Ok(())
}
pub(super) fn build(store: &Store, paths: &RuntimePaths) -> Result<(String, Vec<u8>)> {
    let mut writer = ZipWriter::new(std::io::Cursor::new(Vec::new()));
    entry(
        &mut writer,
        "diagnostics/runtime.json",
        &runtime_diagnostics(store)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/database.json",
        &audit_statistics(store)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/paths.json",
        &paths_diagnostics(paths)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/settings-redacted.json",
        &settings_summary(store)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/logs-inventory.json",
        &log_inventory(paths)?,
    )?;
    let bytes = writer
        .finish()
        .map_err(|error| unavailable(error.to_string()))?
        .into_inner();
    let name = format!(
        "{}_{}_support_package.zip",
        Utc::now().format("%Y%m%d_%H%M%S"),
        &nonce().map_err(unavailable)?[..8]
    );
    Ok((name, bytes))
}
fn runtime_diagnostics(store: &Store) -> Result<Value> {
    let provider = store.provider()?;
    Ok(json!({
        "productVersion": crate::build_info::PRODUCT_VERSION,
        "informationalVersion": crate::build_info::INFORMATIONAL_VERSION,
        "databaseProvider": provider,
        "databaseProviderKey": if provider == "SQLite" { "sqlite" } else { "postgresql" },
        "operatingSystem": std::env::consts::OS,
        "architecture": std::env::consts::ARCH,
        "generatedAt": Utc::now().to_rfc3339(),
        "storagePolicy": SUPPORT_PACKAGE_POLICY
    }))
}
fn paths_diagnostics(paths: &RuntimePaths) -> Result<Value> {
    let item = |key: &str, label: &str, path: &Path| json!({"key": key, "label": label, "path": path, "exists": path.exists()});
    Ok(json!({"roots": [
        item("appRoot", "程序", &paths.app_root),
        item("dataRoot", "业务数据", &paths.data_root),
        item("logRoot", "运行日志", &paths.log_root),
        item("cacheRoot", "缓存", &paths.cache_root),
        item("fontPath", "报表字体", &paths.font_path)
    ], "storagePolicy": SUPPORT_PACKAGE_POLICY}))
}
fn settings_summary(store: &Store) -> Result<Value> {
    let settings = settings::current(store)?;
    let item = |path: &str| settings.pointer(path).cloned().unwrap_or(Value::Null);
    Ok(json!({
        "logRetentionDays": item("/system/logRetentionDays"),
        "logRetainedFileCount": item("/system/logRetainedFileCount"),
        "databaseProvider": item("/system/databaseProvider"),
        "storagePolicy": SUPPORT_PACKAGE_POLICY
    }))
}
fn audit_statistics(store: &Store) -> Result<Value> {
    store.read(|tx| Ok(serde_json::to_value(tx.audit_statistics()?)?))
}
fn log_inventory(paths: &RuntimePaths) -> Result<Value> {
    let mut files = vec![];
    for item in fs::read_dir(&paths.log_root)? {
        crate::operation::check()?;
        let item = item?;
        let metadata = item.metadata()?;
        if !metadata.is_file() {
            continue;
        }
        let modified = metadata
            .modified()
            .ok()
            .and_then(|time| time.duration_since(SystemTime::UNIX_EPOCH).ok())
            .map(|duration| duration.as_secs())
            .unwrap_or(0);
        files.push(json!({"fileName": item.file_name().to_string_lossy(), "sizeBytes": metadata.len(), "modifiedUnixSeconds": modified}));
    }
    files.sort_by(|left, right| {
        right["modifiedUnixSeconds"]
            .as_i64()
            .unwrap_or(0)
            .cmp(&left["modifiedUnixSeconds"].as_i64().unwrap_or(0))
    });
    Ok(json!({"logRoot": paths.log_root, "count": files.len(), "files": files}))
}
pub(super) fn save(store: &Store, paths: &RuntimePaths, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.settings", "manage")?;
    let (name, bytes) = build(store, paths)?;
    let root = support_root(paths)?;
    let path = root.join(&name);
    ensure_safe_absolute(&path).map_err(invalid)?;
    atomic_write(&path, &bytes).map_err(unavailable)?;
    let mut response = contracts::initial(contracts::schema("ApiSupportPackageResponse"));
    response["success"] = json!(true);
    response["message"] = json!("支持包已保存到运行数据根 SupportPackages 目录。");
    response["fileName"] = json!(name);
    response["fullPath"] = json!(path);
    response["sizeBytes"] = json!(bytes.len());
    response["supportPackageRoot"] = json!(root);
    response["storagePolicy"] = json!(SUPPORT_PACKAGE_POLICY);
    Ok(response)
}
