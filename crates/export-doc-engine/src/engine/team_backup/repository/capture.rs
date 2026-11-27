use super::super::{capture::Inventory, package};
use super::*;
use crate::{engine::store::Store, secrets::Protector};

pub(in crate::engine::team_backup) fn create(
    repository: &Repository,
    store: &Store,
    paths: &RuntimePaths,
    protector: &Protector,
    database: &Path,
    provider: &str,
    id: &str,
) -> Result<Commit> {
    if std::env::var_os("EXPORTDOCMANAGER_MASTER_KEY").is_some() {
        return Err(invalid(
            "环境主密钥须先由部署管理员配置独立密钥灾备，不能声明此仓库可独立恢复。",
        ));
    }
    protector
        .protect("backup-key-initialization", "initialize")
        .map_err(unavailable)?;
    let storage = store.files.configuration()?;
    store.files.check_identity(&storage)?;
    let database_name = if provider == "SQLite" {
        package::SQLITE
    } else {
        package::POSTGRES
    };
    let mut writer = repository.writer(id, provider, serde_json::to_value(&storage)?)?;
    source(&mut writer, "Database", database_name, database)?;
    let key = paths.data_root.join(package::KEY);
    if std::fs::metadata(&key)?.len() != 32 {
        return Err(invalid("应用主密钥缺失或长度无效，不能创建独立恢复点。"));
    }
    source(&mut writer, "MasterKey", package::KEY, &key)?;
    let mut templates = Vec::new();
    package::collect(
        &paths.data_root,
        &paths.data_root.join("Templates"),
        &mut templates,
    )?;
    for (name, path) in templates {
        source(&mut writer, "Template", &name, &path)?;
    }
    if provider == "SQLite" {
        let snapshot = export_doc_storage::Connection::sqlite_read_only(database)?;
        let mut cursor = String::new();
        loop {
            let rows = snapshot.file_objects(&cursor, 100, false)?;
            if rows.is_empty() {
                break;
            }
            for object in rows {
                writer.file(
                    "Attachment",
                    &if object.archive_identity.is_empty() {
                        object.digest.clone()
                    } else {
                        format!("{}/{}", object.archive_identity, object.digest)
                    },
                    object.size,
                    &object.digest,
                    |output| store.files.stream_object(&object, output),
                )?;
                cursor = object.digest;
            }
        }
    } else {
        let mut inventory = Inventory::open(database)?;
        loop {
            let rows = inventory.page()?;
            if rows.is_empty() {
                break;
            }
            for object in rows {
                writer.file(
                    "Attachment",
                    &if object.archive_identity.is_empty() {
                        object.digest.clone()
                    } else {
                        format!("{}/{}", object.archive_identity, object.digest)
                    },
                    object.size,
                    &object.digest,
                    |output| store.files.stream_object(&object, output),
                )?;
            }
        }
    }
    let latest = store.files.configuration()?;
    if latest.identity != storage.identity || latest.revision != storage.revision {
        return Err(invalid("备份期间生产文件库已切换，请重新捕获恢复点。"));
    }
    store.files.check_identity(&latest)?;
    writer.finish()
}
fn source(writer: &mut PointWriter<'_>, role: &str, name: &str, path: &Path) -> Result<()> {
    paths::ensure_safe_absolute(path).map_err(invalid)?;
    let (size, digest) = crate::engine::backup_io::digest_file(path)?;
    writer.file(role, name, size, &digest, |output| {
        crate::engine::backup_io::copy_chunks(
            &mut std::fs::File::open(path)?,
            output,
            size,
            |_| {},
        )?;
        Ok(())
    })
}
