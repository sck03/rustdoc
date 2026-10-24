//! Backup results remain on disk; durable descriptors bind their size and digest.
use super::ManagedOutput;
use crate::engine::{
    auth,
    backup_io::Download,
    error::{Result, invalid, unavailable},
    store::{Actor, Store},
};
use serde_json::{Value, json};
use std::{
    fs,
    path::{Path, PathBuf},
};
fn resolve(store: &Store, relative: &str) -> Result<PathBuf> {
    let segments: Vec<_> = relative.split('/').collect();
    if segments.len() < 2
        || !matches!(segments[0], "Backups" | "DisasterRecovery")
        || segments
            .iter()
            .any(|part| !crate::paths::valid_file_name(part))
    {
        return Err(invalid("任务输出路径无效。"));
    }
    let path = segments
        .iter()
        .fold(store.data_root.clone(), |root, part| root.join(part));
    crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
    Ok(path)
}
fn permission(path: &Path) -> &'static str {
    if path
        .extension()
        .is_some_and(|e| e == "edmrecovery" || e == "edmmigration")
    {
        "system.disaster-recovery"
    } else {
        "system.backup"
    }
}

pub(super) fn describe(store: &Store, actor: &Actor, output: &ManagedOutput) -> Result<Value> {
    let relative = output
        .path
        .strip_prefix(&store.data_root)
        .map_err(|_| invalid("任务输出不在受管目录。"))?
        .components()
        .map(|p| {
            p.as_os_str()
                .to_str()
                .ok_or_else(|| invalid("路径编码无效。"))
        })
        .collect::<Result<Vec<_>>>()?
        .join("/");
    let path = resolve(store, &relative)?;
    auth::authorize(actor, permission(&path), "manage")?;
    if output.size == 0 || fs::metadata(&path)?.len() != output.size {
        return Err(unavailable("备份输出大小无效或已变化。"));
    }
    Ok(
        json!({"path":relative,"fileName":path.file_name().and_then(|n| n.to_str()),"size":output.size,"digest":output.digest}),
    )
}
pub(super) fn open(store: &Store, actor: &Actor, descriptor: &Value) -> Result<Download> {
    let path = resolve(store, descriptor["path"].as_str().unwrap_or(""))?;
    auth::authorize(actor, permission(&path), "manage")?;
    let size = descriptor["size"]
        .as_u64()
        .ok_or_else(|| unavailable("备份大小缺失。"))?;
    let digest = descriptor["digest"]
        .as_str()
        .ok_or_else(|| unavailable("备份摘要缺失。"))?;
    Download::open(&path, Some((size, digest)))
}
