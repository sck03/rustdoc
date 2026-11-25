//! Database-only recovery uses the same publication and replacement journals as full recovery.
use super::error::{Result, invalid, unavailable};
use super::{NativeService, auth, disaster, package, restore_files, store::Actor};
use crate::{
    generated_api::RESTORE_DATABASE_BACKUP,
    paths::{self, RuntimePaths},
};
use serde_json::Value;
use std::{fs, path::Path};

pub(in crate::engine) fn stage_sqlite(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize_operation(actor, RESTORE_DATABASE_BACKUP, &[])?;
    if service.provider()? != "SQLite" {
        return Err(invalid("数据库快照恢复只适用于 SQLite。"));
    }
    let name = body["backupFileName"].as_str().unwrap_or("");
    if !paths::valid_file_name(name) || Path::new(name).extension().is_none_or(|e| e != "sqlite3") {
        return Err(invalid("备份文件名无效。"));
    }
    if body["confirmationText"] != "RESTORE" {
        return Err(invalid("请输入 RESTORE 确认恢复数据库。"));
    }
    let source = service.paths.data_root.join("Backups").join(name);
    paths::ensure_safe_absolute(&source).map_err(invalid)?;
    let (store, paths, owner) = (service.store.clone(), service.paths.clone(), actor.id);
    service.jobs.start_backup(
        actor,
        RESTORE_DATABASE_BACKUP,
        "DatabaseRestore",
        "校验并暂存数据库恢复",
        move |_| {
            let actor = auth::current_actor(&store, owner)?;
            auth::authorize_operation(&actor, RESTORE_DATABASE_BACKUP, &[])?;
            let lock = package::lock(&paths)?;
            let prepared = package::prepare_snapshot(&disaster::marker(&paths)?, |staging| {
                export_doc_storage::verify_sqlite_backup(&source)?;
                let target = staging.join(package::SQLITE);
                restore_files::copy(&source, &target, &paths.disk_space)?;
                super::super::file_store::incremental::stage(&source, &target, &paths.disk_space)?;
                super::super::file_store::incremental::verify_staged(&target)?;
                Ok(package::SnapshotRestore::Sqlite {
                    sha256: super::super::backup_io::digest_file(&target)?.1,
                })
            })?;
            Ok(prepared.into_task(
                lock,
                "stage-database-restore",
                "数据库恢复已暂存，请退出并重新打开程序。",
            ))
        },
    )
}
pub(super) fn apply_sqlite(paths: &RuntimePaths, marker: &Path, sha256: &str) -> Result<()> {
    let staging = package::staging_directory(marker)?;
    let database = staging.join(package::SQLITE);
    if super::super::backup_io::digest_file(&database)?.1 != sha256 {
        return Err(invalid("暂存数据库已变化，恢复已拒绝。"));
    }
    let db = export_doc_storage::Connection::sqlite_read_only(&database)?;
    let files = super::super::file_store::FileStore::open(
        paths,
        !db.file_objects("", 1, false)?.is_empty(),
    )?;
    super::super::file_store::incremental::restore(&files, &database)?;
    files.verify_inventory(|cursor| Ok(db.file_objects(cursor, 200, false)?))?;
    drop(db);
    restore_files::install_database(paths, &staging)?;
    fs::remove_file(marker)?;
    Ok(())
}
/// Explicit offline recovery never needs to open the damaged database or authenticate against it.
pub fn import_sqlite(paths: &RuntimePaths, source: &Path, password: &str) -> Result<()> {
    paths::ensure_safe_absolute(source).map_err(invalid)?;
    super::validate_package_password(password)?;
    let _instance = super::super::store::sqlite_layout::prepare(paths)?;
    let lock = package::lock(paths)?;
    let marker = disaster::marker(paths)?;
    let prepared = package::prepare(
        &paths.disk_space,
        &marker,
        disaster::MAGIC,
        password,
        fs::File::open(source)?,
        package::SQLITE,
    )?;
    super::sealed::atomic_write(&marker, &prepared.marker_content(None)?)?;
    drop(prepared);
    drop(lock);
    disaster::apply_pending(paths)
        .map_err(|cause| unavailable(format!("离线恢复未完成，原数据和恢复现场已保留：{cause}")))
}

pub(in crate::engine) fn backup_sqlite(
    store: &super::Store,
    paths: &RuntimePaths,
    target: &Path,
) -> Result<()> {
    let mut reservation = paths
        .disk_space
        .at(target
            .parent()
            .ok_or_else(|| invalid("备份目录无效。"))?
            .to_path_buf())
        .reserve(0)?;
    let mut allocated = 0;
    store.connection()?.backup(target, &mut |bytes| {
        let written = fs::metadata(target)?.len();
        reservation.committed(written.saturating_sub(allocated));
        allocated = written;
        reservation
            .ensure(bytes.saturating_sub(written).saturating_add(1024 * 1024))
            .map_err(|error| export_doc_storage::Error::unavailable(error.message))
    })?;
    Ok(())
}

pub fn command(args: &[String]) -> Option<Result<()>> {
    let mode = args.first()?.as_str();
    if !matches!(mode, "--import-recovery" | "--restore-sqlite-pending") {
        return None;
    }
    Some((|| {
        let expected: &[&str] = if mode == "--import-recovery" {
            &["--app-root", "--data-root", "--package", "--password-file"]
        } else {
            &["--app-root", "--data-root"]
        };
        if args.len() != 1 + expected.len() * 2
            || args[1..]
                .chunks_exact(2)
                .zip(expected)
                .any(|(pair, key)| pair[0] != *key)
        {
            return Err(invalid(
                "离线恢复参数：--app-root <程序目录> --data-root <数据目录>；导入另加 --package <恢复包> --password-file <密码文件>。",
            ));
        }
        let paths =
            RuntimePaths::server(Path::new(&args[2]), Path::new(&args[4])).map_err(invalid)?;
        if mode == "--restore-sqlite-pending" {
            let _instance = super::super::store::sqlite_layout::prepare(&paths)?;
            return disaster::apply_pending(&paths);
        }
        use std::io::Read;
        let password_path = Path::new(&args[8]);
        paths::ensure_safe_absolute(password_path).map_err(invalid)?;
        let mut password = zeroize::Zeroizing::new(String::new());
        fs::File::open(password_path)?
            .take(4097)
            .read_to_string(&mut password)?;
        if password.len() > 4096 {
            return Err(invalid("密码文件过大。"));
        }
        import_sqlite(
            &paths,
            Path::new(&args[6]),
            password.trim_end_matches(['\r', '\n']),
        )
    })())
}
