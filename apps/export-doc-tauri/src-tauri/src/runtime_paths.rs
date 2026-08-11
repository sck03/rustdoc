use std::{
    env,
    error::Error,
    ffi::OsString,
    path::{Path, PathBuf},
};

use tauri::Manager;

use crate::runtime_data_root_migration::apply_pending_data_root_migration;
pub(crate) use crate::runtime_data_root_migration::schedule_data_root_migration;
use crate::runtime_data_root_storage::ensure_runtime_data_root_is_usable;
use crate::runtime_paths_config::{persist_runtime_data_root, read_persisted_data_root};
use crate::runtime_portable::resolve_portable_runtime_root;
use crate::runtime_sidecar_path::resolve_sidecar_path;

#[cfg(test)]
use crate::runtime_data_root_migration::pending_data_root_migration_path;
#[cfg(test)]
use crate::runtime_data_root_storage::validate_distinct_migration_roots;
#[cfg(test)]
use crate::runtime_paths_config::*;
#[cfg(test)]
use crate::runtime_portable::{
    is_portable_runtime, validate_portable_runtime_marker, PORTABLE_RUNTIME_MARKER_FILE_NAME,
    RUNTIME_LAYOUT_MANIFEST_FILE_NAME,
};
#[cfg(test)]
use crate::runtime_sidecar_path::sidecar_file_name;
#[cfg(test)]
use std::fs;

const RUNTIME_CONFIG_ROOT_ENVIRONMENT_VARIABLE: &str = "EXPORTDOCMANAGER_RUNTIME_CONFIG_ROOT";

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
    pub(crate) current_data_root: String,
    pub(crate) target_data_root: String,
    pub(crate) restart_required: bool,
    pub(crate) message: String,
}

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
    let portable_root =
        resolve_portable_runtime_root(&app_root, runtime_arg_value("--portable-root"))?;
    let portable = portable_root.is_some();
    let storage_root = portable_root.as_deref().unwrap_or(&app_root);
    let explicit_data_root =
        data_root_argument.or_else(|| env::var_os("EXPORTDOCMANAGER_DATA_ROOT").map(PathBuf::from));
    let runtime_config_root = if portable {
        storage_root.to_path_buf()
    } else {
        env::var_os(RUNTIME_CONFIG_ROOT_ENVIRONMENT_VARIABLE)
            .map(PathBuf::from)
            .unwrap_or(app.path().app_config_dir()?)
    };
    if !portable {
        apply_pending_data_root_migration(&runtime_config_root)?;
    }
    let data_root = resolve_data_root(
        storage_root,
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
    let selected = crate::runtime_data_root_storage::canonical_runtime_data_root(&selected, true)?;
    persist_runtime_data_root(runtime_config_root, &selected)?;
    Ok(selected)
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
    ensure_runtime_data_root_is_usable(&selected)?;
    Ok(selected)
}

pub(crate) fn ensure_runtime_data_directories(data_root: &Path) -> Result<(), Box<dyn Error>> {
    ensure_directory(data_root)?;
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
        ensure_directory(&data_root.join(directory_name))?;
    }
    Ok(())
}

pub(crate) fn current_exe_dir() -> Result<PathBuf, Box<dyn Error>> {
    let exe = env::current_exe()?;
    exe.parent()
        .map(Path::to_path_buf)
        .ok_or_else(|| "Current executable has no parent directory.".into())
}

pub(crate) fn ensure_directory(path: &Path) -> Result<(), Box<dyn Error>> {
    std::fs::create_dir_all(path)
        .map_err(|error| format!("Failed to create directory '{}': {error}", path.display()).into())
}

#[cfg(test)]
#[path = "runtime_paths_tests.rs"]
mod tests;
