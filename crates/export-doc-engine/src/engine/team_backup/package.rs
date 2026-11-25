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
pub(super) const OBJECTS: &str = "FileObjects.pack";
#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(super) struct Manifest {
    schema_version: u32,
    database_schema: i64,
    database: String,
    pub files: Vec<sealed::ManifestFile>,
}
pub(in crate::engine) fn lock(paths: &RuntimePaths) -> Result<fs::File> {
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
    super::scratch::recover(paths)?;
    Ok(file)
}
pub(super) fn private_directory(path: &Path) -> Result<()> {
    crate::secrets::private_directory(path).map_err(unavailable)
}
pub(super) fn create(
    store: &super::super::store::Store,
    paths: &RuntimePaths,
    protector: &crate::secrets::Protector,
    database: &Path,
    entry: &str,
    working: &Path,
    magic: &[u8],
    password: &str,
) -> Result<PathBuf> {
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
    let objects = working.join(OBJECTS);
    if entry == SQLITE {
        let snapshot = export_doc_storage::Connection::sqlite_read_only(database)?;
        super::super::file_store::export_snapshot(store, &objects, &snapshot)?;
    } else {
        super::super::file_store::export_archive(store, &objects)?;
    }
    sources.push((OBJECTS.into(), objects));
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
    let manifest_bytes = serde_json::to_vec(&manifest)?;
    if manifest_bytes.len() > MAX_MANIFEST_BYTES {
        return Err(invalid("恢复包清单过大。"));
    }
    sealed::atomic_write(&manifest_path, &manifest_bytes)?;
    sources.push(("manifest.json".into(), manifest_path));
    let entries: Vec<_> = sources
        .iter()
        .map(|(name, source)| sealed::Entry { source, name })
        .collect();
    let payload_path = working.join("payload.zip");
    let file = fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(&payload_path)?;
    sealed::zip_payload(
        &entries,
        paths.disk_space.at(working.to_path_buf()).writer(file)?,
    )?;
    fs::remove_file(working.join(OBJECTS))?;
    let target = working.join("sealed-package.bin");
    let mut output = fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(&target)?;
    sealed::seal_to(
        magic,
        password,
        fs::File::open(&payload_path)?,
        &mut paths
            .disk_space
            .at(working.to_path_buf())
            .writer(&mut output)?,
    )?;
    output.sync_all()?;
    fs::remove_file(payload_path)?;
    Ok(target)
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
#[cfg(feature = "postgres")]
pub(super) fn stage(
    paths: &RuntimePaths,
    marker: &Path,
    magic: &[u8],
    password: &str,
    content: impl Read,
    database: &str,
) -> Result<()> {
    let prepared = prepare(
        &paths.disk_space,
        marker,
        magic,
        password,
        content,
        database,
    )?;
    if database == POSTGRES {
        prepared.verify_postgres(paths)?;
    }
    sealed::atomic_write(marker, &prepared.marker_content(None)?)
}
#[derive(Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub(super) enum SnapshotRestore {
    Sqlite {
        sha256: String,
    },
    Postgres {
        file_name: Option<String>,
        sha256: String,
    },
}
pub(in crate::engine) struct PreparedRestore {
    staging: PathBuf,
    snapshot: Option<SnapshotRestore>,
    pub(in crate::engine) marker: PathBuf,
}
impl PreparedRestore {
    pub(super) fn verify_postgres(&self, paths: &RuntimePaths) -> Result<()> {
        super::postgres::verify_dump(paths, &self.staging.join(POSTGRES))
    }
    pub(super) fn into_task(
        self,
        lock: fs::File,
        action: &'static str,
        detail: &str,
    ) -> super::tasks::TaskOutput {
        let note = detail.to_owned();
        super::tasks::TaskOutput {
            file: None,
            destination: None,
            directory: None,
            managed_file: None,
            archive: None,
            detail: note.clone(),
            publication: Some(
                super::tasks::PublicationCheck::new(move |tx, actor| {
                    super::audit_in(tx, actor, action, &note)
                })
                .with_restore(self)
                .with_file_lock(lock),
            ),
        }
    }
    pub(in crate::engine) fn marker_content(&self, job: Option<&str>) -> Result<Vec<u8>> {
        serde_json::to_vec(&serde_json::json!({"stagingDirectoryName":self.staging.file_name().unwrap().to_string_lossy(),
            "scheduledAtUtc":chrono::Utc::now().to_rfc3339(),"jobId":job,"snapshot":self.snapshot})).map_err(Into::into)
    }
}
pub(super) fn prepare_snapshot(
    marker: &Path,
    prepare: impl FnOnce(&Path) -> Result<SnapshotRestore>,
) -> Result<PreparedRestore> {
    if marker.try_exists()? {
        return Err(error(409, "已有恢复等待处理，不能覆盖。"));
    }
    let root = marker.parent().ok_or_else(|| invalid("恢复目录无效。"))?;
    let staging = super::scratch::create(root, "pending-")?;
    let mut prepared = PreparedRestore {
        staging,
        marker: marker.into(),
        snapshot: None,
    };
    prepared.snapshot = Some(prepare(&prepared.staging)?);
    Ok(prepared)
}
pub(super) fn snapshot(marker: &Path) -> Result<Option<SnapshotRestore>> {
    serde_json::from_value(super::scratch::read_json(marker)?["snapshot"].clone())
        .map_err(|_| invalid("数据库恢复标记无效。"))
}
pub(super) fn staging_directory(marker: &Path) -> Result<PathBuf> {
    paths::ensure_safe_absolute(marker).map_err(invalid)?;
    let value = super::scratch::read_json(marker)?;
    let name = value["stagingDirectoryName"]
        .as_str()
        .filter(|name| name.starts_with("pending-") && paths::valid_file_name(name))
        .ok_or_else(|| invalid("恢复暂存目录无效。"))?;
    let staging = marker
        .parent()
        .ok_or_else(|| invalid("恢复目录无效。"))?
        .join(name);
    paths::ensure_safe_absolute(&staging).map_err(invalid)?;
    Ok(staging)
}
impl Drop for PreparedRestore {
    fn drop(&mut self) {
        if self.marker.try_exists().is_ok_and(|exists| !exists) {
            let _ = fs::remove_dir_all(&self.staging);
        }
    }
}
pub(super) fn prepare(
    space: &std::sync::Arc<crate::temporary_space::Budget>,
    marker: &Path,
    magic: &[u8],
    password: &str,
    content: impl Read,
    database: &str,
) -> Result<PreparedRestore> {
    if std::env::var_os("EXPORTDOCMANAGER_MASTER_KEY").is_some() {
        return Err(invalid(
            "当前部署使用环境主密钥，请先由管理员切换到恢复包的文件密钥模式。",
        ));
    }
    if marker.try_exists()? {
        return Err(error(409, "已有恢复等待处理，不能覆盖。"));
    }
    let root = marker.parent().ok_or_else(|| invalid("恢复目录无效。"))?;
    let staging = super::scratch::create(root, "pending-")?;
    let result = (|| {
        let archive_path = staging.join(".payload.zip");
        let mut archive = fs::OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&archive_path)?;
        sealed::open_to(
            magic,
            password,
            content,
            &mut space.at(staging.clone()).writer(&mut archive)?,
        )?;
        archive.sync_all()?;
        drop(archive);
        let entries = sealed::unpack(fs::File::open(&archive_path)?, &staging, space)?;
        fs::remove_file(archive_path)?;
        validate_entries(&read_manifest(&staging)?, &entries, database)?;
        super::super::file_store::restore_archive(&staging.join(OBJECTS), &staging, space)?;
        if database == SQLITE {
            export_doc_storage::verify_sqlite_backup(&staging.join(SQLITE))?;
            let db = export_doc_storage::Connection::sqlite(&staging.join(SQLITE))?;
            super::super::file_store::verify_local_inventory(&staging, |cursor| {
                Ok(db.file_objects(cursor, 200, false)?)
            })?;
        }
        crate::operation::check()?;
        Ok(PreparedRestore {
            staging: staging.clone(),
            snapshot: None,
            marker: marker.to_path_buf(),
        })
    })();
    if result.is_err() {
        let _ = fs::remove_dir_all(&staging);
    }
    result
}
/// Called under the package/instance lock before any database or file replacement.
pub(super) fn confirm_task_marker(
    cache: &Path,
    marker: &Path,
    succeeded: impl FnOnce(&str) -> Result<bool>,
) -> Result<bool> {
    if !marker.try_exists()? {
        return Ok(false);
    }
    let mut value = super::scratch::read_json(marker)?;
    let Some(job) = value["jobId"].as_str() else {
        return Ok(true);
    };
    if job.len() != 32 || !job.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err(invalid("恢复任务标识无效。"));
    }
    if value["taskCommitted"] == true {
        super::tasks::resolve_control(cache, marker, job, true)?;
        return Ok(true);
    }
    let committed = succeeded(job)?;
    super::tasks::resolve_control(cache, marker, job, committed)?;
    if !committed {
        if marker.try_exists()? {
            fs::remove_file(marker)?;
        }
        return Ok(false);
    }
    value["taskCommitted"] = serde_json::json!(true);
    sealed::atomic_write(marker, &serde_json::to_vec(&value)?)?;
    Ok(true)
}
/// A durable decision survives task-history pruning before the next restart.
pub(in crate::engine) fn commit_task_marker(marker: &Path, job: &str) -> Result<()> {
    let mut value = super::scratch::read_json(marker)?;
    if value["jobId"] != job {
        return Err(unavailable("恢复标记与已提交任务不匹配。"));
    }
    if value["taskCommitted"] != true {
        value["taskCommitted"] = serde_json::json!(true);
        sealed::atomic_write(marker, &serde_json::to_vec(&value)?)?;
    }
    Ok(())
}
const MAX_MANIFEST_BYTES: usize = 4 * 1024 * 1024;
fn read_manifest(staging: &Path) -> Result<Manifest> {
    let path = staging.join("manifest.json");
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    let mut bytes = Vec::new();
    fs::File::open(path)?
        .take(MAX_MANIFEST_BYTES as u64 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > MAX_MANIFEST_BYTES {
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
            || (file.name != database
                && file.name != KEY
                && file.name != OBJECTS
                && !file.name.starts_with("Templates/"))
        {
            return Err(invalid("包包含不支持的文件或非法路径。"));
        }
        total = total
            .checked_add(file.size_bytes)
            .filter(|size| *size <= sealed::MAX_EXPANDED_BYTES as u64)
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
        || !entries
            .iter()
            .any(|entry| entry.name == OBJECTS && entry.size_bytes > 0)
    {
        return Err(invalid("包缺少有效数据库、文件对象或主密钥。"));
    }
    Ok(())
}
pub(super) fn staged(
    marker: &Path,
    database: &str,
    space: &std::sync::Arc<crate::temporary_space::Budget>,
) -> Result<Option<PathBuf>> {
    paths::ensure_safe_absolute(marker).map_err(invalid)?;
    if !marker.try_exists()? {
        return Ok(None);
    }
    let staging = staging_directory(marker)?;
    let manifest = read_manifest(&staging)?;
    validate_manifest(&manifest, database)?;
    let mut entries = Vec::with_capacity(manifest.files.len());
    let mut total = 0_u64;
    for file in &manifest.files {
        let path = staging.join(&file.name);
        let entry = sealed::ManifestFile::from_path(&file.name, &path)?;
        total = total
            .checked_add(entry.size_bytes)
            .ok_or_else(|| invalid("暂存文件大小超出可表示范围。"))?;
        entries.push(entry);
    }
    validate_entries(&manifest, &entries, database)?;
    super::super::file_store::verify_archive(&staging.join(OBJECTS), &staging, space)?;
    if database == SQLITE {
        export_doc_storage::verify_sqlite_backup(&staging.join(SQLITE))?;
        let db = export_doc_storage::Connection::sqlite(&staging.join(SQLITE))?;
        super::super::file_store::verify_local_inventory(&staging, |cursor| {
            Ok(db.file_objects(cursor, 200, false)?)
        })?;
    }
    Ok(Some(staging))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn restore_publication_obeys_cancellation_and_final_authorization() {
        use crate::{
            engine::test_support::{ScratchDirectory, administrator},
            generated_api::*,
        };
        use std::{
            sync::mpsc,
            time::{Duration, Instant},
        };
        for scenario in ["success", "cancel", "denied"] {
            let root = ScratchDirectory::new();
            let service = root.service();
            let paths = root.paths();
            let directory = paths.data_root.join("DisasterRecovery");
            fs::create_dir_all(&directory).unwrap();
            let guard = lock(&paths).unwrap();
            let staging = super::super::scratch::create(&directory, "pending-").unwrap();
            let marker = directory.join(".pending-restore.json");
            let prepared = PreparedRestore {
                staging: staging.clone(),
                snapshot: None,
                marker: marker.clone(),
            };
            assert!(!marker.exists());
            let (release, wait) = mpsc::channel();
            let job = service
                .jobs
                .start(
                    &administrator(),
                    GET_JOB,
                    "Restore",
                    "恢复提交",
                    move |_| {
                        wait.recv_timeout(Duration::from_secs(5)).unwrap();
                        Ok(prepared.into_task(guard, "restore-test", "恢复提交"))
                    },
                )
                .unwrap();
            let id = job["jobId"].as_str().unwrap();
            if scenario == "cancel" {
                service
                    .jobs
                    .operation(CANCEL_JOB, &administrator(), id)
                    .unwrap();
            }
            if scenario == "denied" {
                service
                    .store
                    .transaction(|tx| {
                        let actor = administrator();
                        let mut user = crate::engine::store::get(tx, "users", actor.id)?;
                        user["isActive"] = serde_json::json!(false);
                        crate::engine::store::save(
                            tx,
                            "users",
                            actor.id,
                            user,
                            Some(actor.name.clone()),
                            &actor,
                            "disable",
                        )?;
                        Ok(())
                    })
                    .unwrap();
            }
            release.send(()).unwrap();
            let deadline = Instant::now() + Duration::from_secs(5);
            loop {
                let job = service.jobs.get(&administrator(), id).unwrap();
                if job["status"] != "Running" {
                    assert_eq!(
                        job["status"],
                        match scenario {
                            "success" => "Succeeded",
                            "cancel" => "Canceled",
                            _ => "Failed",
                        }
                    );
                    break;
                }
                assert!(Instant::now() < deadline);
                std::thread::sleep(Duration::from_millis(10));
            }
            assert_eq!(marker.exists(), scenario == "success");
            assert_eq!(staging.exists(), scenario == "success");
            if scenario == "success" {
                service
                    .jobs
                    .operation(DELETE_JOB, &administrator(), id)
                    .unwrap();
                assert!(confirm_task_marker(&paths.cache_root, &marker, |_| Ok(true)).unwrap());
                assert!(
                    confirm_task_marker(&paths.cache_root, &marker, |_| panic!(
                        "must not query replaced DB"
                    ))
                    .unwrap()
                );
            }
            service.close().unwrap();
        }
    }

    #[test]
    fn failed_task_marker_is_removed_and_unknown_commit_stops_recovery() {
        let root = crate::engine::test_support::ScratchDirectory::new();
        let marker = root.paths().data_root.join("marker.json");
        let bytes = serde_json::to_vec(&serde_json::json!({"jobId":"a".repeat(32)})).unwrap();
        fs::write(&marker, &bytes).unwrap();
        assert!(
            confirm_task_marker(&root.paths().cache_root, &marker, |_| Err(unavailable(
                "DB offline"
            )))
            .is_err()
        );
        assert_eq!(fs::read(&marker).unwrap(), bytes);
        assert!(!confirm_task_marker(&root.paths().cache_root, &marker, |_| Ok(false)).unwrap());
        assert!(!marker.exists());
    }

    #[test]
    fn recovery_manifests_accept_supported_versions_and_still_verify_payloads() {
        for database in [SQLITE, POSTGRES] {
            for (version, supported) in [
                (4, false),
                (5, false),
                (export_doc_storage::SCHEMA_VERSION - 1, false),
                (export_doc_storage::SCHEMA_VERSION, true),
                (export_doc_storage::SCHEMA_VERSION + 1, false),
            ] {
                let mut entries: Vec<_> =
                    [(database, vec![1]), (KEY, vec![2; 32]), (OBJECTS, vec![3])]
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
                    size_bytes: sealed::MAX_EXPANDED_BYTES as u64,
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
