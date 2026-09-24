//! PostgreSQL 物理备份与还原：通过受控子进程调用程序根 Tools/PostgreSQL/bin 下的
//! pg_dump／pg_restore，custom-format 备份写入运行数据根 Backups/PostgreSQL/。
//! 恢复采用暂存＋重启：登记标记后由启动流程在重新建立数据库连接之前执行
//! pg_restore。SQLite 单机版访问这些操作时返回明确的 501；工具未就绪返回 503。
use super::{
    NativeService, PG_STORAGE_POLICY, PLAN_STORAGE_POLICY, auth,
    error::{Result, error, invalid, unavailable, unsupported},
    store::{Actor, Store},
    tasks,
};
use crate::{
    contracts,
    controlled_process::{self, Output},
    generated_api::*,
    paths::RuntimePaths,
};
use serde_json::{Value, json};
use std::{
    fs,
    path::{Path, PathBuf},
    process::Command,
    time::Duration,
};

const PG_DUMP: &str = "pg_dump";
const PG_RESTORE: &str = "pg_restore";
const BACKUP_SUFFIX: &str = ".dump";
const SAFETY_PREFIX: &str = ".";
const RESTART_MARKER: &str = ".pending-restore.json";
const BACKUP_TIMEOUT: Duration = Duration::from_secs(1800);
const RESTORE_TIMEOUT: Duration = Duration::from_secs(1800);
const OUTPUT_LIMIT: usize = 64 * 1024 * 1024;
const MAX_IDENTIFIER_BYTES: usize = 63;
const MAX_OLD_OWNER_ROLES: usize = 100;

pub(super) struct Endpoint {
    host: String,
    port: String,
    database: String,
    username: String,
    password: zeroize::Zeroizing<String>,
    ssl_mode: &'static str,
}
impl Endpoint {
    fn read(service: &NativeService) -> Result<Self> {
        Self::read_parts(&service.store, &service.protector)
    }
    /// Use the connection injected into this service, including secret-file deployments.
    fn read_parts(store: &Store, _protector: &crate::secrets::Protector) -> Result<Self> {
        #[cfg(feature = "postgres")]
        {
            Self::parse_url(store.postgres_connection()?)
        }
        #[cfg(not(feature = "postgres"))]
        {
            let _ = store;
            Err(unsupported("当前构建不包含 PostgreSQL。"))
        }
    }
    fn parse_url(connection: &str) -> Result<Self> {
        #[cfg(feature = "postgres")]
        {
            let options = export_doc_storage::postgres_client_parameters(connection)?;
            Ok(Self {
                host: options.host,
                port: options.port,
                database: options.database,
                username: options.username,
                password: zeroize::Zeroizing::new(options.password),
                ssl_mode: options.ssl_mode,
            })
        }
        #[cfg(not(feature = "postgres"))]
        {
            let _ = connection;
            Err(unsupported("当前构建不包含 PostgreSQL。"))
        }
    }
    fn configured(&self) -> bool {
        !self.host.is_empty() && !self.database.is_empty() && !self.username.is_empty()
    }
    fn apply(&self, command: &mut Command) {
        command
            .args(["--host", &self.host])
            .args(["--port", &self.port])
            .args(["--username", &self.username])
            .arg("--no-password");
        command
            .env("PGPASSWORD", self.password.as_str())
            .env("PGSSLMODE", self.ssl_mode)
            .env("PGCONNECT_TIMEOUT", "10")
            .env_remove("PGSERVICE")
            .env_remove("PGSERVICEFILE");
        if self.ssl_mode == "verify-full" {
            command.env("PGSSLROOTCERT", "system");
        }
    }
}

fn text(value: &Value, key: &str) -> String {
    value[key].as_str().unwrap_or("").trim().to_string()
}

pub(super) fn postgres_root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::postgres_root(paths)
}
fn restart_marker(paths: &RuntimePaths) -> Result<PathBuf> {
    Ok(postgres_root(paths)?.join(RESTART_MARKER))
}
fn bin_root(paths: &RuntimePaths) -> PathBuf {
    paths.app_root.join("Tools").join("PostgreSQL").join("bin")
}
fn tool(paths: &RuntimePaths, name: &str) -> PathBuf {
    bin_root(paths).join(if cfg!(windows) {
        format!("{name}.exe")
    } else {
        name.to_string()
    })
}
pub(super) struct Tools {
    pub(super) pg_dump: PathBuf,
    pg_restore: PathBuf,
}
impl Tools {
    fn resolve(paths: &RuntimePaths) -> Self {
        Self {
            pg_dump: tool(paths, PG_DUMP),
            pg_restore: tool(paths, PG_RESTORE),
        }
    }
    pub(super) fn ready(&self) -> bool {
        self.pg_dump.is_file() && self.pg_restore.is_file()
    }
    fn missing(&self) -> String {
        [("pg_dump", &self.pg_dump), ("pg_restore", &self.pg_restore)]
            .iter()
            .filter(|(_, path)| !path.is_file())
            .map(|(name, _)| (*name).to_string())
            .collect::<Vec<_>>()
            .join("、")
    }
}

fn require_team(service: &NativeService) -> Result<()> {
    if service.provider()? != "PostgreSQL" {
        return Err(unsupported(
            "PostgreSQL 物理备份只在团队库（PostgreSQL 18）模式下可用；SQLite 单机版请使用灾难恢复包。",
        ));
    }
    Ok(())
}
/// 受控子进程执行前的连接与工具检查。
pub(super) fn require_ready(service: &NativeService) -> Result<Tools> {
    require_team(service)?;
    let endpoint = Endpoint::read(service)?;
    if !endpoint.configured() {
        return Err(invalid(
            "当前未完整配置 PostgreSQL 团队数据库（主机、数据库名或账号缺失），不能执行物理备份操作。",
        ));
    }
    let tools = Tools::resolve(&service.paths);
    if !tools.ready() {
        return Err(unavailable(format!(
            "PostgreSQL 客户端工具未就绪（缺少 {}），不能执行物理备份操作。",
            tools.missing()
        )));
    }
    Ok(tools)
}
/// 团队库连接是否完整配置（状态接口在 SQLite 桌面上也返回明确的 supported=false）。
pub(super) fn team_configured(service: &NativeService) -> Result<bool> {
    if service.provider()? != "PostgreSQL" {
        return Ok(false);
    }
    Endpoint::read(service).map(|endpoint| endpoint.configured())
}
/// 服务器迁移状态需要工具就绪信息。
pub(super) fn team_tools(paths: &RuntimePaths) -> Tools {
    Tools::resolve(paths)
}

fn backup_name(prefix: &str) -> Result<String> {
    Ok(format!(
        "{prefix}-{}-{}{BACKUP_SUFFIX}",
        chrono::Utc::now().format("%Y%m%d-%H%M%S"),
        crate::paths::nonce().map_err(unavailable)?
    ))
}

fn run(command: &mut Command, timeout: Duration) -> Result<Output> {
    if cfg!(target_os = "linux") {
        if let Some(root) = Path::new(command.get_program())
            .parent()
            .and_then(Path::parent)
        {
            let libraries = root.join("lib");
            crate::paths::ensure_safe_absolute(&libraries).map_err(invalid)?;
            command.env("LD_LIBRARY_PATH", libraries);
        }
    }
    controlled_process::run(command, Vec::new(), OUTPUT_LIMIT, timeout)
}

fn list_backups(paths: &RuntimePaths) -> Result<Vec<Value>> {
    let root = postgres_root(paths)?;
    let mut backups = Vec::new();
    for entry in fs::read_dir(&root)? {
        let entry = entry?;
        let path = entry.path();
        crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if path.is_symlink() || !entry.metadata()?.is_file() {
            continue;
        }
        if path
            .extension()
            .is_none_or(|extension| !extension.eq_ignore_ascii_case("dump"))
        {
            continue;
        }
        let name = match path.file_name().and_then(|name| name.to_str()) {
            Some(name) => name,
            None => continue,
        };
        if name.starts_with(SAFETY_PREFIX) {
            continue;
        }
        let metadata = entry.metadata()?;
        let stamp = chrono::DateTime::<chrono::Utc>::from(metadata.modified()?).to_rfc3339();
        let mut item = contracts::initial(contracts::schema("ApiSharedDatabaseBackupItemDto"));
        item["fileName"] = json!(name);
        item["fullPath"] = json!(path);
        item["sizeBytes"] = json!(metadata.len());
        item["createdAt"] = json!(stamp);
        item["lastWriteTime"] = json!(stamp);
        backups.push(item);
    }
    backups.sort_by(|a, b| {
        b["lastWriteTime"]
            .as_str()
            .cmp(&a["lastWriteTime"].as_str())
            .then_with(|| b["fileName"].as_str().cmp(&a["fileName"].as_str()))
    });
    Ok(backups)
}

fn maintenance_status(service: &NativeService, endpoint: &Endpoint) -> Result<Value> {
    let tools = Tools::resolve(&service.paths);
    let mut status =
        contracts::initial(contracts::schema("ApiPostgreSqlMaintenanceStatusResponse"));
    status["postgreSqlSelected"] = json!(service.provider()? == "PostgreSQL");
    status["postgreSqlConfigured"] = json!(endpoint.configured());
    status["host"] = json!(endpoint.host);
    status["port"] = json!(endpoint.port.parse::<i64>().unwrap_or(5432));
    status["database"] = json!(endpoint.database);
    status["username"] = json!(endpoint.username);
    status["backupRoot"] = json!(postgres_root(&service.paths)?);
    status["toolBinRoot"] = json!(bin_root(&service.paths));
    status["pgDumpPath"] = json!(tools.pg_dump);
    status["pgRestorePath"] = json!(tools.pg_restore);
    status["psqlPath"] = json!(tool(&service.paths, "psql"));
    status["toolsReady"] = json!(tools.ready());
    status["storagePolicy"] = json!(PG_STORAGE_POLICY);
    Ok(status)
}

fn managed_write(path: &Path, bytes: &[u8]) -> Result<()> {
    crate::paths::ensure_safe_absolute(path).map_err(invalid)?;
    crate::paths::atomic_write(path, bytes).map_err(unavailable)
}

pub(super) fn list(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    require_team(service)?;
    let endpoint = Endpoint::read(service)?;
    let mut response =
        contracts::initial(contracts::schema("ApiPostgreSqlPhysicalBackupListResponse"));
    response["status"] = maintenance_status(service, &endpoint)?;
    response["backups"] = json!(list_backups(&service.paths)?);
    Ok(response)
}

pub(super) fn create_backup(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let tools = require_ready(service)?;
    let store = service.store.clone();
    let protector = service.protector.clone();
    let paths = service.paths.clone();
    let actor_id = actor.id;
    let pg_dump = tools.pg_dump;
    service.jobs.start(
        actor,
        "PostgreSqlPhysicalBackup",
        "创建 PostgreSQL 物理备份",
        move |_| {
            let _gate = super::package::lock(&paths)?;
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, CREATE_POSTGRE_SQL_PHYSICAL_BACKUP, &[])?;
            let endpoint = Endpoint::read_parts(&store, &protector)?;
            if !endpoint.configured() {
                return Err(invalid(
                    "当前未完整配置 PostgreSQL 团队数据库，不能创建物理备份。",
                ));
            }
            let name = backup_name("edm-postgresql")?;
            let root = postgres_root(&paths)?;
            let target = root.join(&name);
            let temporary = root.join(format!(".{name}.tmp"));
            let result = (|| {
                crate::paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
                let mut command = Command::new(&pg_dump);
                endpoint.apply(&mut command);
                command
                    .args(["--format", "custom"])
                    .args(["--file", &temporary.to_string_lossy()])
                    .arg("--dbname")
                    .arg(&endpoint.database);
                let output = run(&mut command, BACKUP_TIMEOUT)?;
                if !output.status.success() {
                    return Err(unavailable(format!(
                        "pg_dump 执行失败（{}）：{}",
                        output.status,
                        output.stderr.trim()
                    )));
                }
                crate::paths::ensure_safe_absolute(&target).map_err(invalid)?;
                fs::rename(&temporary, &target)?;
                if !target.is_file() || fs::metadata(&target)?.len() == 0 {
                    return Err(unavailable("pg_dump 未生成有效的物理备份文件。"));
                }
                super::audit(
                    &store,
                    &actor,
                    "create-postgresql-backup",
                    &format!("{name} 已写入受管 PostgreSQL 备份目录"),
                )?;
                Ok(target)
            })();
            if result.is_err() {
                let _ = fs::remove_file(&temporary);
            }
            let target = result?;
            Ok(tasks::TaskOutput {
                file: None,
                detail: format!("{name} 已写入运行目录，可通过下载票据安全获取。"),
                destination: Some(target),
                directory: None,
                managed_file: None,
            })
        },
    )
}

pub(super) fn create_restore_plan(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    require_ready(service)?;
    let backup_file_name = super::records_text(body, "backupFileName");
    let target_database = super::records_text(body, "targetDatabase");
    let application_role = super::records_text(body, "applicationRole");
    let old_owner_roles = body["oldOwnerRoles"]
        .as_array()
        .map(|roles| {
            roles
                .iter()
                .filter_map(|role| {
                    role.as_str()
                        .map(str::trim)
                        .filter(|value| !value.is_empty())
                        .map(str::to_string)
                })
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    if backup_file_name.is_empty() {
        return Err(invalid("请选择要生成还原计划的备份文件。"));
    }
    let backup_path =
        super::managed_file(&postgres_root(&service.paths)?, &backup_file_name, "dump")?;
    if !backup_path.is_file() {
        return Err(error(404, "未找到指定的 PostgreSQL 备份。"));
    }
    validate_identifier(&target_database, "目标数据库名")?;
    validate_identifier(&application_role, "应用角色名")?;
    if old_owner_roles.len() > MAX_OLD_OWNER_ROLES {
        return Err(invalid(format!(
            "旧属主角色不能超过 {MAX_OLD_OWNER_ROLES} 个。"
        )));
    }
    for role in &old_owner_roles {
        validate_identifier(role, "旧属主角色")?;
    }
    let plan_root = super::restore_plan_root(&service.paths)?.join(format!(
        "plan-{}",
        chrono::Utc::now().format("%Y%m%d-%H%M%S")
    ));
    crate::paths::ensure_safe_absolute(&plan_root).map_err(invalid)?;
    fs::create_dir_all(&plan_root)?;
    let restore_script_path = plan_root.join("restore.ps1");
    let ownership_sql_path = plan_root.join("post_restore_ownership.sql");
    let quote = |value: &str| format!("'{}'", value.replace('\'', "''"));
    let script = format!(
        "param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')][string]$OwnerRole)\n$ErrorActionPreference = 'Stop'\n# Stop the API first. Supply maintenance credentials through PG environment variables.\n& pg_restore --dbname {db} --role $OwnerRole --clean --if-exists --single-transaction --exit-on-error --no-owner --no-privileges --no-password {backup}\nif ($LASTEXITCODE -ne 0) {{ exit $LASTEXITCODE }}\n& psql -X --dbname {db} --no-password --set ON_ERROR_STOP=1 --set \"owner_role=$OwnerRole\" --single-transaction --file {sql}\nexit $LASTEXITCODE\n",
        db = quote(&target_database),
        backup = quote(&backup_path.to_string_lossy()),
        sql = quote(&ownership_sql_path.to_string_lossy())
    );
    managed_write(&restore_script_path, script.as_bytes())?;
    let mut sql = String::from(
        "-- Maintenance and NOLOGIN ownership remain separate from the business login.\n",
    );
    for role in &old_owner_roles {
        sql.push_str(&format!(
            "REASSIGN OWNED BY \"{role}\" TO :\"owner_role\";\n"
        ));
    }
    sql.push_str(&format!("SET ROLE :\"owner_role\";\nREVOKE CREATE ON SCHEMA public FROM PUBLIC;\nGRANT USAGE ON SCHEMA public TO \"{application_role}\";\nGRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \"{application_role}\";\nGRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO \"{application_role}\";\nALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO \"{application_role}\";\nALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO \"{application_role}\";\n"));
    managed_write(&ownership_sql_path, sql.as_bytes())?;
    super::audit(
        &service.store,
        actor,
        "create-postgresql-restore-plan",
        &format!("{backup_file_name} 的还原计划已生成"),
    )?;
    let mut response = contracts::initial(contracts::schema("ApiPostgreSqlRestorePlanResponse"));
    response["success"] = json!(true);
    response["message"] = json!("还原计划已生成，执行前请由管理员按目标服务器复核。");
    response["planRoot"] = json!(plan_root);
    response["restoreScriptPath"] = json!(restore_script_path);
    response["ownershipSqlPath"] = json!(ownership_sql_path);
    response["backupFilePath"] = json!(backup_path);
    response["storagePolicy"] = json!(PLAN_STORAGE_POLICY);
    Ok(response)
}

fn validate_identifier(value: &str, label: &str) -> Result<()> {
    if value.is_empty() {
        return Err(invalid(format!("{label}不能为空。")));
    }
    if value.len() > MAX_IDENTIFIER_BYTES {
        return Err(invalid(format!(
            "{label}不能超过 {MAX_IDENTIFIER_BYTES} 字节。"
        )));
    }
    let mut chars = value.chars();
    let first = chars.next().expect("non-empty");
    if !(first.is_ascii_alphabetic() || first == '_') {
        return Err(invalid(format!("{label}须以字母或下划线开头。")));
    }
    if !chars.all(|char| char.is_ascii_alphanumeric() || char == '_') {
        return Err(invalid(format!("{label}只能包含字母、数字与下划线。")));
    }
    Ok(())
}

pub(super) fn create_download_ticket(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    require_team(service)?;
    let file_name = super::header(parameters, "fileName");
    let path = super::managed_file(&postgres_root(&service.paths)?, file_name, "dump")?;
    if !path.is_file() {
        return Err(error(404, "未找到指定的 PostgreSQL 备份。"));
    }
    super::audit(
        &service.store,
        actor,
        "create-postgresql-download-ticket",
        file_name,
    )?;
    super::issue_download_ticket(file_name.to_string())
}

pub(super) fn download_with_ticket(
    service: &NativeService,
    parameters: &[(&str, String)],
) -> Result<tasks::FileOutput> {
    let token = super::header(parameters, "token");
    let file_name = super::consume_download_ticket(token)?;
    let path = super::managed_file(&postgres_root(&service.paths)?, &file_name, "dump")?;
    let content = fs::read(&path)?;
    Ok(tasks::FileOutput {
        file_name,
        media_type: "application/octet-stream".into(),
        content,
    })
}

/// 写入暂存恢复标记（物理备份与服务器迁移共用）。
pub(super) fn stage_restore_marker(service: &NativeService, file_name: &str) -> Result<()> {
    ensure_no_pending(&service.paths)?;
    let path = super::managed_file(&postgres_root(&service.paths)?, file_name, "dump")?;
    let content = fs::read(&path)?;
    if !content.starts_with(b"PGDMP") {
        return Err(invalid("请选择 PostgreSQL custom-format 备份。"));
    }
    let marker = json!({
        "restoreId": crate::paths::nonce().map_err(unavailable)?,
        "sourceFileName": file_name,
        "sha256": super::sha256_hex(&content),
        "scheduledAtUtc": chrono::Utc::now().to_rfc3339(),
    });
    managed_write(
        &restart_marker(&service.paths)?,
        serde_json::to_vec(&marker)?.as_slice(),
    )
}

/// 暂存恢复的统一响应（物理备份与服务器迁移共用）。
pub(super) fn restore_response(
    paths: &RuntimePaths,
    file_name: &str,
    message: &str,
    storage_policy: &str,
) -> Result<Value> {
    let mut response = contracts::initial(contracts::schema("ApiServerMigrationRestoreResponse"));
    response["success"] = json!(true);
    response["restartRequired"] = json!(true);
    response["automaticRestartScheduled"] = json!(false);
    response["message"] = json!(message);
    response["packageFileName"] = json!(file_name);
    response["safetyBackupRoot"] = json!(postgres_root(paths)?);
    response["storagePolicy"] = json!(storage_policy);
    Ok(response)
}

/// 登记暂存恢复并返回统一响应。
pub(super) fn schedule_restore(
    service: &NativeService,
    actor: &Actor,
    backup_path: &Path,
) -> Result<Value> {
    let file_name = backup_path
        .file_name()
        .and_then(|name| name.to_str())
        .ok_or_else(|| invalid("备份文件名无效。"))?;
    stage_restore_marker(service, file_name)?;
    super::audit(
        &service.store,
        actor,
        "restore-postgresql-backup",
        &format!("{file_name} 已登记暂存恢复，等待重启执行"),
    )?;
    restore_response(
        &service.paths,
        file_name,
        "备份已暂存。停止服务后运行 --restore-pending 维护命令，再启动服务。",
        PG_STORAGE_POLICY,
    )
}

pub(super) fn restore(
    service: &NativeService,
    actor: &Actor,
    _parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    require_ready(service)?;
    super::verify_admin_password(
        &service.store,
        actor,
        body["adminPassword"].as_str().unwrap_or(""),
    )?;
    if super::records_text(body, "confirmationText") != super::CONFIRM_RESTORE_DATABASE {
        return Err(invalid("请输入 RESTORE DATABASE 确认恢复。"));
    }
    let file_name = super::records_text(body, "backupFileName");
    if file_name.is_empty() {
        return Err(invalid("请选择要恢复的 PostgreSQL 备份。"));
    }
    let backup_path = super::managed_file(&postgres_root(&service.paths)?, &file_name, "dump")?;
    let _gate = super::package::lock(&service.paths)?;
    if !backup_path.is_file() {
        return Err(error(404, "未找到指定的 PostgreSQL 备份。"));
    }
    schedule_restore(service, actor, &backup_path)
}

pub(super) fn upload_and_restore(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
    file_name: &str,
    content: &[u8],
) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    require_ready(service)?;
    super::sensitive_ticket(parameters, actor, super::ACTION_RESTORE_DATABASE)?;
    super::confirmation(parameters)?;
    if content.is_empty() {
        return Err(invalid("上传的备份文件为空。"));
    }
    let _gate = super::package::lock(&service.paths)?;
    ensure_no_pending(&service.paths)?;
    super::managed_file(&postgres_root(&service.paths)?, file_name, "dump")?;
    if !content.starts_with(b"PGDMP") {
        return Err(invalid("请选择 PostgreSQL custom-format 备份。"));
    }
    let staged = postgres_root(&service.paths)?.join(backup_name("uploaded")?);
    managed_write(&staged, content)?;
    schedule_restore(service, actor, &staged)
}

mod recovery;
pub use recovery::apply_pending;
pub(crate) use recovery::{dump_to, ensure_no_pending};
