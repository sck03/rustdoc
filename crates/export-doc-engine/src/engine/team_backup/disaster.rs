//! SQLite recovery runs under the instance lock before opening the database.
use super::{
    DISASTER_STORAGE_POLICY, NativeService, auth,
    error::{Result, error, invalid, unavailable},
    package, restore_files, sealed,
    store::Actor,
    tasks::TaskOutput,
};
use crate::{contracts, generated_api::*, paths::RuntimePaths};
use serde_json::{Value, json};
use std::{
    fs,
    path::{Path, PathBuf},
};

fn root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::disaster_root(paths)
}
pub(super) fn marker(paths: &RuntimePaths) -> Result<PathBuf> {
    Ok(root(paths)?.join(".pending-restore.json"))
}
pub(super) fn status(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let supported = service.provider()? == "SQLite";
    let pending = marker(&service.paths)?.try_exists()?;
    let mut result = contracts::initial(contracts::schema("ApiDisasterRecoveryStatusResponse"));
    result["supported"] = json!(supported);
    result["usesSqlite"] = json!(supported);
    result["pendingRestore"] = json!(pending);
    result["recoveryRoot"] = json!(root(&service.paths)?);
    result["message"] = json!(if pending {
        "恢复已暂存，请退出并重新打开桌面程序。"
    } else if supported {
        "便携恢复点包含数据库、主密钥、模板和全部文件。恢复时按有界窗口读取；导出文件本身需要相应目标空间。"
    } else {
        "团队版请使用服务器迁移包。"
    });
    result["storagePolicy"] = json!(DISASTER_STORAGE_POLICY);
    Ok(result)
}
pub(super) fn create_package(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    if service.provider()? != "SQLite" {
        return Err(invalid("灾备包只适用于 SQLite 单机版。"));
    }
    let password = zeroize::Zeroizing::new(body["password"].as_str().unwrap_or("").to_owned());
    super::validate_package_password(&password)?;
    let (store, paths, protector, id) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
    );
    service.jobs.start_backup(
        actor,
        CREATE_DISASTER_RECOVERY_PACKAGE,
        "DisasterRecoveryPackage",
        "创建加密灾备包",
        move |_| {
            let actor = auth::current_actor(&store, id)?;
            auth::authorize_operation(&actor, CREATE_DISASTER_RECOVERY_PACKAGE, &[])?;
            let _gate = package::lock(&paths)?;
            let _templates = super::super::report_template_files::storage_lock(&paths)?;
            if marker(&paths)?.try_exists()? {
                return Err(error(409, "已有恢复等待重启。"));
            }
            let working = sealed::working_directory(&root(&paths)?)?;
            package::private_directory(&working)?;
            let result = (|| {
                let snapshot = working.join("snapshot.sqlite3");
                super::snapshot::backup_sqlite(&store, &paths, &snapshot)?;
                let target = super::portable::create(
                    &store, &paths, &protector, &snapshot, "SQLite", &password,
                )?;
                crate::operation::check()?;
                super::audit(
                    &store,
                    &actor,
                    "create-disaster-recovery-package",
                    "加密灾备包已创建",
                )?;
                TaskOutput::managed_file(
                    target,
                    "灾备包已创建。请下载到离线介质，并单独保管密码。".into(),
                )
            })();
            let _ = fs::remove_dir_all(&working);
            result
        },
    )
}
pub(super) fn restore_package(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    if service.provider()? != "SQLite" {
        return Err(invalid("灾备恢复只适用于 SQLite 单机版。"));
    }
    if super::records_text(body, "confirmationText") != "RECOVER" {
        return Err(invalid("请输入 RECOVER 确认恢复。"));
    }
    let password = zeroize::Zeroizing::new(body["password"].as_str().unwrap_or("").to_owned());
    super::validate_package_password(&password)?;
    let name = super::records_text(body, "packagePath");
    let path = if Path::new(&name).is_absolute() {
        PathBuf::from(name)
    } else {
        super::managed_file(&root(&service.paths)?, &name, "edmpoint")?
    };
    crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
    if path.extension().is_none_or(|e| e != "edmpoint") {
        return Err(invalid("请选择本版本 .edmpoint 恢复点文件。"));
    }
    let (store, paths, id) = (service.store.clone(), service.paths.clone(), actor.id);
    service.jobs.start_backup(
        actor,
        RESTORE_DISASTER_RECOVERY_PACKAGE,
        "DisasterRecoveryRestore",
        "校验并暂存灾备恢复",
        move |_| {
            let actor = auth::current_actor(&store, id)?;
            auth::authorize_operation(&actor, RESTORE_DISASTER_RECOVERY_PACKAGE, &[])?;
            let _gate = package::lock(&paths)?;
            crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
            let prepared = super::portable::prepare(
                &paths,
                &marker(&paths)?,
                &password,
                fs::File::open(path)?,
                "SQLite",
            )?;
            Ok(prepared.into_task(
                _gate,
                "restore-disaster-recovery",
                "恢复已暂存。请立即退出并重新打开程序，恢复前原数据会保存在恢复目录。",
            ))
        },
    )
}
pub fn apply_pending(paths: &RuntimePaths) -> Result<()> {
    let marker = marker(paths)?;
    let _gate = package::lock(paths)?;
    if !package::confirm_task_marker(&paths.cache_root, &marker, |job| {
        let db = export_doc_storage::Connection::sqlite_read_only(&paths.sqlite_database_path())?;
        let row = db.find_identity("background-jobs", job)?.ok_or_else(|| {
            unavailable("恢复任务提交记录缺失，已停止恢复。")
                .coded(crate::api::ErrorCode::DependencyUnavailable)
        })?;
        Ok(row["status"] == "Succeeded")
    })? {
        return Ok(());
    }
    match package::snapshot(&marker)? {
        Some(package::SnapshotRestore::Sqlite { sha256 }) => {
            return super::snapshot::apply_sqlite(paths, &marker, &sha256);
        }
        Some(package::SnapshotRestore::Repository { ref provider, .. }) if provider == "SQLite" => {
        }
        Some(_) => return Err(invalid("恢复数据库类型不匹配。")),
        None => {}
    }
    let Some(staging) = package::staged(paths, &marker, package::SQLITE, &paths.disk_space)? else {
        return Ok(());
    };
    export_doc_storage::verify_sqlite_backup(&staging.join(package::SQLITE))?;
    let snapshot =
        export_doc_storage::Connection::sqlite_read_only(&staging.join(package::SQLITE))?;
    restore_files::install(paths, &staging, true, |cursor| {
        Ok(snapshot.file_objects(cursor, 200, false)?)
    })?;
    fs::remove_file(marker)?;
    Ok(())
}
