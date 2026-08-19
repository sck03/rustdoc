use std::{
    error::Error,
    fs::{self, OpenOptions},
    io::{Read, Seek, Write},
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

use crate::runtime_data_root_network::reject_network_data_root;
use crate::runtime_path_identity::{is_path_within, same_path};
use crate::runtime_paths::{
    ensure_directory, ensure_runtime_data_directories, RUNTIME_DATA_DIRECTORIES,
};

pub(crate) fn ensure_runtime_data_root_is_usable(data_root: &Path) -> Result<(), Box<dyn Error>> {
    validate_migration_root_shape(data_root, "dataRoot")?;
    reject_network_data_root(data_root)?;
    reject_link_like_path(data_root)?;
    ensure_runtime_data_directories(data_root)?;
    reject_network_data_root(data_root)?;
    reject_link_like_path(data_root)?;
    for directory_name in RUNTIME_DATA_DIRECTORIES {
        reject_link_like_path(&data_root.join(directory_name))?;
    }
    probe_writable_directory(data_root)
}

pub(crate) fn canonical_runtime_data_root(
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
    reject_network_data_root(&canonical)?;
    if require_expected_layout {
        ensure_expected_runtime_data_root(&canonical)?;
    }
    Ok(canonical)
}

pub(crate) fn validate_migration_root_shape(
    path: &Path,
    field_name: &str,
) -> Result<(), Box<dyn Error>> {
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

pub(crate) fn validate_distinct_migration_roots(
    source_root: &Path,
    target_root: &Path,
) -> Result<(), Box<dyn Error>> {
    if same_path(source_root, target_root) {
        return Err("新数据目录不能与当前数据目录相同。".into());
    }
    if is_path_within(target_root, source_root) || is_path_within(source_root, target_root) {
        return Err("新旧数据目录不能互相包含，请选择彼此独立的目录。".into());
    }
    Ok(())
}

pub(crate) fn ensure_expected_runtime_data_root(data_root: &Path) -> Result<(), Box<dyn Error>> {
    for directory_name in RUNTIME_DATA_DIRECTORIES {
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

pub(crate) fn ensure_directory_is_empty(path: &Path) -> Result<(), Box<dyn Error>> {
    if fs::read_dir(path)?.next().is_some() {
        return Err(format!(
            "所选新数据目录 '{}' 不是空目录。为防止混合或覆盖数据，请选择一个空目录。",
            path.display()
        )
        .into());
    }
    Ok(())
}

pub(crate) fn probe_writable_directory(path: &Path) -> Result<(), Box<dyn Error>> {
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
            .map_err(|error| {
                format!(
                    "无法写入数据目录 '{}'。请确认当前账号拥有该目录的读写和删除权限：{error}",
                    path.display()
                )
            })?;
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

pub(crate) fn directory_tree_size(root: &Path) -> Result<u64, Box<dyn Error>> {
    let mut total_bytes = 0_u64;
    collect_directory_tree_size(root, &mut total_bytes)?;
    Ok(total_bytes)
}

fn collect_directory_tree_size(
    directory: &Path,
    total_bytes: &mut u64,
) -> Result<(), Box<dyn Error>> {
    for entry in fs::read_dir(directory).map_err(|error| {
        format!(
            "无法读取待迁移数据目录 '{}'。请确认当前账号拥有读取权限：{error}",
            directory.display()
        )
    })? {
        let entry = entry?;
        let entry_path = entry.path();
        let metadata = fs::symlink_metadata(&entry_path)?;
        if metadata.file_type().is_symlink() || is_windows_reparse_point(&metadata) {
            return Err(format!(
                "数据目录迁移不支持符号链接、目录联接或重解析点：'{}'.",
                entry_path.display()
            )
            .into());
        }
        if metadata.is_dir() {
            collect_directory_tree_size(&entry_path, total_bytes)?;
        } else if metadata.is_file() {
            *total_bytes = total_bytes
                .checked_add(metadata.len())
                .ok_or("待迁移数据总大小超过程序可处理的范围。")?;
        } else {
            return Err(format!(
                "数据目录包含不支持的文件系统对象：'{}'.",
                entry_path.display()
            )
            .into());
        }
    }
    Ok(())
}

pub(crate) fn reject_link_like_path(path: &Path) -> Result<(), Box<dyn Error>> {
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

pub(crate) fn copy_directory_tree(source: &Path, target: &Path) -> Result<(), Box<dyn Error>> {
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

pub(crate) fn write_new_file_atomically(
    target_path: &Path,
    content: &[u8],
) -> Result<(), Box<dyn Error>> {
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

pub(crate) fn sync_directory(path: &Path) {
    if let Ok(directory) = fs::File::open(path) {
        let _ = directory.sync_all();
    }
}

pub(crate) fn unix_seconds_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|value| value.as_secs())
        .unwrap_or_default()
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
