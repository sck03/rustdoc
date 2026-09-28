//! Relocate a supported database using a verified SQLite snapshot, including committed WAL data.
use crate::{
    engine::error::{Result, error, unavailable},
    paths::{RuntimePaths, ensure_safe_absolute, nonce},
};
use fs2::FileExt;
use std::{
    fs::{self, File, OpenOptions},
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
    let database = paths.sqlite_database_path();
    let database_root = database.parent().expect("managed database root");
    directory(database_root)?;
    for name in FILES {
        ensure_safe_absolute(&paths.data_root.join(name)).map_err(unavailable)?;
        ensure_safe_absolute(&database_root.join(name)).map_err(unavailable)?;
    }
    let original = paths.data_root.join(FILES[0]);
    if !original.try_exists()? {
        if has_database_files(&paths.data_root)?
            || (!database.try_exists()? && has_database_files(database_root)?)
        {
            return Err(unavailable(
                "存在数据库伴随文件但缺少主库，已停止新建数据库；请检查原数据目录。",
            ));
        }
        return Ok(instance);
    }
    if has_database_files(database_root)? {
        return Err(unavailable(
            "根目录和 Database 同时存在数据库文件，不能自动选择或覆盖；请核对数据及 Backups/DatabaseLayout 备份后重试。",
        ));
    }
    let old_lock_path = paths.data_root.join("native-instance.lock");
    let old_lock = lock(&old_lock_path)?;
    export_doc_storage::verify_sqlite_backup(&original)?;
    let backup = paths
        .data_root
        .join("Backups")
        .join("DatabaseLayout")
        .join(nonce().map_err(unavailable)?);
    directory(&backup)?;
    let staged = backup.join("relocated.db");
    export_doc_storage::prepare_sqlite_restore(&original, &staged)?;
    export_doc_storage::verify_sqlite_backup(&staged)?;
    // Publish only a verified snapshot. Interrupted archiving leaves both copies and fails closed.
    fs::rename(&staged, &database)?;
    for name in FILES {
        let path = paths.data_root.join(name);
        if path.try_exists()? {
            fs::rename(path, backup.join(name))?;
        }
    }
    drop(old_lock);
    fs::rename(old_lock_path, backup.join("native-instance.lock"))?;
    Ok(instance)
}
