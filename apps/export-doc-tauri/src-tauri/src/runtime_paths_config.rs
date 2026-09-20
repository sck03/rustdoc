use std::{
    error::Error,
    fmt,
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

use crate::runtime_paths::ensure_directory;

pub(crate) const RUNTIME_PATHS_CONFIG_FILE_NAME: &str = "runtime-paths.json";
pub(crate) const RUNTIME_PATHS_CONFIG_BACKUP_FILE_NAME: &str = "runtime-paths.json.bak";
pub(crate) const RUNTIME_PATHS_CONFIG_SCHEMA_VERSION: u32 = 1;

#[derive(serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct RuntimePathsConfig {
    pub(crate) schema_version: u32,
    pub(crate) data_root: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub(crate) source: Option<String>,
}

#[derive(Debug)]
pub(crate) struct RuntimePathsConfigReadError {
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

pub(crate) fn read_persisted_data_root(
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

pub(crate) fn read_data_root_from_config(
    config_path: &Path,
    app_root: &Path,
) -> Result<PathBuf, RuntimePathsConfigReadError> {
    let config_text = fs::read_to_string(config_path).map_err(|error| {
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

pub(crate) fn persist_runtime_data_root(
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

pub(crate) fn runtime_paths_config_path(runtime_config_root: &Path) -> PathBuf {
    runtime_config_root.join(RUNTIME_PATHS_CONFIG_FILE_NAME)
}

pub(crate) fn runtime_paths_config_backup_path(runtime_config_root: &Path) -> PathBuf {
    runtime_config_root.join(RUNTIME_PATHS_CONFIG_BACKUP_FILE_NAME)
}

pub(crate) fn write_runtime_paths_config_atomically(
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

pub(crate) fn runtime_paths_config_temporary_path(runtime_config_root: &Path) -> PathBuf {
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_nanos())
        .unwrap_or_default();
    runtime_config_root.join(format!(
        ".{RUNTIME_PATHS_CONFIG_FILE_NAME}.{}.{timestamp}.tmp",
        std::process::id()
    ))
}
