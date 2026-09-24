//! Encrypted PostgreSQL migration packages include credentials and managed user templates.
use super::{
    MIGRATION_STORAGE_POLICY, NativeService, auth,
    error::{Result, invalid, unavailable},
    package, postgres, sealed,
    store::Actor,
    tasks::TaskOutput,
};
use crate::{contracts, generated_api::*, paths::RuntimePaths};
use serde_json::{Value, json};
use std::{fs, path::PathBuf};
const MAGIC: &[u8] = b"EDM-SERVER-MIGRATION-1";
pub(super) fn root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::migration_root(paths)
}
pub(super) fn marker_path(paths: &RuntimePaths) -> Result<PathBuf> {
    Ok(root(paths)?.join(".pending-restore.json"))
}
pub(super) fn status(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let supported = service.provider()? == "PostgreSQL";
    let configured = postgres::team_configured(service)?;
    let ready = postgres::team_tools(&service.paths).ready();
    let pending = marker_path(&service.paths)?.try_exists()?;
    let mut response = contracts::initial(contracts::schema("ApiServerMigrationStatusResponse"));
    response["supported"] = json!(supported);
    response["postgreSqlConfigured"] = json!(configured);
    response["toolsReady"] = json!(ready);
    response["pendingRestore"] = json!(pending);
    response["packageRoot"] = json!(root(&service.paths)?);
    response["message"] = json!(if !supported {
        "单机版请使用灾备包。"
    } else if pending {
        "迁移已暂存。停止服务后运行 --restore-pending 维护命令，再启动服务。"
    } else if !ready {
        "PostgreSQL 客户端工具未就绪。"
    } else {
        "迁移包包含数据库、凭据主密钥和用户模板。"
    });
    response["storagePolicy"] = json!(MIGRATION_STORAGE_POLICY);
    Ok(response)
}
pub(super) fn authorize(service: &NativeService, actor: &Actor, body: &Value) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let action = super::records_text(body, "action");
    let action = if action == super::ACTION_RESTORE_DATABASE {
        super::ACTION_RESTORE_DATABASE
    } else if action == super::ACTION_RESTORE_SERVER {
        super::ACTION_RESTORE_SERVER
    } else {
        return Err(invalid("敏感操作类型无效。"));
    };
    super::verify_admin_password(
        &service.store,
        actor,
        body["adminPassword"].as_str().unwrap_or(""),
    )?;
    let (ticket, expiry) = super::issue_sensitive_ticket(actor, action)?;
    super::audit(
        &service.store,
        actor,
        "authorize-sensitive-operation",
        &format!("已为敏感操作 {action} 签发一次性票据"),
    )?;
    let mut response = contracts::initial(contracts::schema(
        "ApiSensitiveOperationAuthorizationResponse",
    ));
    response["action"] = json!(action);
    response["ticket"] = json!(ticket);
    response["expiresAtUtc"] = json!(expiry);
    Ok(response)
}

pub(super) fn create_package(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let password = zeroize::Zeroizing::new(body["password"].as_str().unwrap_or("").to_owned());
    super::validate_package_password(&password)?;
    if super::records_text(body, "confirmationText") != "MIGRATE" {
        return Err(invalid("请输入 MIGRATE 确认创建迁移包。"));
    }
    let tools = postgres::require_ready(service)?;
    super::verify_admin_password(
        &service.store,
        actor,
        body["adminPassword"].as_str().unwrap_or(""),
    )?;
    let (store, paths, protector, id) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
    );
    service.jobs.start(
        actor,
        "ServerMigrationPackage",
        "创建服务器迁移包",
        move |_| {
            let actor = auth::current_actor(&store, id)?;
            auth::authorize_operation(&actor, CREATE_SERVER_MIGRATION_PACKAGE, &[])?;
            let _gate = package::lock(&paths)?;
            let _templates = super::super::report_template_files::storage_lock(&paths)?;
            postgres::ensure_no_pending(&paths)?;
            let working = sealed::working_directory(&root(&paths)?)?;
            package::private_directory(&working)?;
            let result = (|| {
                let dump = working.join("database.dump");
                postgres::dump_to(&store, &protector, &tools.pg_dump, &dump)?;
                let bytes = package::create(
                    &paths,
                    &protector,
                    &dump,
                    package::POSTGRES,
                    &working,
                    MAGIC,
                    &password,
                )?;
                let target = root(&paths)?.join(format!(
                    "edm-migration-{}.edmmigration",
                    crate::paths::nonce().map_err(unavailable)?
                ));
                crate::operation::check()?;
                sealed::atomic_write(&target, &bytes)?;
                super::audit(
                    &store,
                    &actor,
                    "create-server-migration-package",
                    "加密迁移包已创建",
                )?;
                Ok(TaskOutput::managed_file(
                    target,
                    "迁移包已创建，可下载。请单独保管密码。".into(),
                ))
            })();
            let _ = fs::remove_dir_all(&working);
            result
        },
    )
}
pub(super) fn stage_restore(
    _service: &NativeService,
    _actor: &Actor,
    _parameters: &[(&str, String)],
) -> Result<Value> {
    Err(invalid("请选择并上传 .edmmigration 迁移包。"))
}
pub(super) fn stage_restore_upload(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
    file_name: &str,
    content: &[u8],
) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    postgres::require_ready(service)?;
    super::confirmation(parameters)?;
    super::sensitive_ticket(parameters, actor, super::ACTION_RESTORE_SERVER)?;
    super::managed_file(&root(&service.paths)?, file_name, "edmmigration")?;
    let password = parameters
        .iter()
        .find(|(key, _)| *key == super::MIGRATION_PASSWORD_HEADER)
        .map(|(_, value)| value.as_str())
        .unwrap_or("");
    super::validate_package_password(password)?;
    let _gate = package::lock(&service.paths)?;
    postgres::ensure_no_pending(&service.paths)?;
    package::stage(
        &service.paths,
        &marker_path(&service.paths)?,
        MAGIC,
        password,
        content,
        package::POSTGRES,
    )?;
    super::audit(
        &service.store,
        actor,
        "restore-server-migration",
        "迁移包已校验并暂存",
    )?;
    postgres::restore_response(
        &service.paths,
        file_name,
        "迁移已暂存。停止服务后运行 --restore-pending 维护命令，再启动服务。",
        MIGRATION_STORAGE_POLICY,
    )
}
