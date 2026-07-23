use std::{
    env,
    error::Error,
    ffi::OsString,
    fmt,
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

use tauri::Manager;

const RUNTIME_PATHS_CONFIG_FILE_NAME: &str = "runtime-paths.json";
const RUNTIME_PATHS_CONFIG_BACKUP_FILE_NAME: &str = "runtime-paths.json.bak";
const RUNTIME_PATHS_CONFIG_SCHEMA_VERSION: u32 = 1;

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimePathsConfig {
    schema_version: u32,
    data_root: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    source: Option<String>,
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
    let explicit_data_root =
        data_root_argument.or_else(|| env::var_os("EXPORTDOCMANAGER_DATA_ROOT").map(PathBuf::from));
    let data_root = resolve_data_root(&app_root, explicit_data_root)?;

    ensure_directory(&app_root)?;
    ensure_runtime_data_directories(&data_root)?;
    let log_root = data_root.join("Logs");

    let sidecar_path = resolve_sidecar_path(&app_root)?;

    Ok(RuntimePaths {
        app_root,
        data_root,
        log_root,
        sidecar_path,
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
    explicit_data_root: Option<PathBuf>,
) -> Result<PathBuf, Box<dyn Error>> {
    if let Some(data_root) = explicit_data_root {
        return Ok(data_root);
    }

    if let Some(data_root) = read_persisted_data_root(app_root)? {
        return Ok(data_root);
    }

    let default_data_root = app_root.join("App_Data");
    if is_system_drive_path(&default_data_root) {
        let selected = prompt_for_runtime_data_root(
            "默认数据目录位于系统盘。请选择一个非系统盘业务数据目录，例如 D:\\ExportDocManagerData。",
            &default_data_root,
        )?;
        try_persist_runtime_data_root(app_root, &selected);
        return Ok(selected);
    }

    match ensure_runtime_data_directories(&default_data_root) {
        Ok(()) => Ok(default_data_root),
        Err(error) => {
            let selected = prompt_for_runtime_data_root(
                &format!(
                    "默认数据目录不可写或无法创建：{error}\n请选择一个可写的业务数据目录，推荐放在非系统盘。"
                ),
                &default_data_root,
            )?;
            try_persist_runtime_data_root(app_root, &selected);
            Ok(selected)
        }
    }
}

fn read_persisted_data_root(app_root: &Path) -> Result<Option<PathBuf>, Box<dyn Error>> {
    let config_path = runtime_paths_config_path(app_root);
    let backup_path = runtime_paths_config_backup_path(app_root);
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

fn try_persist_runtime_data_root(app_root: &Path, data_root: &Path) {
    if let Err(error) = persist_runtime_data_root(app_root, data_root) {
        eprintln!(
            "Failed to persist runtime data root to '{}': {error}",
            runtime_paths_config_path(app_root).display()
        );
    }
}

fn persist_runtime_data_root(app_root: &Path, data_root: &Path) -> Result<(), Box<dyn Error>> {
    let config = RuntimePathsConfig {
        schema_version: RUNTIME_PATHS_CONFIG_SCHEMA_VERSION,
        data_root: data_root.to_string_lossy().into_owned(),
        source: Some("tauri-runtime-selection".to_owned()),
    };
    let config_text = serde_json::to_string_pretty(&config)?;
    write_runtime_paths_config_atomically(app_root, format!("{config_text}\n").as_bytes())
}

fn runtime_paths_config_path(app_root: &Path) -> PathBuf {
    app_root.join(RUNTIME_PATHS_CONFIG_FILE_NAME)
}

fn runtime_paths_config_backup_path(app_root: &Path) -> PathBuf {
    app_root.join(RUNTIME_PATHS_CONFIG_BACKUP_FILE_NAME)
}

fn write_runtime_paths_config_atomically(
    app_root: &Path,
    content: &[u8],
) -> Result<(), Box<dyn Error>> {
    ensure_directory(app_root)?;

    let config_path = runtime_paths_config_path(app_root);
    let backup_path = runtime_paths_config_backup_path(app_root);
    let temporary_path = runtime_paths_config_temporary_path(app_root);
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

fn runtime_paths_config_temporary_path(app_root: &Path) -> PathBuf {
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_nanos())
        .unwrap_or_default();
    app_root.join(format!(
        ".{RUNTIME_PATHS_CONFIG_FILE_NAME}.{}.{timestamp}.tmp",
        std::process::id()
    ))
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

    if is_system_drive_path(&selected) {
        return Err(format!(
            "选择的数据目录位于系统盘：'{}'。请重新启动并选择非系统盘目录，或通过 --data-root 明确指定企业策略允许的目录。",
            selected.display()
        )
        .into());
    }

    ensure_runtime_data_directories(&selected)?;
    Ok(selected)
}

fn ensure_runtime_data_directories(data_root: &Path) -> Result<(), Box<dyn Error>> {
    ensure_directory(data_root)?;
    ensure_directory(&data_root.join("Database"))?;
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

#[cfg(windows)]
fn is_system_drive_path(path: &Path) -> bool {
    let Some(system_drive) = env::var_os("SystemDrive") else {
        return false;
    };

    let system_drive = system_drive
        .to_string_lossy()
        .replace('/', "\\")
        .to_ascii_lowercase();
    if system_drive.trim().is_empty() {
        return false;
    }

    let mut normalized_drive = system_drive.trim_end_matches('\\').to_owned();
    if !normalized_drive.ends_with(':') {
        normalized_drive.push(':');
    }

    let full_path = normalize_path_for_prefix(path)
        .to_string_lossy()
        .replace('/', "\\")
        .to_ascii_lowercase();
    full_path == normalized_drive || full_path.starts_with(&format!("{normalized_drive}\\"))
}

#[cfg(not(windows))]
fn is_system_drive_path(_path: &Path) -> bool {
    false
}

fn normalize_path_for_prefix(path: &Path) -> PathBuf {
    if path.is_absolute() {
        return path.to_path_buf();
    }

    env::current_dir()
        .map(PathBuf::from)
        .unwrap_or_else(|_| PathBuf::from("."))
        .join(path)
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
        fs::create_dir_all(&app_root).unwrap();
        let data_root = absolute_test_data_root("configured-business-data");
        let config = RuntimePathsConfig {
            schema_version: RUNTIME_PATHS_CONFIG_SCHEMA_VERSION,
            data_root: data_root.to_string_lossy().into_owned(),
            source: Some("test".to_owned()),
        };
        fs::write(
            runtime_paths_config_path(&app_root),
            serde_json::to_string(&config).unwrap(),
        )
        .unwrap();

        assert_eq!(
            read_persisted_data_root(&app_root).unwrap(),
            Some(data_root)
        );
    }

    #[test]
    fn rejects_unsupported_runtime_paths_config_schema() {
        let app_root = fresh_test_dir("unsupported-runtime-paths-schema");
        fs::create_dir_all(&app_root).unwrap();
        fs::write(
            runtime_paths_config_path(&app_root),
            r#"{"schemaVersion":2,"dataRoot":"BusinessData"}"#,
        )
        .unwrap();

        let error = read_persisted_data_root(&app_root).unwrap_err().to_string();

        assert!(error.contains("Unsupported runtime paths config schema version 2"));
    }

    #[test]
    fn unsupported_schema_does_not_fall_back_to_older_backup() {
        let app_root = fresh_test_dir("unsupported-runtime-paths-schema-with-backup");
        fs::create_dir_all(&app_root).unwrap();
        fs::write(
            runtime_paths_config_path(&app_root),
            r#"{"schemaVersion":2,"dataRoot":"NewBusinessData"}"#,
        )
        .unwrap();
        fs::write(
            runtime_paths_config_backup_path(&app_root),
            r#"{"schemaVersion":1,"dataRoot":"OldBusinessData"}"#,
        )
        .unwrap();

        let error = read_persisted_data_root(&app_root).unwrap_err().to_string();

        assert!(error.contains("Unsupported runtime paths config schema version 2"));
    }

    #[test]
    fn resolves_persisted_relative_runtime_data_root_against_app_root() {
        let app_root = fresh_test_dir("relative-runtime-data-root");
        fs::create_dir_all(&app_root).unwrap();
        fs::write(
            runtime_paths_config_path(&app_root),
            r#"{"schemaVersion":1,"dataRoot":"BusinessData"}"#,
        )
        .unwrap();

        assert_eq!(
            read_persisted_data_root(&app_root).unwrap(),
            Some(app_root.join("BusinessData"))
        );
    }

    #[test]
    fn persists_runtime_data_root_as_valid_config() {
        let app_root = fresh_test_dir("persist-runtime-data-root");
        fs::create_dir_all(&app_root).unwrap();
        let data_root = app_root.join("BusinessData");

        persist_runtime_data_root(&app_root, &data_root).unwrap();

        assert_eq!(
            read_persisted_data_root(&app_root).unwrap(),
            Some(data_root)
        );
    }

    #[test]
    fn persist_keeps_previous_runtime_paths_config_as_backup() {
        let app_root = fresh_test_dir("backup-runtime-data-root");
        fs::create_dir_all(&app_root).unwrap();
        let first_data_root = app_root.join("FirstBusinessData");
        let second_data_root = app_root.join("SecondBusinessData");

        persist_runtime_data_root(&app_root, &first_data_root).unwrap();
        persist_runtime_data_root(&app_root, &second_data_root).unwrap();

        assert_eq!(
            read_persisted_data_root(&app_root).unwrap(),
            Some(second_data_root)
        );
        assert_eq!(
            read_data_root_from_config(&runtime_paths_config_backup_path(&app_root), &app_root)
                .unwrap(),
            first_data_root
        );
    }

    #[test]
    fn falls_back_to_backup_when_runtime_paths_config_is_corrupted() {
        let app_root = fresh_test_dir("recover-runtime-data-root");
        fs::create_dir_all(&app_root).unwrap();
        let first_data_root = app_root.join("FirstBusinessData");
        let second_data_root = app_root.join("SecondBusinessData");

        persist_runtime_data_root(&app_root, &first_data_root).unwrap();
        persist_runtime_data_root(&app_root, &second_data_root).unwrap();
        fs::write(runtime_paths_config_path(&app_root), "{broken-json").unwrap();

        assert_eq!(
            read_persisted_data_root(&app_root).unwrap(),
            Some(first_data_root)
        );
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
