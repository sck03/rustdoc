use std::{
    env,
    error::Error,
    ffi::OsString,
    fmt,
    fs::{self, OpenOptions},
    io::{Read, Seek, Write},
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

use tauri::Manager;

const RUNTIME_PATHS_CONFIG_FILE_NAME: &str = "runtime-paths.json";
const RUNTIME_PATHS_CONFIG_BACKUP_FILE_NAME: &str = "runtime-paths.json.bak";
const RUNTIME_PATHS_CONFIG_SCHEMA_VERSION: u32 = 1;
const RUNTIME_CONFIG_ROOT_ENVIRONMENT_VARIABLE: &str = "EXPORTDOCMANAGER_RUNTIME_CONFIG_ROOT";
const PORTABLE_RUNTIME_MARKER_FILE_NAME: &str = "portable-runtime.json";
const RUNTIME_LAYOUT_MANIFEST_FILE_NAME: &str = "runtime-layout.json";
const DATA_ROOT_MIGRATION_MARKER_FILE_NAME: &str = "pending-data-root-migration.json";
const DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME: &str = ".exportdoc-data-root-migration-complete";
const DATA_ROOT_MIGRATION_SCHEMA_VERSION: u32 = 1;

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimePathsConfig {
    schema_version: u32,
    data_root: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    source: Option<String>,
}

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct PendingDataRootMigration {
    schema_version: u32,
    source_root: String,
    target_root: String,
    requested_at_unix_seconds: u64,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct PortableRuntimeMarker {
    schema_version: u32,
    mode: String,
}

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct RuntimeStorageContext {
    data_root: String,
    portable: bool,
    migration_supported: bool,
    storage_policy: &'static str,
}

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DataRootMigrationScheduleResult {
    current_data_root: String,
    target_data_root: String,
    restart_required: bool,
    message: String,
}

#[derive(Debug)]
struct RuntimePathsConfigReadError {
    message: String,
    recoverable: bool,
}

impl RuntimePathsConfigReadError {
    fn recoverable(message: impl Into<String>) -> Self {
        Self {
            message: message.into(),
            recoverable: true,
        }
    }

    fn incompatible(message: impl Into<String>) -> Self {
        Self {
            message: message.into(),
            recoverable: false,
        }
    }
}

impl fmt::Display for RuntimePathsConfigReadError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.message)
    }
}

impl Error for RuntimePathsConfigReadError {}

#[derive(Clone)]
pub(crate) struct RuntimePaths {
    pub(crate) app_root: PathBuf,
    pub(crate) data_root: PathBuf,
    pub(crate) log_root: PathBuf,
    pub(crate) sidecar_path: PathBuf,
    pub(crate) runtime_config_root: PathBuf,
    pub(crate) portable: bool,
}

pub(crate) fn prepare_runtime_paths(app: &tauri::App) -> Result<RuntimePaths, Box<dyn Error>> {
    let app_root_argument = runtime_arg_value("--app-root");
    let data_root_argument = runtime_arg_value("--data-root");
    let default_app_root = match app.path().resource_dir() {
        Ok(path) => path,
        Err(_) => current_exe_dir()?,
    };
    let app_root = app_root_argument
        .or_else(|| env::var_os("EXPORTDOCMANAGER_APP_ROOT").map(PathBuf::from))
        .unwrap_or(default_app_root);
    let portable = is_portable_runtime(&app_root)?;
    let explicit_data_root =
        data_root_argument.or_else(|| env::var_os("EXPORTDOCMANAGER_DATA_ROOT").map(PathBuf::from));
    let runtime_config_root = if portable {
        app_root.clone()
    } else {
        env::var_os(RUNTIME_CONFIG_ROOT_ENVIRONMENT_VARIABLE)
            .map(PathBuf::from)
            .unwrap_or(app.path().app_config_dir()?)
    };
    if !portable {
        apply_pending_data_root_migration(&runtime_config_root)?;
    }
    let data_root = resolve_data_root(
        &app_root,
        &runtime_config_root,
        explicit_data_root,
        portable,
    )?;

    ensure_directory(&app_root)?;
    ensure_runtime_data_directories(&data_root)?;
    let log_root = data_root.join("Logs");

    let sidecar_path = resolve_sidecar_path(&app_root)?;

    Ok(RuntimePaths {
        app_root,
        data_root,
        log_root,
        sidecar_path,
        runtime_config_root,
        portable,
    })
}

fn runtime_arg_value(name: &str) -> Option<PathBuf> {
    runtime_arg_value_from(env::args_os().skip(1), name)
}

pub(crate) fn explicit_data_root_hint() -> Option<PathBuf> {
    runtime_arg_value("--data-root")
        .or_else(|| env::var_os("EXPORTDOCMANAGER_DATA_ROOT").map(PathBuf::from))
}

fn runtime_arg_value_from<I>(args: I, name: &str) -> Option<PathBuf>
where
    I: IntoIterator<Item = OsString>,
{
    let mut args = args.into_iter();
    while let Some(argument) = args.next() {
        if argument == name {
            return args.next().map(PathBuf::from);
        }

        let argument_text = argument.to_string_lossy();
        let prefix = format!("{name}=");
        if let Some(value) = argument_text.strip_prefix(&prefix) {
            if !value.trim().is_empty() {
                return Some(PathBuf::from(value));
            }
        }
    }

    None
}

fn resolve_data_root(
    app_root: &Path,
    runtime_config_root: &Path,
    explicit_data_root: Option<PathBuf>,
    portable: bool,
) -> Result<PathBuf, Box<dyn Error>> {
    if let Some(data_root) = explicit_data_root {
        ensure_runtime_data_root_is_usable(&data_root)?;
        return Ok(data_root);
    }

    if portable {
        let data_root = app_root.join("App_Data");
        ensure_runtime_data_root_is_usable(&data_root)?;
        return Ok(data_root);
    }

    if let Some(data_root) = read_persisted_data_root(runtime_config_root, app_root)? {
        ensure_runtime_data_root_is_usable(&data_root)?;
        return Ok(data_root);
    }

    let suggested_data_root = app_root.join("App_Data");
    let selected = prompt_for_runtime_data_root(
        "首次启用需要选择业务数据目录。建议选择非系统盘；只有单一磁盘时也可选择该磁盘上的专用目录。",
        &suggested_data_root,
    )?;
    ensure_runtime_data_root_is_usable(&selected)?;
    let selected = canonical_runtime_data_root(&selected, true)?;
    persist_runtime_data_root(runtime_config_root, &selected)?;
    Ok(selected)
}

fn is_portable_runtime(app_root: &Path) -> Result<bool, Box<dyn Error>> {
    let marker_path = app_root.join(PORTABLE_RUNTIME_MARKER_FILE_NAME);
    if !marker_path.exists() {
        return Ok(false);
    }

    if !app_root.join(RUNTIME_LAYOUT_MANIFEST_FILE_NAME).is_file() {
        return Err(format!(
            "Portable runtime marker '{}' exists without '{}'.",
            marker_path.display(),
            RUNTIME_LAYOUT_MANIFEST_FILE_NAME
        )
        .into());
    }

    let marker_text = fs::read_to_string(&marker_path).map_err(|error| {
        format!(
            "Failed to read portable runtime marker '{}': {error}",
            marker_path.display()
        )
    })?;
    let marker: PortableRuntimeMarker = serde_json::from_str(&marker_text).map_err(|error| {
        format!(
            "Failed to parse portable runtime marker '{}': {error}",
            marker_path.display()
        )
    })?;
    if marker.schema_version != 1 || marker.mode != "portable" {
        return Err(format!(
            "Unsupported portable runtime marker '{}': expected schemaVersion=1 and mode='portable'.",
            marker_path.display()
        )
        .into());
    }

    Ok(true)
}

pub(crate) fn runtime_storage_context(paths: &RuntimePaths) -> RuntimeStorageContext {
    RuntimeStorageContext {
        data_root: paths.data_root.to_string_lossy().into_owned(),
        portable: paths.portable,
        migration_supported: !paths.portable,
        storage_policy: if paths.portable {
            "便携版业务数据固定存放在程序目录旁的 App_Data；复制整套程序目录即可迁移。"
        } else {
            "安装版业务数据存放在首次启用时选择的目录；更换目录会在重启前安排安全迁移。"
        },
    }
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
    ensure_directory(requested_target)?;
    reject_link_like_path(requested_target)?;
    let target_root = canonical_runtime_data_root(requested_target, false)?;
    validate_distinct_migration_roots(&source_root, &target_root)?;
    ensure_directory_is_empty(&target_root)?;
    probe_writable_directory(&target_root)?;

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

fn apply_pending_data_root_migration(runtime_config_root: &Path) -> Result<(), Box<dyn Error>> {
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

    ensure_directory(&target_root)?;
    reject_link_like_path(&target_root)?;
    let target_root = canonical_runtime_data_root(&target_root, false)?;
    validate_distinct_migration_roots(&source_root, &target_root)?;
    ensure_directory_is_empty(&target_root)?;
    probe_writable_directory(&target_root)?;

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
    let source_stats = collect_tree_stats(&source_root, None)?;
    let staging_stats = collect_tree_stats(&staging_root, None)?;
    if source_stats != staging_stats {
        return Err(format!(
            "Data-root migration verification failed: source has {} files / {} bytes, staging has {} files / {} bytes.",
            source_stats.file_count,
            source_stats.total_bytes,
            staging_stats.file_count,
            staging_stats.total_bytes
        )
        .into());
    }

    let completion_path = staging_root.join(DATA_ROOT_MIGRATION_COMPLETE_FILE_NAME);
    let completion_text = format!("{}\n", serde_json::to_string_pretty(&migration)?);
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
                target_root.display(),
                source_root.display()
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

fn read_persisted_data_root(
    runtime_config_root: &Path,
    app_root: &Path,
) -> Result<Option<PathBuf>, Box<dyn Error>> {
    let config_path = runtime_paths_config_path(runtime_config_root);
    let backup_path = runtime_paths_config_backup_path(runtime_config_root);
    if !config_path.exists() {
        if !backup_path.exists() {
            return Ok(None);
        }

        eprintln!(
            "Runtime paths config '{}' was not found; using backup '{}'.",
            config_path.display(),
            backup_path.display()
        );
        return read_data_root_from_config(&backup_path, app_root)
            .map(Some)
            .map_err(|error| Box::new(error) as Box<dyn Error>);
    }

    match read_data_root_from_config(&config_path, app_root) {
        Ok(data_root) => Ok(Some(data_root)),
        Err(primary_error) if primary_error.recoverable && backup_path.exists() => {
            match read_data_root_from_config(&backup_path, app_root) {
                Ok(data_root) => {
                    eprintln!(
                        "Runtime paths config '{}' is invalid ({primary_error}); using backup '{}'.",
                        config_path.display(),
                        backup_path.display()
                    );
                    Ok(Some(data_root))
                }
                Err(backup_error) => Err(format!(
                    "Runtime paths config '{}' is invalid: {primary_error}. Backup '{}' is also invalid: {backup_error}.",
                    config_path.display(),
                    backup_path.display()
                )
                .into()),
            }
        }
        Err(error) => Err(Box::new(error)),
    }
}

fn read_data_root_from_config(
    config_path: &Path,
    app_root: &Path,
) -> Result<PathBuf, RuntimePathsConfigReadError> {
    let config_text = fs::read_to_string(&config_path).map_err(|error| {
        RuntimePathsConfigReadError::recoverable(format!(
            "Failed to read runtime paths config '{}': {error}",
            config_path.display()
        ))
    })?;
    let config: RuntimePathsConfig = serde_json::from_str(&config_text).map_err(|error| {
        RuntimePathsConfigReadError::recoverable(format!(
            "Failed to parse runtime paths config '{}': {error}",
            config_path.display()
        ))
    })?;
    if config.schema_version != RUNTIME_PATHS_CONFIG_SCHEMA_VERSION {
        return Err(RuntimePathsConfigReadError::incompatible(format!(
            "Unsupported runtime paths config schema version {} in '{}'; expected {}.",
            config.schema_version,
            config_path.display(),
            RUNTIME_PATHS_CONFIG_SCHEMA_VERSION
        )));
    }

    let data_root = config.data_root.trim();
    if data_root.is_empty() {
        return Err(RuntimePathsConfigReadError::recoverable(format!(
            "Runtime paths config '{}' did not specify dataRoot.",
            config_path.display()
        )));
    }

    let data_root = PathBuf::from(data_root);
    if data_root.is_absolute() {
        Ok(data_root)
    } else {
        Ok(app_root.join(data_root))
    }
}

fn persist_runtime_data_root(
    runtime_config_root: &Path,
    data_root: &Path,
) -> Result<(), Box<dyn Error>> {
    let config = RuntimePathsConfig {
        schema_version: RUNTIME_PATHS_CONFIG_SCHEMA_VERSION,
        data_root: data_root.to_string_lossy().into_owned(),
        source: Some("tauri-runtime-selection".to_owned()),
    };
    let config_text = serde_json::to_string_pretty(&config)?;
    write_runtime_paths_config_atomically(
        runtime_config_root,
        format!("{config_text}\n").as_bytes(),
    )
}

fn runtime_paths_config_path(runtime_config_root: &Path) -> PathBuf {
    runtime_config_root.join(RUNTIME_PATHS_CONFIG_FILE_NAME)
}

fn runtime_paths_config_backup_path(runtime_config_root: &Path) -> PathBuf {
    runtime_config_root.join(RUNTIME_PATHS_CONFIG_BACKUP_FILE_NAME)
}

fn write_runtime_paths_config_atomically(
    runtime_config_root: &Path,
    content: &[u8],
) -> Result<(), Box<dyn Error>> {
    ensure_directory(runtime_config_root)?;

    let config_path = runtime_paths_config_path(runtime_config_root);
    let backup_path = runtime_paths_config_backup_path(runtime_config_root);
    let temporary_path = runtime_paths_config_temporary_path(runtime_config_root);
    let write_result = (|| -> Result<(), Box<dyn Error>> {
        let mut temporary_file = OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temporary_path)
            .map_err(|error| {
                format!(
                    "Failed to create temporary runtime paths config '{}': {error}",
                    temporary_path.display()
                )
            })?;
        temporary_file.write_all(content).map_err(|error| {
            format!(
                "Failed to write temporary runtime paths config '{}': {error}",
                temporary_path.display()
            )
        })?;
        temporary_file.sync_all().map_err(|error| {
            format!(
                "Failed to flush temporary runtime paths config '{}': {error}",
                temporary_path.display()
            )
        })?;
        drop(temporary_file);

        if config_path.exists() {
            if backup_path.exists() {
                fs::remove_file(&backup_path).map_err(|error| {
                    format!(
                        "Failed to replace runtime paths backup '{}': {error}",
                        backup_path.display()
                    )
                })?;
            }

            fs::rename(&config_path, &backup_path).map_err(|error| {
                format!(
                    "Failed to back up runtime paths config '{}' to '{}': {error}",
                    config_path.display(),
                    backup_path.display()
                )
            })?;
        }

        if let Err(error) = fs::rename(&temporary_path, &config_path) {
            if backup_path.exists() && !config_path.exists() {
                let _ = fs::rename(&backup_path, &config_path);
            }

            return Err(format!(
                "Failed to activate runtime paths config '{}': {error}",
                config_path.display()
            )
            .into());
        }

        Ok(())
    })();

    if temporary_path.exists() {
        let _ = fs::remove_file(&temporary_path);
    }

    write_result
}

fn runtime_paths_config_temporary_path(runtime_config_root: &Path) -> PathBuf {
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_nanos())
        .unwrap_or_default();
    runtime_config_root.join(format!(
        ".{RUNTIME_PATHS_CONFIG_FILE_NAME}.{}.{timestamp}.tmp",
        std::process::id()
    ))
}

fn pending_data_root_migration_path(runtime_config_root: &Path) -> PathBuf {
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

    let completed = read_pending_data_root_migration(&completion_path)?;
    Ok(completed.schema_version == expected.schema_version
        && completed.source_root == expected.source_root
        && completed.target_root == expected.target_root
        && completed.requested_at_unix_seconds == expected.requested_at_unix_seconds)
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

fn write_new_file_atomically(target_path: &Path, content: &[u8]) -> Result<(), Box<dyn Error>> {
    if target_path.exists() {
        return Err(format!("File '{}' already exists.", target_path.display()).into());
    }
    let parent = target_path
        .parent()
        .ok_or_else(|| format!("File '{}' has no parent directory.", target_path.display()))?;
    ensure_directory(parent)?;
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_nanos())
        .unwrap_or_default();
    let file_name = target_path
        .file_name()
        .and_then(|value| value.to_str())
        .unwrap_or("runtime-marker");
    let temporary_path = parent.join(format!(
        ".{file_name}.{}.{timestamp}.tmp",
        std::process::id()
    ));
    let write_result = (|| -> Result<(), Box<dyn Error>> {
        let mut file = OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temporary_path)?;
        file.write_all(content)?;
        file.sync_all()?;
        drop(file);
        fs::rename(&temporary_path, target_path)?;
        sync_directory(parent);
        Ok(())
    })();
    if temporary_path.exists() {
        let _ = fs::remove_file(&temporary_path);
    }
    write_result
}

fn ensure_runtime_data_root_is_usable(data_root: &Path) -> Result<(), Box<dyn Error>> {
    validate_migration_root_shape(data_root, "dataRoot")?;
    ensure_runtime_data_directories(data_root)?;
    reject_link_like_path(data_root)?;
    probe_writable_directory(data_root)
}

fn canonical_runtime_data_root(
    data_root: &Path,
    require_expected_layout: bool,
) -> Result<PathBuf, Box<dyn Error>> {
    validate_migration_root_shape(data_root, "dataRoot")?;
    let canonical = fs::canonicalize(data_root).map_err(|error| {
        format!(
            "Failed to resolve data root '{}': {error}",
            data_root.display()
        )
    })?;
    validate_migration_root_shape(&canonical, "dataRoot")?;
    if require_expected_layout {
        ensure_expected_runtime_data_root(&canonical)?;
    }
    Ok(canonical)
}

fn validate_migration_root_shape(path: &Path, field_name: &str) -> Result<(), Box<dyn Error>> {
    if !path.is_absolute() {
        return Err(format!(
            "{field_name} must be an absolute path: '{}'.",
            path.display()
        )
        .into());
    }
    if path.parent().is_none() || path.file_name().is_none() {
        return Err(format!(
            "{field_name} cannot be a filesystem root: '{}'.",
            path.display()
        )
        .into());
    }
    Ok(())
}

fn validate_distinct_migration_roots(
    source_root: &Path,
    target_root: &Path,
) -> Result<(), Box<dyn Error>> {
    if source_root == target_root {
        return Err("新数据目录不能与当前数据目录相同。".into());
    }
    if target_root.starts_with(source_root) || source_root.starts_with(target_root) {
        return Err("新旧数据目录不能互相包含，请选择彼此独立的目录。".into());
    }
    Ok(())
}

fn ensure_expected_runtime_data_root(data_root: &Path) -> Result<(), Box<dyn Error>> {
    for directory_name in [
        "Database",
        "Templates",
        "Files",
        "Exports",
        "SingleWindow",
        "Backups",
        "Cache",
        "Config",
        "Security",
        "WebView",
        "Logs",
    ] {
        if !data_root.join(directory_name).is_dir() {
            return Err(format!(
                "Data root '{}' is missing required runtime directory '{}'.",
                data_root.display(),
                directory_name
            )
            .into());
        }
    }
    Ok(())
}

fn ensure_directory_is_empty(path: &Path) -> Result<(), Box<dyn Error>> {
    if fs::read_dir(path)?.next().is_some() {
        return Err(format!(
            "所选新数据目录 '{}' 不是空目录。为防止混合或覆盖数据，请选择一个空目录。",
            path.display()
        )
        .into());
    }
    Ok(())
}

fn probe_writable_directory(path: &Path) -> Result<(), Box<dyn Error>> {
    let mut random_bytes = [0_u8; 32];
    getrandom::getrandom(&mut random_bytes)
        .map_err(|error| format!("Failed to obtain randomness for data-root probe: {error}"))?;
    let file_name = format!(
        ".exportdoc-write-probe-{}-{}",
        std::process::id(),
        random_bytes[..8]
            .iter()
            .map(|value| format!("{value:02x}"))
            .collect::<String>()
    );
    let probe_path = path.join(file_name);
    let probe_result = (|| -> Result<(), Box<dyn Error>> {
        let mut file = OpenOptions::new()
            .create_new(true)
            .read(true)
            .write(true)
            .open(&probe_path)
            .map_err(|error| format!("Data root '{}' is not writable: {error}", path.display()))?;
        file.write_all(&random_bytes)?;
        file.sync_all()?;
        file.rewind()?;
        let mut read_back = [0_u8; 32];
        file.read_exact(&mut read_back)?;
        if read_back != random_bytes {
            return Err(format!(
                "Data-root write verification failed at '{}'.",
                path.display()
            )
            .into());
        }
        drop(file);
        fs::remove_file(&probe_path)?;
        sync_directory(path);
        Ok(())
    })();
    if probe_path.exists() {
        let _ = fs::remove_file(&probe_path);
    }
    probe_result
}

fn reject_link_like_path(path: &Path) -> Result<(), Box<dyn Error>> {
    let mut current = PathBuf::new();
    for component in path.components() {
        current.push(component.as_os_str());
        if !current.exists() {
            continue;
        }
        let metadata = fs::symlink_metadata(&current)?;
        if metadata.file_type().is_symlink() || is_windows_reparse_point(&metadata) {
            return Err(format!(
                "数据目录路径不能经过符号链接、目录联接或其他重解析点：'{}'.",
                current.display()
            )
            .into());
        }
    }
    Ok(())
}

#[cfg(windows)]
fn is_windows_reparse_point(metadata: &fs::Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0400;
    metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0
}

#[cfg(not(windows))]
fn is_windows_reparse_point(_metadata: &fs::Metadata) -> bool {
    false
}

fn copy_directory_tree(source: &Path, target: &Path) -> Result<(), Box<dyn Error>> {
    fs::create_dir(target).map_err(|error| {
        format!(
            "Failed to create data-root migration staging directory '{}': {error}",
            target.display()
        )
    })?;
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let source_path = entry.path();
        let target_path = target.join(entry.file_name());
        let metadata = fs::symlink_metadata(&source_path)?;
        if metadata.file_type().is_symlink() || is_windows_reparse_point(&metadata) {
            return Err(format!(
                "Data-root migration refuses link-like entry '{}'.",
                source_path.display()
            )
            .into());
        }
        if metadata.is_dir() {
            copy_directory_tree(&source_path, &target_path)?;
        } else if metadata.is_file() {
            let mut source_file = fs::File::open(&source_path)?;
            let mut target_file = OpenOptions::new()
                .create_new(true)
                .write(true)
                .open(&target_path)?;
            std::io::copy(&mut source_file, &mut target_file)?;
            target_file.sync_all()?;
        } else {
            return Err(format!(
                "Data-root migration found unsupported filesystem entry '{}'.",
                source_path.display()
            )
            .into());
        }
    }
    sync_directory(target);
    Ok(())
}

#[derive(Debug, Default, PartialEq, Eq)]
struct TreeStats {
    file_count: u64,
    total_bytes: u64,
}

fn collect_tree_stats(
    path: &Path,
    excluded_name: Option<&str>,
) -> Result<TreeStats, Box<dyn Error>> {
    let mut stats = TreeStats::default();
    collect_tree_stats_into(path, excluded_name, &mut stats)?;
    Ok(stats)
}

fn collect_tree_stats_into(
    path: &Path,
    excluded_name: Option<&str>,
    stats: &mut TreeStats,
) -> Result<(), Box<dyn Error>> {
    for entry in fs::read_dir(path)? {
        let entry = entry?;
        if excluded_name.is_some_and(|name| entry.file_name() == name) {
            continue;
        }
        let entry_path = entry.path();
        let metadata = fs::symlink_metadata(&entry_path)?;
        if metadata.file_type().is_symlink() || is_windows_reparse_point(&metadata) {
            return Err(format!(
                "Link-like entry '{}' is not supported.",
                entry_path.display()
            )
            .into());
        }
        if metadata.is_dir() {
            collect_tree_stats_into(&entry_path, excluded_name, stats)?;
        } else if metadata.is_file() {
            stats.file_count += 1;
            stats.total_bytes = stats.total_bytes.saturating_add(metadata.len());
        }
    }
    Ok(())
}

fn sync_directory(path: &Path) {
    if let Ok(directory) = fs::File::open(path) {
        let _ = directory.sync_all();
    }
}

fn unix_seconds_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_secs())
        .unwrap_or_default()
}

fn prompt_for_runtime_data_root(
    reason: &str,
    default_data_root: &Path,
) -> Result<PathBuf, Box<dyn Error>> {
    rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Warning)
        .set_title("选择程序数据目录")
        .set_description(format!(
            "{reason}\n\n数据库、缓存、单一窗口业务数据和运行期可写数据会放到该目录。不会静默改写到 AppData 或 ProgramData。"
        ))
        .set_buttons(rfd::MessageButtons::Ok)
        .show();

    let selected = rfd::FileDialog::new()
        .set_title("选择程序数据目录")
        .pick_folder()
        .ok_or_else(|| {
            format!(
                "未选择数据目录。默认数据目录 '{}' 不可直接使用；可通过 --data-root 或 EXPORTDOCMANAGER_DATA_ROOT 指定。",
                default_data_root.display()
            )
        })?;

    ensure_runtime_data_directories(&selected)?;
    Ok(selected)
}

fn ensure_runtime_data_directories(data_root: &Path) -> Result<(), Box<dyn Error>> {
    ensure_directory(data_root)?;
    ensure_directory(&data_root.join("Database"))?;
    ensure_directory(&data_root.join("Templates"))?;
    ensure_directory(&data_root.join("Files"))?;
    ensure_directory(&data_root.join("Exports"))?;
    ensure_directory(&data_root.join("SingleWindow"))?;
    ensure_directory(&data_root.join("Backups"))?;
    ensure_directory(&data_root.join("Cache"))?;
    ensure_directory(&data_root.join("Config"))?;
    ensure_directory(&data_root.join("Security"))?;
    ensure_directory(&data_root.join("WebView"))?;
    ensure_directory(&data_root.join("Logs"))?;
    Ok(())
}

fn resolve_sidecar_path(app_root: &Path) -> Result<PathBuf, Box<dyn Error>> {
    if let Some(path) = env::var_os("EXPORTDOCMANAGER_API_SIDECAR").map(PathBuf::from) {
        if path.exists() {
            return Ok(path);
        }
    }

    let file_name = sidecar_file_name();
    let mut candidates = vec![app_root.join("sidecar").join(file_name)];

    if let Some(repo_root) = repo_root_from_manifest() {
        candidates.push(
            repo_root
                .join("src")
                .join("ExportDocManager.Api")
                .join("bin")
                .join("Debug")
                .join("net8.0")
                .join(file_name),
        );
        candidates.push(
            repo_root
                .join("src")
                .join("ExportDocManager.Api")
                .join("bin")
                .join("Release")
                .join("net8.0")
                .join("publish")
                .join(file_name),
        );
    }

    candidates
        .into_iter()
        .find(|path| path.exists())
        .ok_or_else(|| {
            format!(
                "API sidecar executable was not found. Set EXPORTDOCMANAGER_API_SIDECAR or publish it to '{}'.",
                app_root.join("sidecar").display()
            )
            .into()
        })
}

fn sidecar_file_name() -> &'static str {
    if cfg!(windows) {
        "ExportDocManager.Api.exe"
    } else {
        "ExportDocManager.Api"
    }
}

pub(crate) fn current_exe_dir() -> Result<PathBuf, Box<dyn Error>> {
    let exe = env::current_exe()?;
    exe.parent()
        .map(Path::to_path_buf)
        .ok_or_else(|| "Current executable has no parent directory.".into())
}

fn repo_root_from_manifest() -> Option<PathBuf> {
    let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    manifest_dir
        .parent()?
        .parent()?
        .parent()
        .map(Path::to_path_buf)
}

fn ensure_directory(path: &Path) -> Result<(), Box<dyn Error>> {
    fs::create_dir_all(path)
        .map_err(|error| format!("Failed to create directory '{}': {error}", path.display()).into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_runtime_path_arguments_from_split_values() {
        let args = vec![
            OsString::from("--data-root"),
            OsString::from("D:\\ExportDocManagerData"),
            OsString::from("--app-root"),
            OsString::from("D:\\ExportDocManager"),
        ];

        assert_eq!(
            runtime_arg_value_from(args.clone(), "--data-root"),
            Some(PathBuf::from("D:\\ExportDocManagerData"))
        );
        assert_eq!(
            runtime_arg_value_from(args, "--app-root"),
            Some(PathBuf::from("D:\\ExportDocManager"))
        );
    }

    #[test]
    fn reads_runtime_path_arguments_from_equals_values() {
        let args = vec![
            OsString::from("--data-root=D:\\ExportDocManagerData"),
            OsString::from("--app-root=D:\\ExportDocManager"),
        ];

        assert_eq!(
            runtime_arg_value_from(args.clone(), "--data-root"),
            Some(PathBuf::from("D:\\ExportDocManagerData"))
        );
        assert_eq!(
            runtime_arg_value_from(args, "--app-root"),
            Some(PathBuf::from("D:\\ExportDocManager"))
        );
    }

    #[test]
    fn reads_persisted_absolute_runtime_data_root() {
        let app_root = fresh_test_dir("absolute-runtime-data-root");
        let config_root = app_root.join("platform-config");
        fs::create_dir_all(&config_root).unwrap();
        let data_root = absolute_test_data_root("configured-business-data");
        let config = RuntimePathsConfig {
            schema_version: RUNTIME_PATHS_CONFIG_SCHEMA_VERSION,
            data_root: data_root.to_string_lossy().into_owned(),
            source: Some("test".to_owned()),
        };
        fs::write(
            runtime_paths_config_path(&config_root),
            serde_json::to_string(&config).unwrap(),
        )
        .unwrap();

        assert_eq!(
            read_persisted_data_root(&config_root, &app_root).unwrap(),
            Some(data_root)
        );
        assert!(!runtime_paths_config_path(&app_root).exists());
    }

    #[test]
    fn rejects_unsupported_runtime_paths_config_schema() {
        let app_root = fresh_test_dir("unsupported-runtime-paths-schema");
        let config_root = app_root.join("platform-config");
        fs::create_dir_all(&config_root).unwrap();
        fs::write(
            runtime_paths_config_path(&config_root),
            r#"{"schemaVersion":2,"dataRoot":"BusinessData"}"#,
        )
        .unwrap();

        let error = read_persisted_data_root(&config_root, &app_root)
            .unwrap_err()
            .to_string();

        assert!(error.contains("Unsupported runtime paths config schema version 2"));
    }

    #[test]
    fn unsupported_schema_does_not_fall_back_to_older_backup() {
        let app_root = fresh_test_dir("unsupported-runtime-paths-schema-with-backup");
        let config_root = app_root.join("platform-config");
        fs::create_dir_all(&config_root).unwrap();
        fs::write(
            runtime_paths_config_path(&config_root),
            r#"{"schemaVersion":2,"dataRoot":"NewBusinessData"}"#,
        )
        .unwrap();
        fs::write(
            runtime_paths_config_backup_path(&config_root),
            r#"{"schemaVersion":1,"dataRoot":"OldBusinessData"}"#,
        )
        .unwrap();

        let error = read_persisted_data_root(&config_root, &app_root)
            .unwrap_err()
            .to_string();

        assert!(error.contains("Unsupported runtime paths config schema version 2"));
    }

    #[test]
    fn resolves_persisted_relative_runtime_data_root_against_app_root() {
        let app_root = fresh_test_dir("relative-runtime-data-root");
        let config_root = app_root.join("platform-config");
        fs::create_dir_all(&config_root).unwrap();
        fs::write(
            runtime_paths_config_path(&config_root),
            r#"{"schemaVersion":1,"dataRoot":"BusinessData"}"#,
        )
        .unwrap();

        assert_eq!(
            read_persisted_data_root(&config_root, &app_root).unwrap(),
            Some(app_root.join("BusinessData"))
        );
    }

    #[test]
    fn persists_runtime_data_root_as_valid_config() {
        let app_root = fresh_test_dir("persist-runtime-data-root");
        let config_root = app_root.join("platform-config");
        let data_root = app_root.join("BusinessData");

        persist_runtime_data_root(&config_root, &data_root).unwrap();

        assert_eq!(
            read_persisted_data_root(&config_root, &app_root).unwrap(),
            Some(data_root)
        );
        assert!(runtime_paths_config_path(&config_root).exists());
        assert!(!runtime_paths_config_path(&app_root).exists());
    }

    #[test]
    fn persist_keeps_previous_runtime_paths_config_as_backup() {
        let app_root = fresh_test_dir("backup-runtime-data-root");
        let config_root = app_root.join("platform-config");
        let first_data_root = app_root.join("FirstBusinessData");
        let second_data_root = app_root.join("SecondBusinessData");

        persist_runtime_data_root(&config_root, &first_data_root).unwrap();
        persist_runtime_data_root(&config_root, &second_data_root).unwrap();

        assert_eq!(
            read_persisted_data_root(&config_root, &app_root).unwrap(),
            Some(second_data_root)
        );
        assert_eq!(
            read_data_root_from_config(&runtime_paths_config_backup_path(&config_root), &app_root)
                .unwrap(),
            first_data_root
        );
    }

    #[test]
    fn falls_back_to_backup_when_runtime_paths_config_is_corrupted() {
        let app_root = fresh_test_dir("recover-runtime-data-root");
        let config_root = app_root.join("platform-config");
        let first_data_root = app_root.join("FirstBusinessData");
        let second_data_root = app_root.join("SecondBusinessData");

        persist_runtime_data_root(&config_root, &first_data_root).unwrap();
        persist_runtime_data_root(&config_root, &second_data_root).unwrap();
        fs::write(runtime_paths_config_path(&config_root), "{broken-json").unwrap();

        assert_eq!(
            read_persisted_data_root(&config_root, &app_root).unwrap(),
            Some(first_data_root)
        );
    }

    #[test]
    fn detects_valid_portable_runtime_marker() {
        let app_root = fresh_test_dir("portable-runtime-marker");
        fs::create_dir_all(&app_root).unwrap();
        fs::write(
            app_root.join(PORTABLE_RUNTIME_MARKER_FILE_NAME),
            r#"{"schemaVersion":1,"mode":"portable"}"#,
        )
        .unwrap();
        fs::write(app_root.join(RUNTIME_LAYOUT_MANIFEST_FILE_NAME), "{}").unwrap();

        assert!(is_portable_runtime(&app_root).unwrap());
    }

    #[test]
    fn accepts_writable_data_root_without_requiring_a_secondary_volume() {
        let data_root = env::temp_dir()
            .join("ExportDocManagerRuntimePathTests")
            .join(format!("single-volume-{}", std::process::id()));
        let _ = fs::remove_dir_all(&data_root);

        ensure_runtime_data_root_is_usable(&data_root).unwrap();

        assert!(data_root.join("Database").is_dir());
        assert!(data_root.join("Logs").is_dir());
        assert!(data_root.join("WebView").is_dir());
        fs::remove_dir_all(data_root).unwrap();
    }

    #[test]
    fn schedules_and_applies_data_root_migration_before_runtime_start() {
        let root = fresh_test_dir("data-root-migration");
        let app_root = root.join("app");
        let config_root = root.join("platform-config");
        let source_root = root.join("source-data");
        let target_root = root.join("target-data");
        fs::create_dir_all(&app_root).unwrap();
        ensure_runtime_data_directories(&source_root).unwrap();
        fs::write(source_root.join("Database").join("data.db"), b"database").unwrap();
        fs::create_dir_all(&target_root).unwrap();
        persist_runtime_data_root(&config_root, &source_root).unwrap();
        let paths = RuntimePaths {
            app_root: app_root.clone(),
            data_root: source_root.clone(),
            log_root: source_root.join("Logs"),
            sidecar_path: app_root.join("sidecar").join(sidecar_file_name()),
            runtime_config_root: config_root.clone(),
            portable: false,
        };

        let scheduled = schedule_data_root_migration(&paths, &target_root).unwrap();
        assert!(scheduled.restart_required);
        assert!(pending_data_root_migration_path(&config_root).exists());

        apply_pending_data_root_migration(&config_root).unwrap();

        assert!(!source_root.exists());
        assert_eq!(
            fs::read(target_root.join("Database").join("data.db")).unwrap(),
            b"database"
        );
        assert_eq!(
            read_persisted_data_root(&config_root, &app_root).unwrap(),
            Some(fs::canonicalize(&target_root).unwrap())
        );
        assert!(!pending_data_root_migration_path(&config_root).exists());
    }

    #[test]
    fn rejects_nested_data_root_migration_target() {
        let root = fresh_test_dir("nested-data-root-migration");
        let source_root = root.join("source-data");
        ensure_runtime_data_directories(&source_root).unwrap();
        let target_root = source_root.join("nested-target");
        fs::create_dir_all(&target_root).unwrap();

        let error = validate_distinct_migration_roots(
            &fs::canonicalize(&source_root).unwrap(),
            &fs::canonicalize(&target_root).unwrap(),
        )
        .unwrap_err()
        .to_string();

        assert!(error.contains("不能互相包含"));
    }

    fn fresh_test_dir(name: &str) -> PathBuf {
        let root = env::current_dir()
            .unwrap()
            .join("target")
            .join("runtime-path-tests")
            .join(format!("{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        root
    }

    fn absolute_test_data_root(name: &str) -> PathBuf {
        env::current_dir()
            .unwrap()
            .join("target")
            .join("runtime-path-tests")
            .join("external-data")
            .join(name)
    }
}
