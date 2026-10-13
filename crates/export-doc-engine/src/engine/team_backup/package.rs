//! One bounded, authenticated package contract for SQLite disaster recovery and PG migration.
use super::{
    error::{Result, error, invalid, unavailable},
    sealed,
};
use crate::paths::{self, RuntimePaths};
use fs2::FileExt;
use serde::{Deserialize, Serialize};
use std::{
    fs,
    io::Read,
    path::{Path, PathBuf},
};

pub(super) const KEY: &str = "Security/native-master-key.bin";
pub(super) const SQLITE: &str = crate::paths::SQLITE_DATABASE_RELATIVE_PATH;
pub(super) const POSTGRES: &str = "Database/postgresql-physical.dump";
#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(super) struct Manifest {
    schema_version: u32,
    database_schema: i64,
    database: String,
    pub files: Vec<sealed::ManifestFile>,
}
pub(super) fn lock(paths: &RuntimePaths) -> Result<fs::File> {
    let path = paths.data_root.join("Locks").join("backup-restore.lock");
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    fs::create_dir_all(path.parent().unwrap())?;
    let file = fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)?;
    file.try_lock_exclusive().map_err(|e| {
        if e.raw_os_error() == fs2::lock_contended_error().raw_os_error() {
            error(429, "备份或恢复正在进行，请稍后重试。")
        } else {
            unavailable(e.to_string())
        }
    })?;
    Ok(file)
}
pub(super) fn private_directory(path: &Path) -> Result<()> {
    crate::secrets::private_directory(path).map_err(unavailable)
}
pub(super) fn create(
    paths: &RuntimePaths,
    protector: &crate::secrets::Protector,
    database: &Path,
    entry: &str,
    working: &Path,
    magic: &[u8],
    password: &str,
) -> Result<Vec<u8>> {
    if std::env::var_os("EXPORTDOCMANAGER_MASTER_KEY").is_some() {
        return Err(invalid(
            "环境主密钥部署须由管理员单独迁移密钥，不能生成独立恢复包。",
        ));
    }
    let _ = protector
        .protect("backup-key-initialization", "initialize")
        .map_err(unavailable)?;
    let key = paths.data_root.join(KEY);
    paths::ensure_safe_absolute(&key).map_err(invalid)?;
    if fs::metadata(&key)?.len() != 32 {
        return Err(invalid("本地主密钥无效。"));
    }
    let mut sources = vec![
        (entry.to_owned(), database.to_path_buf()),
        (KEY.to_owned(), key),
    ];
    collect(
        &paths.data_root,
        &paths.data_root.join("Templates"),
        &mut sources,
    )?;
    let manifest = Manifest {
        schema_version: 1,
        database_schema: export_doc_storage::SCHEMA_VERSION,
        database: entry.into(),
        files: sources
            .iter()
            .map(|(name, source)| sealed::ManifestFile::from_path(name, source))
            .collect::<Result<_>>()?,
    };
    let manifest_path = working.join("manifest.json");
    sealed::atomic_write(&manifest_path, &serde_json::to_vec(&manifest)?)?;
    sources.push(("manifest.json".into(), manifest_path));
    let entries: Vec<_> = sources
        .iter()
        .map(|(name, source)| sealed::Entry { source, name })
        .collect();
    let payload = zeroize::Zeroizing::new(sealed::zip_payload(&entries)?);
    sealed::seal(magic, password, &payload)
}
fn collect(root: &Path, directory: &Path, sources: &mut Vec<(String, PathBuf)>) -> Result<()> {
    paths::ensure_safe_absolute(directory).map_err(invalid)?;
    if !directory.try_exists()? {
        return Ok(());
    }
    for entry in fs::read_dir(directory)? {
        crate::operation::check()?;
        let path = entry?.path();
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if path.is_dir() {
            collect(root, &path, sources)?;
        } else {
            if !path.is_file() || sources.len() >= sealed::MAX_FILES - 1 {
                return Err(invalid("模板文件数量或类型无效。"));
            }
            let name = path
                .strip_prefix(root)
                .map_err(|_| invalid("模板路径越界。"))?
                .components()
                .map(|p| p.as_os_str().to_string_lossy().into_owned())
                .collect::<Vec<_>>()
                .join("/");
            if name.split('/').any(|p| !paths::valid_file_name(p)) {
                return Err(invalid("模板文件名无效。"));
            }
            sources.push((name, path));
        }
    }
    Ok(())
}
pub(super) fn stage(
    marker: &Path,
    magic: &[u8],
    password: &str,
    content: &[u8],
    database: &str,
) -> Result<()> {
    if std::env::var_os("EXPORTDOCMANAGER_MASTER_KEY").is_some() {
        return Err(invalid(
            "当前部署使用环境主密钥，请先由管理员切换到恢复包的文件密钥模式。",
        ));
    }
    if marker.try_exists()? {
        return Err(error(409, "已有恢复等待处理，不能覆盖。"));
    }
    let root = marker.parent().ok_or_else(|| invalid("恢复目录无效。"))?;
    let staging = root.join(format!("pending-{}", paths::nonce().map_err(unavailable)?));
    private_directory(&staging)?;
    let result = (|| {
        let payload = zeroize::Zeroizing::new(sealed::open(magic, password, content)?);
        let entries = sealed::unpack(&payload, &staging)?;
        validate_entries(&read_manifest(&staging)?, &entries, database)?;
        if database == SQLITE {
            export_doc_storage::verify_sqlite_backup(&staging.join(SQLITE))?;
        }
        crate::operation::check()?;
        sealed::atomic_write(
            marker,
            &serde_json::to_vec(
                &serde_json::json!({"stagingDirectoryName":staging.file_name().unwrap().to_string_lossy(),"scheduledAtUtc":chrono::Utc::now().to_rfc3339()}),
            )?,
        )
    })();
    if result.is_err() {
        let _ = fs::remove_dir_all(&staging);
    }
    result
}
fn read_manifest(staging: &Path) -> Result<Manifest> {
    let path = staging.join("manifest.json");
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    let mut bytes = Vec::new();
    fs::File::open(path)?
        .take(sealed::MAX_PLAINTEXT_BYTES as u64 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > sealed::MAX_PLAINTEXT_BYTES {
        return Err(invalid("恢复包清单过大。"));
    }
    serde_json::from_slice(&bytes).map_err(|_| invalid("恢复包清单无效。"))
}
fn validate_manifest(manifest: &Manifest, database: &str) -> Result<()> {
    if manifest.schema_version != 1 || manifest.database != database {
        return Err(invalid("恢复包的数据库类型或版本不匹配。"));
    }
    export_doc_storage::validate_schema_version(manifest.database_schema)
        .map_err(|cause| invalid(cause.to_string()))?;
    if manifest.files.len() >= sealed::MAX_FILES {
        return Err(invalid("包内文件数量超过上限。"));
    }
    let mut total = 0_u64;
    for file in &manifest.files {
        if file
            .name
            .split('/')
            .any(|part| !paths::valid_file_name(part))
            || (file.name != database && file.name != KEY && !file.name.starts_with("Templates/"))
        {
            return Err(invalid("包包含不支持的文件或非法路径。"));
        }
        total = total
            .checked_add(file.size_bytes)
            .filter(|size| *size <= sealed::MAX_PLAINTEXT_BYTES as u64)
            .ok_or_else(|| invalid("包内容超过容量上限。"))?;
    }
    Ok(())
}
fn validate_entries(
    manifest: &Manifest,
    entries: &[sealed::ManifestFile],
    database: &str,
) -> Result<()> {
    validate_manifest(manifest, database)?;
    sealed::verify_manifest(entries, &manifest.files)?;
    if !entries
        .iter()
        .any(|entry| entry.name == database && entry.size_bytes > 0)
        || !entries
            .iter()
            .any(|entry| entry.name == KEY && entry.size_bytes == 32)
    {
        return Err(invalid("包缺少有效数据库或主密钥。"));
    }
    Ok(())
}
pub(super) fn staged(marker: &Path, database: &str) -> Result<Option<PathBuf>> {
    paths::ensure_safe_absolute(marker).map_err(invalid)?;
    if !marker.try_exists()? {
        return Ok(None);
    }
    let value: serde_json::Value =
        serde_json::from_slice(&fs::read(marker)?).map_err(|_| invalid("恢复标记无效。"))?;
    let name = value["stagingDirectoryName"]
        .as_str()
        .filter(|name| name.starts_with("pending-") && paths::valid_file_name(name))
        .ok_or_else(|| invalid("恢复暂存目录无效。"))?;
    let staging = marker.parent().unwrap().join(name);
    paths::ensure_safe_absolute(&staging).map_err(invalid)?;
    let manifest = read_manifest(&staging)?;
    validate_manifest(&manifest, database)?;
    let mut entries = Vec::with_capacity(manifest.files.len());
    let mut total = 0_u64;
    for file in &manifest.files {
        let path = staging.join(&file.name);
        let entry = sealed::ManifestFile::from_path(&file.name, &path)?;
        total += entry.size_bytes;
        if total > sealed::MAX_PLAINTEXT_BYTES as u64 {
            return Err(invalid("暂存文件过大。"));
        }
        entries.push(entry);
    }
    validate_entries(&manifest, &entries, database)?;
    Ok(Some(staging))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn recovery_manifests_accept_supported_versions_and_still_verify_payloads() {
        for database in [SQLITE, POSTGRES] {
            for (version, supported) in [
                (4, false),
                (5, true),
                (export_doc_storage::SCHEMA_VERSION, true),
                (export_doc_storage::SCHEMA_VERSION + 1, false),
            ] {
                let mut entries: Vec<_> = [(database, vec![1]), (KEY, vec![2; 32])]
                    .into_iter()
                    .map(|(name, bytes)| sealed::ManifestFile {
                        name: name.to_owned(),
                        size_bytes: bytes.len() as u64,
                        sha256: super::super::sha256_hex(&bytes),
                    })
                    .collect();
                let manifest = Manifest {
                    schema_version: 1,
                    database_schema: version,
                    database: database.into(),
                    files: entries.clone(),
                };
                assert_eq!(
                    validate_entries(&manifest, &entries, database).is_ok(),
                    supported
                );
                entries[0].sha256 = "tampered".into();
                assert!(validate_entries(&manifest, &entries, database).is_err());
            }
        }
    }

    #[test]
    fn staged_manifests_reject_aggregate_capacity_and_paths_before_reading_files() {
        let mut manifest = Manifest {
            schema_version: 1,
            database_schema: export_doc_storage::SCHEMA_VERSION,
            database: POSTGRES.into(),
            files: vec![
                sealed::ManifestFile {
                    name: POSTGRES.into(),
                    size_bytes: sealed::MAX_PLAINTEXT_BYTES as u64,
                    sha256: String::new(),
                },
                sealed::ManifestFile {
                    name: KEY.into(),
                    size_bytes: 32,
                    sha256: String::new(),
                },
            ],
        };
        assert!(validate_manifest(&manifest, POSTGRES).is_err());
        manifest.files[0].size_bytes = 1;
        assert!(validate_manifest(&manifest, POSTGRES).is_ok());
        manifest.files[0].name = "Templates/../outside".into();
        assert!(validate_manifest(&manifest, POSTGRES).is_err());
        manifest.files[0].name = "Security/unsupported.bin".into();
        assert!(validate_manifest(&manifest, POSTGRES).is_err());
    }
}
