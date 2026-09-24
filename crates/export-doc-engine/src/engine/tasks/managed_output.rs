//! Large backup results stay on disk instead of recursively entering database backups.
use super::FileOutput;
use crate::engine::{
    auth,
    error::{Result, invalid, unavailable},
    store::{Actor, Store},
};
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use std::{
    fs,
    io::Read,
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
fn digest(path: &Path) -> Result<(u64, String)> {
    let mut file = fs::File::open(path)?;
    let mut hash = Sha256::new();
    let mut size = 0u64;
    let mut buffer = [0; 64 * 1024];
    loop {
        crate::operation::check()?;
        let count = file.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        size += count as u64;
        if size > 256 * 1024 * 1024 {
            return Err(invalid("任务下载超过 256 MiB，请由管理员从备份目录复制。"));
        }
        hash.update(&buffer[..count]);
    }
    if size == 0 {
        return Err(invalid("备份输出为空。"));
    }
    Ok((
        size,
        hash.finalize()
            .iter()
            .map(|byte| format!("{byte:02x}"))
            .collect(),
    ))
}
pub(super) fn describe(store: &Store, actor: &Actor, path: &Path) -> Result<Value> {
    let relative = path
        .strip_prefix(&store.data_root)
        .map_err(|_| invalid("任务输出不在受管目录。"))?;
    let relative = relative
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
    let (size, digest) = digest(&path)?;
    Ok(
        json!({"path":relative,"fileName":path.file_name().and_then(|n| n.to_str()),"size":size,"digest":digest}),
    )
}
pub(super) fn read(store: &Store, actor: &Actor, descriptor: &Value) -> Result<FileOutput> {
    let path = resolve(store, descriptor["path"].as_str().unwrap_or(""))?;
    auth::authorize(actor, permission(&path), "manage")?;
    let mut bytes = Vec::new();
    fs::File::open(&path)?
        .take(256 * 1024 * 1024 + 1)
        .read_to_end(&mut bytes)?;
    if descriptor["size"].as_u64() != Some(bytes.len() as u64)
        || descriptor["digest"] != crate::engine::media::digest(&bytes)
    {
        return Err(unavailable("备份输出已变化，不能下载不完整结果。"));
    }
    Ok(FileOutput {
        file_name: path.file_name().unwrap().to_string_lossy().into_owned(),
        media_type: "application/octet-stream".into(),
        content: bytes,
    })
}
