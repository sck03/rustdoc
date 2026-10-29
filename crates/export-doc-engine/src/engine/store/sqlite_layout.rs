//! Accept only the current managed layout; reject experimental root databases untouched.
use crate::{
    engine::error::{Result, error, unavailable},
    paths::{RuntimePaths, ensure_safe_absolute},
};
use fs2::FileExt;
use std::{
    fs::{File, OpenOptions},
    path::Path,
};

const FILES: [&str; 4] = [
    "exportdoc-native.db",
    "exportdoc-native.db-wal",
    "exportdoc-native.db-shm",
    "exportdoc-native.db-journal",
];

fn directory(path: &Path) -> Result<()> {
    ensure_safe_absolute(path).map_err(unavailable)?;
    crate::secrets::private_directory(path).map_err(unavailable)?;
    ensure_safe_absolute(path).map_err(unavailable)?;
    Ok(())
}
fn has_database_files(root: &Path) -> Result<bool> {
    let mut found = false;
    for name in FILES {
        found |= root.join(name).try_exists()?;
    }
    Ok(found)
}
fn lock(path: &Path) -> Result<File> {
    ensure_safe_absolute(path).map_err(unavailable)?;
    let file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)?;
    file.try_lock_exclusive().map_err(|cause| {
        if cause.raw_os_error() == fs2::lock_contended_error().raw_os_error() {
            error(429, "此数据目录已由另一个原生程序打开。")
        } else {
            unavailable(format!("无法取得数据库实例锁：{cause}"))
        }
    })?;
    Ok(file)
}

pub(super) fn prepare(paths: &RuntimePaths) -> Result<File> {
    let locks = paths.data_root.join("Locks");
    directory(&locks)?;
    let instance = lock(&locks.join("native-instance.lock"))?;
    export_doc_storage::maintenance::ensure_data_root_available(&paths.data_root)?;
    let database = paths.sqlite_database_path();
    let database_root = database.parent().expect("managed database root");
    directory(database_root)?;
    for name in FILES {
        ensure_safe_absolute(&paths.data_root.join(name)).map_err(unavailable)?;
        ensure_safe_absolute(&database_root.join(name)).map_err(unavailable)?;
    }
    if has_database_files(&paths.data_root)? {
        return Err(unavailable(
            "不支持旧的根目录数据库布局；请显式选择新的数据目录，程序不会搬迁或覆盖原库。",
        ));
    }
    if !database.try_exists()? && has_database_files(database_root)? {
        return Err(unavailable(
            "存在数据库伴随文件但缺少主库，已停止新建数据库；请检查数据目录。",
        ));
    }
    Ok(instance)
}
