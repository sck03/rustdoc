use std::{
    error::Error,
    fs,
    path::{Path, PathBuf},
};

use crate::{
    runtime_data_root_network::reject_network_data_root,
    runtime_data_root_storage::{
        canonical_runtime_data_root, copy_directory_tree, directory_tree_size,
        ensure_directory_is_empty, ensure_expected_runtime_data_root, probe_writable_directory,
        reject_link_like_path, sync_directory, unix_seconds_now, validate_distinct_migration_roots,
        validate_migration_root_shape, write_new_file_atomically,
    },
    runtime_paths::{
        ensure_directory, ensure_runtime_data_directories, DataRootMigrationScheduleResult,
        RuntimePaths,
    },
    runtime_paths_config::{persist_runtime_data_root, read_persisted_data_root},
    runtime_tree_manifest::{collect_tree_manifest, TreeManifest},
};

const DATA_ROOT_MIGRATION_MARKER_FILE_NAME: &str = "pending-data-root-migration.json";
const DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME: &str = ".exportdoc-data-root-migration-complete";
const DATA_ROOT_MIGRATION_SCHEMA_VERSION: u32 = 2;
const MINIMUM_MIGRATION_FREE_SPACE_MARGIN_BYTES: u64 = 64 * 1024 * 1024;

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct PendingDataRootMigration {
    schema_version: u32,
    source_root: String,
    target_root: String,
    requested_at_unix_seconds: u64,
}

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct CompletedDataRootMigration {
    schema_version: u32,
    source_root: String,
    target_root: String,
    requested_at_unix_seconds: u64,
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
    if pending_path.exists() {
        return Err("已经安排过一次数据目录迁移，请先重启程序完成迁移。".into());
    }

    let source_root = canonical_runtime_data_root(&paths.data_root, true)?;
    ensure_expected_runtime_data_root(&source_root)?;
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
    if persisted_root != source_root {
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
    let marker_text = format!("{}\n", serde_json::to_string_pretty(&migration)?);
    write_new_file_atomically(&pending_path, marker_text.as_bytes())?;

    Ok(DataRootMigrationScheduleResult {
        current_data_root: source_root.to_string_lossy().into_owned(),
        target_data_root: target_root.to_string_lossy().into_owned(),
        restart_required: true,
        message:
            "数据目录迁移已安排。退出并重新打开程序后，程序会先完成复制、校验和切换，再启动数据库。"
                .to_owned(),
    })
}

pub(crate) fn apply_pending_data_root_migration(
    runtime_config_root: &Path,
) -> Result<(), Box<dyn Error>> {
    let pending_path = pending_data_root_migration_path(runtime_config_root);
    if !pending_path.exists() {
        return Ok(());
    }

    let migration = read_pending_data_root_migration(&pending_path)?;
    let source_root = PathBuf::from(&migration.source_root);
    let target_root = PathBuf::from(&migration.target_root);
    validate_migration_root_shape(&source_root, "sourceRoot")?;
    validate_migration_root_shape(&target_root, "targetRoot")?;

    if target_root.exists() && migration_completion_matches(&target_root, &migration)? {
        return finish_activated_data_root_migration(
            runtime_config_root,
            &pending_path,
            &source_root,
            &target_root,
        );
    }

    let source_root = canonical_runtime_data_root(&source_root, true)?;
    ensure_expected_runtime_data_root(&source_root)?;
    reject_link_like_path(&source_root)?;
    reject_link_like_path(&target_root)?;
    ensure_directory(&target_root)?;
    reject_link_like_path(&target_root)?;
    let target_root = canonical_runtime_data_root(&target_root, false)?;
    validate_distinct_migration_roots(&source_root, &target_root)?;
    ensure_directory_is_empty(&target_root)?;
    probe_writable_directory(&target_root)?;
    ensure_migration_disk_capacity(&source_root, &target_root)?;

    let configured_root = read_persisted_data_root(runtime_config_root, Path::new("."))?
        .ok_or("Pending data-root migration has no persisted source configuration.")?;
    let configured_root = canonical_runtime_data_root(&configured_root, true)?;
    if configured_root != source_root {
        return Err(format!(
            "Pending data-root migration source '{}' does not match configured data root '{}'.",
            source_root.display(),
            configured_root.display()
        )
        .into());
    }

    fs::remove_dir(&target_root).map_err(|error| {
        format!(
            "Failed to prepare empty data-root migration target '{}': {error}",
            target_root.display()
        )
    })?;
    let staging_root = data_root_migration_staging_path(&target_root, &migration)?;
    if staging_root.exists() {
        fs::remove_dir_all(&staging_root).map_err(|error| {
            format!(
                "Failed to clean interrupted data-root migration staging directory '{}': {error}",
                staging_root.display()
            )
        })?;
    }

    copy_directory_tree(&source_root, &staging_root)?;
    let source_manifest = collect_tree_manifest(&source_root, None)?;
    let staging_manifest = collect_tree_manifest(&staging_root, None)?;
    if source_manifest != staging_manifest {
        return Err(format!(
            "Data-root migration verification failed: source has {} files / {} bytes / SHA-256 {}, staging has {} files / {} bytes / SHA-256 {}.",
            source_manifest.file_count, source_manifest.total_bytes, source_manifest.sha256,
            staging_manifest.file_count, staging_manifest.total_bytes, staging_manifest.sha256
        ).into());
    }

    let completion_path = staging_root.join(DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME);
    let completion = CompletedDataRootMigration {
        schema_version: migration.schema_version,
        source_root: migration.source_root.clone(),
        target_root: migration.target_root.clone(),
        requested_at_unix_seconds: migration.requested_at_unix_seconds,
        source_manifest,
    };
    let completion_text = format!("{}\n", serde_json::to_string_pretty(&completion)?);
    write_new_file_atomically(&completion_path, completion_text.as_bytes())?;
    sync_directory(&staging_root);
    fs::rename(&staging_root, &target_root).map_err(|error| {
        format!(
            "Failed to activate migrated data root '{}' from same-disk staging '{}': {error}",
            target_root.display(),
            staging_root.display()
        )
    })?;
    sync_directory(target_root.parent().unwrap_or(Path::new(".")));

    if !migration_completion_matches(&target_root, &migration)? {
        return Err(
            "Activated data-root migration failed its final SHA-256 manifest verification.".into(),
        );
    }
    finish_activated_data_root_migration(
        runtime_config_root,
        &pending_path,
        &source_root,
        &target_root,
    )
}

fn finish_activated_data_root_migration(
    runtime_config_root: &Path,
    pending_path: &Path,
    source_root: &Path,
    target_root: &Path,
) -> Result<(), Box<dyn Error>> {
    reject_link_like_path(target_root)?;
    ensure_runtime_data_directories(target_root)?;
    probe_writable_directory(target_root)?;
    persist_runtime_data_root(runtime_config_root, target_root)?;

    if source_root.exists() {
        reject_link_like_path(source_root)?;
        ensure_expected_runtime_data_root(source_root)?;
        fs::remove_dir_all(source_root).map_err(|error| {
            format!(
                "Migrated data was activated at '{}', but the old data root '{}' could not be removed: {error}. The migration will retry cleanup on next start.",
                target_root.display(), source_root.display()
            )
        })?;
    }
    fs::remove_file(pending_path).map_err(|error| {
        format!(
            "Failed to remove completed data-root migration marker '{}': {error}",
            pending_path.display()
        )
    })?;
    let completion_path = target_root.join(DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME);
    if completion_path.exists() {
        let _ = fs::remove_file(completion_path);
    }
    sync_directory(runtime_config_root);
    Ok(())
}

pub(crate) fn pending_data_root_migration_path(runtime_config_root: &Path) -> PathBuf {
    runtime_config_root.join(DATA_ROOT_MIGRATION_MARKER_FILE_NAME)
}

fn read_pending_data_root_migration(
    marker_path: &Path,
) -> Result<PendingDataRootMigration, Box<dyn Error>> {
    let marker_text = fs::read_to_string(marker_path).map_err(|error| {
        format!(
            "Failed to read pending data-root migration '{}': {error}",
            marker_path.display()
        )
    })?;
    let migration: PendingDataRootMigration =
        serde_json::from_str(&marker_text).map_err(|error| {
            format!(
                "Failed to parse pending data-root migration '{}': {error}",
                marker_path.display()
            )
        })?;
    if migration.schema_version != DATA_ROOT_MIGRATION_SCHEMA_VERSION {
        return Err(format!(
            "Unsupported data-root migration schema version {} in '{}'; expected {}.",
            migration.schema_version,
            marker_path.display(),
            DATA_ROOT_MIGRATION_SCHEMA_VERSION
        )
        .into());
    }
    Ok(migration)
}

fn migration_completion_matches(
    target_root: &Path,
    expected: &PendingDataRootMigration,
) -> Result<bool, Box<dyn Error>> {
    let completion_path = target_root.join(DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME);
    if !completion_path.is_file() {
        return Ok(false);
    }
    let completed: CompletedDataRootMigration =
        serde_json::from_str(&fs::read_to_string(&completion_path)?)?;
    if completed.schema_version != DATA_ROOT_MIGRATION_SCHEMA_VERSION {
        return Ok(false);
    }
    let metadata_matches = completed.schema_version == expected.schema_version
        && completed.source_root == expected.source_root
        && completed.target_root == expected.target_root
        && completed.requested_at_unix_seconds == expected.requested_at_unix_seconds;
    if !metadata_matches {
        return Ok(false);
    }
    let target_manifest =
        collect_tree_manifest(target_root, Some(DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME))?;
    Ok(target_manifest == completed.source_manifest)
}

fn data_root_migration_staging_path(
    target_root: &Path,
    migration: &PendingDataRootMigration,
) -> Result<PathBuf, Box<dyn Error>> {
    let parent = target_root
        .parent()
        .ok_or("Data-root migration target cannot be a filesystem root.")?;
    let file_name = target_root
        .file_name()
        .and_then(|value| value.to_str())
        .ok_or("Data-root migration target must have a valid final directory name.")?;
    Ok(parent.join(format!(
        ".{file_name}.exportdoc-migration-{}.staging",
        migration.requested_at_unix_seconds
    )))
}

fn ensure_migration_disk_capacity(
    source_root: &Path,
    target_root: &Path,
) -> Result<(), Box<dyn Error>> {
    let source_bytes = directory_tree_size(source_root)?;
    let required_bytes = required_migration_capacity(source_bytes);
    let available_bytes = fs2::available_space(target_root).map_err(|error| {
        format!(
            "无法读取新数据目录所在磁盘的剩余空间 '{}'。请确认磁盘已连接且当前账号拥有访问权限：{error}",
            target_root.display()
        )
    })?;
    if available_bytes < required_bytes {
        return Err(format!(
            "新数据目录所在磁盘空间不足：迁移至少需要 {}，当前可用 {}。请清理空间或选择其他磁盘。",
            format_capacity(required_bytes),
            format_capacity(available_bytes)
        )
        .into());
    }
    Ok(())
}

fn required_migration_capacity(source_bytes: u64) -> u64 {
    let proportional_margin = source_bytes / 20;
    source_bytes.saturating_add(proportional_margin.max(MINIMUM_MIGRATION_FREE_SPACE_MARGIN_BYTES))
}

fn format_capacity(bytes: u64) -> String {
    const GIB: f64 = 1024.0 * 1024.0 * 1024.0;
    const MIB: f64 = 1024.0 * 1024.0;
    if bytes >= 1024 * 1024 * 1024 {
        format!("{:.2} GiB", bytes as f64 / GIB)
    } else {
        format!("{:.1} MiB", bytes as f64 / MIB)
    }
}

#[cfg(test)]
mod capacity_tests {
    use super::*;

    #[test]
    fn migration_capacity_keeps_a_minimum_and_proportional_margin() {
        assert_eq!(
            required_migration_capacity(1),
            1 + MINIMUM_MIGRATION_FREE_SPACE_MARGIN_BYTES
        );
        assert_eq!(
            required_migration_capacity(20 * 1024 * 1024 * 1024),
            21 * 1024 * 1024 * 1024
        );
    }

    #[test]
    fn migration_capacity_saturates_instead_of_wrapping() {
        assert_eq!(required_migration_capacity(u64::MAX), u64::MAX);
    }
}
