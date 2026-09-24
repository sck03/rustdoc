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
    path::{Path, PathBuf},
};

pub(super) const KEY: &str = "Security/native-master-key.bin";
pub(super) const SQLITE: &str = "Database/exportdoc-native.db";
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
            if !path.is_file() || sources.len() >= 4095 {
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
    paths: &RuntimePaths,
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
        validate_entries(&entries, database)?;
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
    let _ = paths;
    result
}
fn validate_entries(entries: &[(String, Vec<u8>, String)], database: &str) -> Result<Manifest> {
    let manifest: Manifest = serde_json::from_slice(
        &entries
            .iter()
            .find(|(name, _, _)| name == "manifest.json")
            .ok_or_else(|| invalid("包缺少清单。"))?
            .1,
    )
    .map_err(|_| invalid("恢复包清单无效。"))?;
    if manifest.schema_version != 1
        || manifest.database_schema != export_doc_storage::SCHEMA_VERSION
        || manifest.database != database
    {
        return Err(invalid("恢复包的数据库类型或版本不匹配。"));
    }
    sealed::verify_manifest(entries, &manifest.files)?;
    if !entries
        .iter()
        .any(|(name, bytes, _)| name == database && !bytes.is_empty())
        || !entries
            .iter()
            .any(|(name, bytes, _)| name == KEY && bytes.len() == 32)
    {
        return Err(invalid("包缺少有效数据库或主密钥。"));
    }
    if entries.iter().any(|(name, _, _)| {
        name != database
            && name != KEY
            && name != "manifest.json"
            && !name.starts_with("Templates/")
    }) {
        return Err(invalid("包包含不支持的文件。"));
    }
    Ok(manifest)
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
    let manifest: Manifest = serde_json::from_slice(&fs::read(staging.join("manifest.json"))?)
        .map_err(|_| invalid("暂存清单无效。"))?;
    let mut entries = vec![(
        "manifest.json".into(),
        serde_json::to_vec(&manifest)?,
        String::new(),
    )];
    for file in &manifest.files {
        if file
            .name
            .split('/')
            .any(|part| !paths::valid_file_name(part))
        {
            return Err(invalid("暂存文件路径无效。"));
        }
        let path = staging.join(&file.name);
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if fs::metadata(&path)?.len() > sealed::MAX_PLAINTEXT_BYTES as u64 {
            return Err(invalid("暂存文件过大。"));
        }
        let bytes = fs::read(path)?;
        let digest = super::sha256_hex(&bytes);
        entries.push((file.name.clone(), bytes, digest));
    }
    validate_entries(&entries, database)?;
    Ok(Some(staging))
}
