use std::{
    error::Error,
    fs::{self, File, OpenOptions},
    io::Read,
    path::{Path, PathBuf},
};

use export_doc_engine::maintenance::{
    DATA_ROOT_MIGRATION_PENDING_FILE, DATA_ROOT_MIGRATION_REDIRECT_FILE, ensure_data_root_available,
};
use fs2::FileExt;
use serde::de::DeserializeOwned;

use crate::{
    runtime_data_root_network::reject_network_data_root,
    runtime_data_root_storage::{
        canonical_runtime_data_root, directory_tree_size, ensure_directory_is_empty,
        ensure_expected_runtime_data_root, probe_writable_directory, reject_link_like_path,
        sync_directory, unix_seconds_now, validate_distinct_migration_roots,
        validate_migration_root_shape, write_new_file_atomically,
    },
    runtime_path_identity::same_path,
    runtime_paths::{DataRootMigrationScheduleResult, RuntimePaths, ensure_directory},
    runtime_paths_config::{persist_runtime_data_root, read_persisted_data_root},
    runtime_tree_manifest::{
        COMPLETION_FILE, INSTANCE_LOCK, TreeManifest, collect_tree_manifest, copy_verified_tree,
    },
};

const DATA_ROOT_MIGRATION_MARKER_FILE_NAME: &str = "pending-data-root-migration.json";
const DATA_ROOT_MIGRATION_SCHEMA_VERSION: u32 = 2;
const MINIMUM_MIGRATION_FREE_SPACE_MARGIN_BYTES: u64 = 64 * 1024 * 1024;
const MAX_MARKER_BYTES: u64 = 64 * 1024 * 1024;

#[derive(Clone, serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct PendingDataRootMigration {
    schema_version: u32,
    source_root: String,
    target_root: String,
    requested_at_unix_seconds: u64,
}

impl PendingDataRootMigration {
    fn matches(&self, other: &Self) -> bool {
        self.schema_version == other.schema_version
            && same_path(Path::new(&self.source_root), Path::new(&other.source_root))
            && same_path(Path::new(&self.target_root), Path::new(&other.target_root))
            && self.requested_at_unix_seconds == other.requested_at_unix_seconds
    }
}

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct MigrationJournal {
    schema_version: u32,
    migration: PendingDataRootMigration,
    source_manifest: TreeManifest,
}

pub(crate) fn schedule_data_root_migration(
    paths: &RuntimePaths,
    requested_target: &Path,
) -> Result<DataRootMigrationScheduleResult, Box<dyn Error>> {
    if paths.portable {
        return Err(
            "便携版的数据目录固定为程序目录旁的 App_Data；如需迁移，请退出程序后复制完整便携目录。"
                .into(),
        );
    }
    let pending_path = pending_data_root_migration_path(&paths.runtime_config_root);
    if pending_path.try_exists()? {
        return Err("已经安排过一次数据目录迁移，请先重启程序完成迁移。".into());
    }
    let source_root = canonical_runtime_data_root(&paths.data_root, true)?;
    ensure_data_root_available(&source_root)?;
    validate_migration_root_shape(requested_target, "targetRoot")?;
    reject_network_data_root(requested_target)?;
    reject_link_like_path(requested_target)?;
    ensure_directory(requested_target)?;
    reject_link_like_path(requested_target)?;
    let target_root = canonical_runtime_data_root(requested_target, false)?;
    validate_distinct_migration_roots(&source_root, &target_root)?;
    ensure_directory_is_empty(&target_root)?;
    probe_writable_directory(&target_root)?;
    ensure_migration_disk_capacity(&source_root, &target_root)?;

    let persisted_root = read_persisted_data_root(&paths.runtime_config_root, &paths.app_root)?
        .ok_or("当前数据目录由命令行或环境变量临时指定，不能在界面中安排永久迁移。")?;
    let persisted_root = canonical_runtime_data_root(&persisted_root, true)?;
    if !same_path(&persisted_root, &source_root) {
        return Err(
            "当前运行数据目录与持久化配置不一致，不能安排迁移。请移除临时路径参数后重启。".into(),
        );
    }
    let migration = PendingDataRootMigration {
        schema_version: DATA_ROOT_MIGRATION_SCHEMA_VERSION,
        source_root: source_root.to_string_lossy().into_owned(),
        target_root: target_root.to_string_lossy().into_owned(),
        requested_at_unix_seconds: unix_seconds_now(),
    };
    write_marker(&pending_path, &migration)?;
    Ok(DataRootMigrationScheduleResult {
        current_data_root: migration.source_root,
        target_data_root: migration.target_root,
        restart_required: true,
        message: "数据目录迁移已安排。重启后会在实例锁内复制、校验并切换；旧目录保留封存标记，防止误开为空库。".to_owned(),
    })
}

pub(crate) fn apply_pending_data_root_migration(
    runtime_config_root: &Path,
    app_root: &Path,
) -> Result<(), Box<dyn Error>> {
    let pending_path = pending_data_root_migration_path(runtime_config_root);
    reject_link_like_path(&pending_path)?;
    if !pending_path.try_exists()? {
        return Ok(());
    }
    let migration: PendingDataRootMigration = read_marker(&pending_path)?;
    if migration.schema_version != DATA_ROOT_MIGRATION_SCHEMA_VERSION {
        return Err("数据目录迁移标记版本不受支持，已保留原数据和标记。".into());
    }
    let source_root = PathBuf::from(&migration.source_root);
    let target_root = PathBuf::from(&migration.target_root);
    for root in [&source_root, &target_root] {
        validate_migration_root_shape(root, "migrationRoot")?;
        reject_network_data_root(root)?;
        reject_link_like_path(root)?;
    }
    let source_root = canonical_runtime_data_root(&source_root, false)?;
    ensure_directory(&target_root)?;
    let target_root = canonical_runtime_data_root(&target_root, false)?;
    validate_distinct_migration_roots(&source_root, &target_root)?;

    // These are the exact locks used by ordinary SQLite opens. Their paths stay
    // in place through copying, publication, and source cleanup on every OS.
    let _source_lock = lock_root(&source_root, false)?;
    let _legacy_lock = lock_legacy_source(&source_root)?;
    let _target_lock = lock_root(&target_root, true)?;
    let redirected = source_redirect_matches(&source_root, &migration)?;
    let journal_path = target_root.join(DATA_ROOT_MIGRATION_PENDING_FILE);
    let completion_path = target_root.join(COMPLETION_FILE);
    if target_root
        .join(DATA_ROOT_MIGRATION_REDIRECT_FILE)
        .try_exists()?
    {
        return Err("迁移目标是已封存的旧数据目录，已停止操作。".into());
    }
    if completion_path.try_exists()? {
        read_journal(&completion_path, &migration, &target_root)?;
        if !redirected || journal_path.try_exists()? {
            return Err("数据目录迁移完成状态不一致，已保留所有内容。".into());
        }
        // The completion fence is published only after durable configuration and
        // verified source cleanup. A reopened target may already contain new data.
        persist_migrated_root(runtime_config_root, &target_root)?;
        return finish_committed(
            runtime_config_root,
            &pending_path,
            &completion_path,
            &target_root,
        );
    }
    let journal = if journal_path.try_exists()? {
        read_journal(&journal_path, &migration, &target_root)?
    } else {
        if redirected {
            return Err("迁移目标缺少恢复清单，旧目录仍被封存；请恢复迁移标记。".into());
        }
        ensure_data_root_available(&source_root)?;
        ensure_expected_runtime_data_root(&source_root)?;
        let reservation = collect_tree_manifest(&target_root)?;
        if reservation
            .entries
            .iter()
            .any(|entry| entry.path != "Locks" || entry.sha256.is_some())
        {
            return Err("迁移目标出现未登记内容，已保留且不会覆盖。".into());
        }
        let configured = read_persisted_data_root(runtime_config_root, app_root)?
            .ok_or("数据目录迁移缺少原路径配置。")?;
        reject_link_like_path(&configured)?;
        let configured = canonical_runtime_data_root(&configured, true)?;
        if !same_path(&configured, &source_root) {
            return Err("数据目录迁移源与持久化配置不一致。".into());
        }
        probe_writable_directory(&target_root)?;
        ensure_migration_disk_capacity(&source_root, &target_root)?;
        export_doc_engine::secrets::private_directory(&target_root)?;
        let journal = MigrationJournal {
            schema_version: 1,
            migration: migration.clone(),
            source_manifest: collect_tree_manifest(&source_root)?,
        };
        journal.source_manifest.validate(&target_root)?;
        write_marker(&journal_path, &journal)?;
        journal
    };
    if redirected {
        journal.source_manifest.verify(&target_root)?;
    } else {
        ensure_data_root_available(&source_root)?;
        journal.source_manifest.verify(&source_root)?;
        copy_verified_tree(&journal.source_manifest, &source_root, &target_root)?;
        journal.source_manifest.verify(&source_root)?;
        write_marker(
            &source_root.join(DATA_ROOT_MIGRATION_REDIRECT_FILE),
            &migration,
        )?;
    }
    // A crash between either write is recoverable while the pending fence remains.
    persist_migrated_root(runtime_config_root, &target_root)?;
    journal
        .source_manifest
        .remove_verified_source(&source_root)?;
    fs::rename(&journal_path, &completion_path)?;
    sync_directory(&target_root);
    finish_committed(
        runtime_config_root,
        &pending_path,
        &completion_path,
        &target_root,
    )
}

fn finish_committed(
    runtime_config_root: &Path,
    pending_path: &Path,
    completion_path: &Path,
    target_root: &Path,
) -> Result<(), Box<dyn Error>> {
    fs::remove_file(pending_path)?;
    sync_directory(runtime_config_root);
    fs::remove_file(completion_path)?;
    sync_directory(target_root);
    Ok(())
}

fn persist_migrated_root(config_root: &Path, target: &Path) -> Result<(), Box<dyn Error>> {
    // The second durable rotation replaces the obsolete source in the backup.
    // The migration fence remains present until both copies identify the target.
    persist_runtime_data_root(config_root, target)?;
    persist_runtime_data_root(config_root, target)
}

fn lock_root(root: &Path, create: bool) -> Result<File, Box<dyn Error>> {
    let locks = root.join("Locks");
    if create {
        reject_link_like_path(&locks)?;
        fs::create_dir_all(&locks)?;
    }
    let path = root.join(INSTANCE_LOCK);
    reject_link_like_path(&path)?;
    let file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(&path)?;
    file.try_lock_exclusive().map_err(|cause| {
        format!(
            "数据目录已被程序或维护操作占用，迁移未开始：{}：{cause}",
            root.display()
        )
    })?;
    Ok(file)
}

fn lock_legacy_source(root: &Path) -> Result<Option<File>, Box<dyn Error>> {
    let path = root.join("native-instance.lock");
    reject_link_like_path(&path)?;
    if !path.try_exists()? {
        return Ok(None);
    }
    let file = OpenOptions::new().read(true).write(true).open(path)?;
    file.try_lock_exclusive()
        .map_err(|cause| format!("旧数据目录实例锁被占用：{cause}"))?;
    Ok(Some(file))
}

fn source_redirect_matches(
    root: &Path,
    migration: &PendingDataRootMigration,
) -> Result<bool, Box<dyn Error>> {
    let path = root.join(DATA_ROOT_MIGRATION_REDIRECT_FILE);
    reject_link_like_path(&path)?;
    if !path.try_exists()? {
        return Ok(false);
    }
    let redirected: PendingDataRootMigration = read_marker(&path)?;
    if !redirected.matches(migration) {
        return Err("源数据目录属于另一项迁移，已保留原内容。".into());
    }
    Ok(true)
}

fn read_journal(
    path: &Path,
    migration: &PendingDataRootMigration,
    root: &Path,
) -> Result<MigrationJournal, Box<dyn Error>> {
    let journal: MigrationJournal = read_marker(path)?;
    if journal.schema_version != 1 || !journal.migration.matches(migration) {
        return Err("数据目录迁移恢复清单身份不一致。".into());
    }
    journal.source_manifest.validate(root)?;
    Ok(journal)
}

fn read_marker<T: DeserializeOwned>(path: &Path) -> Result<T, Box<dyn Error>> {
    reject_link_like_path(path)?;
    let file = File::open(path)?;
    if !file.metadata()?.is_file() || file.metadata()?.len() > MAX_MARKER_BYTES {
        return Err("数据目录迁移标记类型或容量无效。".into());
    }
    let mut bytes = Vec::new();
    file.take(MAX_MARKER_BYTES + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 > MAX_MARKER_BYTES {
        return Err("数据目录迁移标记超过容量上限。".into());
    }
    Ok(serde_json::from_slice(&bytes)?)
}

fn write_marker(path: &Path, value: &impl serde::Serialize) -> Result<(), Box<dyn Error>> {
    let bytes = serde_json::to_vec(value)?;
    if bytes.len() as u64 > MAX_MARKER_BYTES {
        return Err("数据目录迁移清单超过容量上限。".into());
    }
    write_new_file_atomically(path, &bytes)
}

pub(crate) fn pending_data_root_migration_path(runtime_config_root: &Path) -> PathBuf {
    runtime_config_root.join(DATA_ROOT_MIGRATION_MARKER_FILE_NAME)
}

fn ensure_migration_disk_capacity(source: &Path, target: &Path) -> Result<(), Box<dyn Error>> {
    let required = required_migration_capacity(directory_tree_size(source)?);
    let available = fs2::available_space(target)?;
    if available < required {
        return Err(format!(
            "新数据目录磁盘空间不足：至少需要 {}，当前可用 {}。",
            format_capacity(required),
            format_capacity(available)
        )
        .into());
    }
    Ok(())
}

fn required_migration_capacity(source_bytes: u64) -> u64 {
    source_bytes.saturating_add((source_bytes / 20).max(MINIMUM_MIGRATION_FREE_SPACE_MARGIN_BYTES))
}

fn format_capacity(bytes: u64) -> String {
    if bytes >= 1024 * 1024 * 1024 {
        format!("{:.2} GiB", bytes as f64 / (1024. * 1024. * 1024.))
    } else {
        format!("{:.1} MiB", bytes as f64 / (1024. * 1024.))
    }
}

#[cfg(test)]
#[path = "runtime_data_root_migration_tests.rs"]
mod tests;
