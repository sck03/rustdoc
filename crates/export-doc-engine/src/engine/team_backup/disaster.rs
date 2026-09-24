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
    io::Read,
    path::{Path, PathBuf},
};

const MAGIC: &[u8] = b"EDM-DISASTER-RECOVERY-1";
fn root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::disaster_root(paths)
}
fn marker(paths: &RuntimePaths) -> Result<PathBuf> {
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
        "创建加密恢复包，包含数据库、凭据主密钥和用户模板。"
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
    service.jobs.start(
        actor,
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
                store.connection()?.backup(&snapshot)?;
                let bytes = package::create(
                    &paths,
                    &protector,
                    &snapshot,
                    package::SQLITE,
                    &working,
                    MAGIC,
                    &password,
                )?;
                let target = root(&paths)?.join(format!(
                    "edm-recovery-{}.edmrecovery",
                    crate::paths::nonce().map_err(unavailable)?
                ));
                crate::operation::check()?;
                sealed::atomic_write(&target, &bytes)?;
                super::audit(
                    &store,
                    &actor,
                    "create-disaster-recovery-package",
                    "加密灾备包已创建",
                )?;
                Ok(TaskOutput::managed_file(
                    target,
                    "灾备包已创建。请下载到离线介质，并单独保管密码。".into(),
                ))
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
        super::managed_file(&root(&service.paths)?, &name, "edmrecovery")?
    };
    crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
    if path.extension().is_none_or(|e| e != "edmrecovery") {
        return Err(invalid("请选择 .edmrecovery 恢复包。"));
    }
    let (store, paths, id) = (service.store.clone(), service.paths.clone(), actor.id);
    service.jobs.start(
        actor,
        "DisasterRecoveryRestore",
        "校验并暂存灾备恢复",
        move |_| {
            let actor = auth::current_actor(&store, id)?;
            auth::authorize_operation(&actor, RESTORE_DISASTER_RECOVERY_PACKAGE, &[])?;
            let _gate = package::lock(&paths)?;
            crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
            let mut bytes = Vec::new();
            fs::File::open(path)?
                .take(sealed::MAX_PLAINTEXT_BYTES as u64 + 1024 * 1024 + 1)
                .read_to_end(&mut bytes)?;
            package::stage(
                &paths,
                &marker(&paths)?,
                MAGIC,
                &password,
                &bytes,
                package::SQLITE,
            )?;
            super::audit(
                &store,
                &actor,
                "restore-disaster-recovery",
                "已校验并暂存，等待重启",
            )?;
            Ok(TaskOutput {
                file: None,
                managed_file: None,
                destination: None,
                directory: None,
                detail: "恢复已暂存。请立即退出并重新打开程序，恢复前原数据会保存在恢复目录。"
                    .into(),
            })
        },
    )
}
pub fn apply_pending(paths: &RuntimePaths) -> Result<()> {
    let marker = marker(paths)?;
    let Some(staging) = package::staged(&marker, package::SQLITE)? else {
        return Ok(());
    };
    let _gate = package::lock(paths)?;
    export_doc_storage::verify_sqlite_backup(&staging.join(package::SQLITE))?;
    restore_files::install(paths, &staging, true)?;
    fs::remove_file(marker)?;
    Ok(())
}
