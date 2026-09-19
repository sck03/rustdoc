//! 服务器迁移包：PostgreSQL 物理备份加清单，使用包密码派生的 AES-256-GCM 密封，
//! 写入运行数据根 Backups/ServerMigration/。恢复采用暂存＋重启：登记标记后由
//! 启动流程在重新建立数据库连接之前执行 pg_restore。所有写入都落在受管根内。
use super::{
    MIGRATION_STORAGE_POLICY, NativeService, auth,
    error::{Result, error, invalid, unavailable},
    postgres, sealed,
    store::Actor,
    tasks::TaskOutput,
};
use crate::{contracts, generated_api::*, paths::RuntimePaths};
use serde_json::{Value, json};
use std::{fs, path::PathBuf};

const MAGIC: &[u8] = b"EDM-SERVER-MIGRATION-1";
const PACKAGE_SUFFIX: &str = ".edmmigration";
const DUMP_ENTRY: &str = "Database/postgresql-physical.dump";
const MANIFEST_ENTRY: &str = "manifest.json";
const CONFIRM_TEXT: &str = "MIGRATE";
const SCHEMA_VERSION: u32 = 1;
const MARKER_NAME: &str = ".pending-restore.json";

pub(super) fn root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::directory(paths.data_root.join("Backups").join("ServerMigration"))
}
fn marker_path(paths: &RuntimePaths) -> Result<PathBuf> {
    Ok(root(paths)?.join(MARKER_NAME))
}
fn package_name() -> Result<String> {
    Ok(format!(
        "edm-server-migration-{}{PACKAGE_SUFFIX}",
        chrono::Utc::now().format("%Y%m%d-%H%M%S")
    ))
}

struct Manifest {
    package_id: String,
    package_file_name: String,
    dump_file_name: String,
    files: Vec<sealed::ManifestFile>,
}
impl Manifest {
    fn to_json(&self, created_at: &str) -> Value {
        json!({
            "schemaVersion": SCHEMA_VERSION,
            "packageId": self.package_id,
            "packageCreatedAtUtc": created_at,
            "packageFileName": self.package_file_name,
            "dumpFileName": self.dump_file_name,
            "files": self.files.iter().map(sealed::ManifestFile::to_json).collect::<Vec<_>>()
        })
    }
    fn from_json(value: &Value) -> Result<Self> {
        let package_id = value["packageId"]
            .as_str()
            .map(str::to_string)
            .ok_or_else(|| invalid("迁移包清单缺少包标识。"))?;
        let package_file_name = value["packageFileName"]
            .as_str()
            .map(str::to_string)
            .ok_or_else(|| invalid("迁移包清单缺少包文件名。"))?;
        let dump_file_name = value["dumpFileName"]
            .as_str()
            .map(str::to_string)
            .ok_or_else(|| invalid("迁移包清单缺少物理备份文件名。"))?;
        let files = value["files"]
            .as_array()
            .ok_or_else(|| invalid("迁移包清单缺少文件列表。"))?
            .iter()
            .map(|item| {
                let name = item["relativePath"]
                    .as_str()
                    .ok_or_else(|| invalid("清单条目缺少相对路径。"))?;
                let size_bytes = item["sizeBytes"]
                    .as_u64()
                    .ok_or_else(|| invalid("清单条目缺少字节数。"))?;
                let sha256 = item["sha256"]
                    .as_str()
                    .ok_or_else(|| invalid("清单条目缺少摘要。"))?;
                if name.is_empty() || sha256.is_empty() {
                    return Err(invalid("清单条目不完整。"));
                }
                Ok(sealed::ManifestFile {
                    name: name.into(),
                    size_bytes,
                    sha256: sha256.into(),
                })
            })
            .collect::<Result<Vec<_>>>()?;
        Ok(Self {
            package_id,
            package_file_name,
            dump_file_name,
            files,
        })
    }
}

pub(super) fn status(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let supported = service.provider()? == "PostgreSQL";
    let configured = postgres::team_configured(service).unwrap_or(false);
    let tools = postgres::team_tools(&service.paths);
    let marker = marker_path(&service.paths)?;
    let pending = marker.is_file();
    let mut response = contracts::initial(contracts::schema("ApiServerMigrationStatusResponse"));
    response["supported"] = json!(supported);
    response["postgreSqlConfigured"] = json!(configured);
    response["toolsReady"] = json!(tools.ready());
    response["pendingRestore"] = json!(pending);
    response["packageRoot"] = json!(root(&service.paths)?);
    response["message"] = json!(if !supported {
        "服务器迁移包只在 PostgreSQL 团队库模式下可用；SQLite 单机版请使用持卡机灾难恢复包。"
    } else if pending {
        "服务器迁移恢复已排队，请立即重启服务。"
    } else if !configured {
        "PostgreSQL 团队库连接未完整配置，请先在系统设置中保存连接信息。"
    } else if !tools.ready() {
        "PostgreSQL 客户端工具未就绪，请把 pg_dump／pg_restore 放到程序根 Tools/PostgreSQL/bin。"
    } else {
        "可创建加密服务器迁移包，完整迁移当前团队库到新服务器。"
    });
    response["storagePolicy"] = json!(MIGRATION_STORAGE_POLICY);
    if pending {
        if let Ok(value) = serde_json::from_str::<Value>(&fs::read_to_string(&marker)?) {
            response["restorePhase"] = json!(value["phase"].as_str().unwrap_or("staged"));
            response["restoreDetail"] = json!(value["detail"].as_str().unwrap_or(""));
            response["restoreUpdatedAtUtc"] = json!(value["scheduledAtUtc"].as_str().unwrap_or(""));
        }
    }
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
        &super::records_text(body, "adminPassword"),
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
    let password = super::records_text(body, "password");
    super::validate_package_password(&password)?;
    if super::records_text(body, "confirmationText") != CONFIRM_TEXT {
        return Err(invalid(format!(
            "创建服务器迁移包前需要输入确认文本 {CONFIRM_TEXT}。"
        )));
    }
    let tools = postgres::require_ready(service)?;
    super::verify_admin_password(
        &service.store,
        actor,
        &super::records_text(body, "adminPassword"),
    )?;
    let store = service.store.clone();
    let protector = service.protector.clone();
    let paths = service.paths.clone();
    let actor_id = actor.id;
    let pg_dump = tools.pg_dump;
    service.jobs.start(
        actor,
        "ServerMigrationPackage",
        "创建服务器迁移包",
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, CREATE_SERVER_MIGRATION_PACKAGE, &[])?;
            let root = root(&paths)?;
            let working = sealed::working_directory(&root)?;
            let result = (|| {
                if marker_path(&paths)?.is_file() {
                    return Err(error(409, "已有服务器迁移恢复任务等待重启执行，不能创建新的迁移包。"));
                }
                let dump = postgres::dump_for_migration(&store, &protector, &paths, &pg_dump)?;
                let created_at = chrono::Utc::now().to_rfc3339();
                let package_file_name = package_name()?;
                let manifest = Manifest {
                    package_id: crate::paths::nonce().map_err(unavailable)?,
                    package_file_name: package_file_name.clone(),
                    dump_file_name: dump
                        .file_name()
                        .and_then(|name| name.to_str())
                        .unwrap_or("")
                        .to_string(),
                    files: vec![sealed::ManifestFile::from_path(DUMP_ENTRY, &dump)?],
                };
                let manifest_bytes = serde_json::to_vec_pretty(&manifest.to_json(&created_at))?;
                let manifest_path = working.join("manifest.json");
                crate::paths::ensure_safe_absolute(&manifest_path).map_err(invalid)?;
                fs::write(&manifest_path, &manifest_bytes)?;
                let entries = vec![
                    sealed::Entry {
                        source: &manifest_path,
                        name: MANIFEST_ENTRY,
                    },
                    sealed::Entry {
                        source: &dump,
                        name: DUMP_ENTRY,
                    },
                ];
                let payload = sealed::zip_payload(&entries)?;
                let sealed_bytes = sealed::seal(MAGIC, &password, &payload)?;
                let target = root.join(&package_file_name);
                sealed::atomic_write(&target, &sealed_bytes)?;
                super::audit(
                    &store,
                    &actor,
                    "create-server-migration-package",
                    &format!("{package_file_name} 已写入受管迁移包目录"),
                )?;
                Ok((package_file_name, target, sealed_bytes.len()))
            })();
            let _ = fs::remove_dir_all(&working);
            let (package_file_name, target, size) = result?;
            Ok(TaskOutput {
                file: None,
                detail: format!(
                    "服务器迁移包已创建（{size} 字节）。恢复前必须再次确认目标服务器，恢复需要重启服务才能在建立数据库连接前完成切换。"
                ),
                destination: Some(target),
                directory: None,
            })
            .map(|output| {
                let _ = package_file_name;
                output
            })
        },
    )
}

/// 登记暂存恢复并返回统一响应。
fn schedule_restore(service: &NativeService, actor: &Actor, file_name: &str) -> Result<Value> {
    postgres::stage_restore_marker(service, file_name)?;
    super::audit(
        &service.store,
        actor,
        "restore-server-migration",
        &format!("{file_name} 已登记暂存恢复，等待重启执行"),
    )?;
    postgres::restore_response(
        &service.paths,
        file_name,
        "服务器迁移恢复已安排。请尽快重启服务，恢复会在重新建立数据库连接之前执行。",
        MIGRATION_STORAGE_POLICY,
    )
}

/// 暂存恢复前校验请求：票据、确认文本与受管备份文件。
fn validate_restore(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<()> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    postgres::require_ready(service)?;
    super::sensitive_ticket(parameters, actor, super::ACTION_RESTORE_SERVER)?;
    super::confirmation(parameters)?;
    if marker_path(&service.paths)?.is_file() {
        return Err(error(409, "已有服务器迁移恢复任务等待重启执行。"));
    }
    Ok(())
}

pub(super) fn stage_restore(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<Value> {
    validate_restore(service, actor, parameters)?;
    let file_name = super::header(parameters, super::PG_BACKUP_FILE_NAME_HEADER);
    let backup_path =
        super::managed_file(&super::postgres_root(&service.paths)?, file_name, "dump")?;
    if !backup_path.is_file() {
        return Err(error(404, "未找到指定的 PostgreSQL 备份。"));
    }
    schedule_restore(
        service,
        actor,
        backup_path
            .file_name()
            .and_then(|name| name.to_str())
            .ok_or_else(|| invalid("备份文件名无效。"))?,
    )
}

pub(super) fn stage_restore_upload(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
    file_name: &str,
    content: &[u8],
) -> Result<Value> {
    validate_restore(service, actor, parameters)?;
    if content.is_empty() {
        return Err(invalid("上传的迁移包文件为空。"));
    }
    let staged = super::managed_file(&super::postgres_root(&service.paths)?, file_name, "dump")?;
    crate::paths::ensure_safe_absolute(&staged).map_err(invalid)?;
    crate::paths::atomic_write(&staged, content).map_err(unavailable)?;
    schedule_restore(
        service,
        actor,
        staged
            .file_name()
            .and_then(|name| name.to_str())
            .ok_or_else(|| invalid("备份文件名无效。"))?,
    )
}

/// 启动时执行已排队的迁移暂存恢复，必须在建立业务连接之前调用。
pub fn apply_pending(paths: &RuntimePaths, connection_url: &str) -> Result<()> {
    postgres::apply_pending_marker(paths, connection_url, &marker_path(paths)?)
}
