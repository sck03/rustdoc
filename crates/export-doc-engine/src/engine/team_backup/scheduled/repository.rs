//! Owned backup repository. Retention never selects files by age or filename alone.
use super::*;
use serde::{Deserialize, Serialize};
use std::io::Read;

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(super) struct Archive {
    pub id: String,
    pub repository: String,
    pub file_name: String,
    pub created_at: chrono::DateTime<chrono::Utc>,
    pub size_bytes: u64,
    pub sha256: String,
    pub replica_status: String,
    pub replica_target: String,
    pub message: String,
    #[serde(default)]
    pub deleting: bool,
}
pub(super) fn root(paths: &RuntimePaths) -> PathBuf {
    paths.data_root.join("Backups/ScheduledRecovery")
}
fn read_json(path: &Path) -> Result<Value> {
    crate::paths::ensure_safe_absolute(path).map_err(invalid)?;
    let mut bytes = Vec::new();
    fs::File::open(path)?
        .take(32 * 1024 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > 32 * 1024 {
        return Err(unavailable("备份清单过大。"));
    }
    serde_json::from_slice(&bytes).map_err(|_| unavailable("备份仓库清单损坏。"))
}
pub(super) fn ensure(paths: &RuntimePaths) -> Result<String> {
    let root = root(paths);
    crate::paths::ensure_safe_absolute(&root).map_err(invalid)?;
    let marker = root.join(".repository.json");
    if marker.try_exists()? {
        return identity(paths);
    }
    if root.try_exists()? && fs::read_dir(&root)?.next().is_some() {
        return Err(invalid("计划备份目录已有未登记文件，请先由管理员核对。"));
    }
    package::private_directory(&root)?;
    let id = crate::paths::nonce().map_err(unavailable)?;
    sealed::atomic_write(
        &marker,
        &serde_json::to_vec(
            &json!({"schemaVersion":1,"purpose":"scheduled-recovery-backups","id":id}),
        )?,
    )?;
    Ok(id)
}
pub(super) fn identity(paths: &RuntimePaths) -> Result<String> {
    let marker = read_json(&root(paths).join(".repository.json"))?;
    if marker["schemaVersion"] != 1 || marker["purpose"] != "scheduled-recovery-backups" {
        return Err(unavailable("计划备份仓库标记无效。"));
    }
    marker["id"]
        .as_str()
        .filter(|id| valid_id(id))
        .map(str::to_owned)
        .ok_or_else(|| unavailable("计划备份仓库编号无效。"))
}
fn valid_id(id: &str) -> bool {
    id.len() == 32 && id.bytes().all(|c| c.is_ascii_hexdigit())
}
fn meta_path(paths: &RuntimePaths, id: &str) -> Result<PathBuf> {
    if !valid_id(id) {
        return Err(invalid("备份编号无效。"));
    }
    Ok(root(paths).join(format!("{id}.backup.json")))
}
pub(super) fn write(paths: &RuntimePaths, archive: &Archive) -> Result<()> {
    sealed::atomic_write(
        &meta_path(paths, &archive.id)?,
        &serde_json::to_vec(archive)?,
    )
}
pub(super) fn read(paths: &RuntimePaths, id: &str) -> Result<Archive> {
    let path = meta_path(paths, id)?;
    if !path.try_exists()? {
        return Err(error(404, "计划备份不存在。"));
    }
    let archive: Archive =
        serde_json::from_value(read_json(&path)?).map_err(|_| unavailable("计划备份清单无效。"))?;
    if archive.id != id
        || archive.repository != identity(paths)?
        || !crate::paths::valid_file_name(&archive.file_name)
        || ![format!("{id}.edmmigration"), format!("{id}.edmrecovery")].contains(&archive.file_name)
        || archive.sha256.len() != 64
        || !archive.sha256.bytes().all(|c| c.is_ascii_hexdigit())
    {
        return Err(unavailable("计划备份清单与仓库不匹配。"));
    }
    let file = root(paths).join(&archive.file_name);
    crate::paths::ensure_safe_absolute(&file).map_err(invalid)?;
    if !archive.deleting {
        let metadata = fs::metadata(&file)
            .map_err(|_| unavailable("已登记备份文件缺失或不可读，请检查备份目录。"))?;
        if !metadata.is_file() || archive.size_bytes == 0 || metadata.len() != archive.size_bytes {
            return Err(unavailable("已登记备份文件大小或类型已改变。"));
        }
    }
    Ok(archive)
}
pub(super) fn list(paths: &RuntimePaths) -> Result<Vec<Archive>> {
    let root = root(paths);
    crate::paths::ensure_safe_absolute(&root).map_err(invalid)?;
    if !root.try_exists()? {
        return Ok(vec![]);
    }
    identity(paths)?;
    let mut result = Vec::new();
    for entry in fs::read_dir(root)? {
        crate::operation::check()?;
        let entry = entry?;
        let name = entry.file_name();
        if let Some(id) = name
            .to_str()
            .and_then(|name| name.strip_suffix(".backup.json"))
        {
            result.push(read(paths, id)?);
        }
    }
    result.sort_by(|left, right| {
        right
            .created_at
            .cmp(&left.created_at)
            .then_with(|| left.id.cmp(&right.id))
    });
    Ok(result)
}
pub(super) fn download(
    paths: &RuntimePaths,
    id: &str,
) -> Result<crate::engine::backup_io::Download> {
    let archive = read(paths, id)?;
    if archive.deleting {
        return Err(error(404, "此备份已按保留策略清理。"));
    }
    crate::engine::backup_io::Download::open(
        &root(paths).join(&archive.file_name),
        Some((archive.size_bytes, &archive.sha256)),
    )
}
pub(super) fn prune(
    paths: &RuntimePaths,
    plan: &BackupPlan,
    replica: &replica::Target,
) -> Result<()> {
    let now = chrono::Utc::now();
    for (index, mut archive) in list(paths)?.into_iter().enumerate() {
        if index == 0
            || (!archive.deleting
                && index < plan.retention_count
                && (plan.retention_days == 0
                    || archive.created_at
                        >= now - chrono::Duration::days(i64::from(plan.retention_days))))
        {
            continue;
        }
        let path = root(paths).join(&archive.file_name);
        crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if path.try_exists()?
            && crate::engine::backup_io::digest_file(&path)?
                != (archive.size_bytes, archive.sha256.clone())
        {
            return Err(unavailable("旧备份已被修改，保留策略拒绝删除。"));
        }
        archive.deleting = true;
        write(paths, &archive)?;
        // Changing endpoint/account never gives the new target permission to
        // delete archives at an old target. Those remote copies remain there.
        if !archive.replica_target.is_empty() && archive.replica_target == replica.identity() {
            replica.delete(&archive.file_name)?;
        }
        if path.try_exists()? {
            fs::remove_file(&path)?;
        }
        fs::remove_file(meta_path(paths, &archive.id)?)?;
    }
    Ok(())
}
