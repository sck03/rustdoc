//! 团队备份与灾备（Batch C）：PostgreSQL 物理备份／还原计划／受控下载票据、
//! WebDAV 云备份、持卡机灾备包、服务器迁移包与共享库归属改派。
//!
//! PostgreSQL 物理备份与服务器迁移只在 `postgres` feature 下通过受控子进程调用
//! pg_dump／pg_restore／psql；未启用该 feature 的 SQLite 桌面构建访问这些操作时
//! 返回明确的 501。灾备包、云备份状态与连接测试在 SQLite 桌面和 PostgreSQL
//! 服务器上都能工作；云备份由保存在设置中的 WebDAV 配置驱动，未配置时返回明确
//! 状态而不是错误。所有写入都落在受管数据根目录内。
use super::{
    NativeService, auth,
    error::{self, Result, error, invalid, unavailable, unsupported},
    settings,
    store::{self, Actor, Store},
    tasks,
};
use crate::{
    contracts,
    generated_api::*,
    paths::{RuntimePaths, ensure_safe_absolute, nonce},
};
use export_doc_storage::AuditWrite;
use pbkdf2::pbkdf2_hmac_array;
use serde_json::{Value, json};
#[cfg(test)]
use sha2::Digest;
use sha2::Sha256;
use std::{
    fs,
    path::{Path, PathBuf},
    time::Duration,
};
use subtle::ConstantTimeEq;

pub mod cloud;
pub mod disaster;
pub mod migration;
pub mod ownership;
mod package;
pub mod postgres;
mod restore_files;
#[cfg(feature = "s3-backup")]
mod s3;
pub(in crate::engine) mod scheduled;
mod scratch;
mod sealed;
mod tickets;
mod transfer;
pub(super) use tickets::Tickets;
pub use transfer::UploadAuthorization;
#[cfg(test)]
mod tests;

pub const OPERATIONS: &[Operation] = &[
    GET_BACKUP_PLAN,
    SAVE_BACKUP_PLAN,
    RUN_BACKUP_PLAN,
    TEST_BACKUP_REPLICA,
    CREATE_RECOVERY_ARCHIVE_DOWNLOAD_TICKET,
    STAGE_RECOVERY_ARCHIVE_RESTORE,
    DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET,
    CREATE_POSTGRE_SQL_PHYSICAL_BACKUP,
    CREATE_POSTGRE_SQL_PHYSICAL_BACKUP_DOWNLOAD_TICKET,
    DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
    UPLOAD_AND_RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
    RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
    CREATE_POSTGRE_SQL_RESTORE_PLAN,
    LIST_POSTGRE_SQL_PHYSICAL_BACKUPS,
    GET_CLOUD_BACKUP_STATUS,
    TEST_CLOUD_BACKUP_CONNECTION,
    UPLOAD_LATEST_DATABASE_BACKUP_TO_CLOUD,
    LIST_CLOUD_DATABASE_BACKUPS,
    DOWNLOAD_CLOUD_DATABASE_BACKUP,
    GET_DISASTER_RECOVERY_STATUS,
    CREATE_DISASTER_RECOVERY_PACKAGE,
    RESTORE_DISASTER_RECOVERY_PACKAGE,
    GET_SERVER_MIGRATION_STATUS,
    CREATE_SERVER_MIGRATION_PACKAGE,
    STAGE_SERVER_MIGRATION_RESTORE,
    AUTHORIZE_SERVER_MIGRATION_OPERATION,
    GET_SHARED_DATABASE_OWNERSHIP_SUMMARY,
    TRANSFER_SHARED_DATABASE_OWNERSHIP,
];
/// 灾备包只能由受信任的桌面版创建和恢复；浏览器版请使用服务器迁移包。
pub const LOCAL: &[Operation] = &[
    CREATE_DISASTER_RECOVERY_PACKAGE,
    RESTORE_DISASTER_RECOVERY_PACKAGE,
];
/// 上传型操作：上传并恢复 PostgreSQL 物理备份、暂存服务器迁移包。
pub const UPLOADS: &[Operation] = &[
    UPLOAD_AND_RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP,
    STAGE_SERVER_MIGRATION_RESTORE,
];

const CONFIRM_RESTORE_DATABASE: &str = "RESTORE DATABASE";
const CONFIRMATION_HEADER: &str = "X-ExportDocManager-Restore-Confirmation";
const TICKET_HEADER: &str = "X-ExportDocManager-Sensitive-Operation-Ticket";
const MIGRATION_PASSWORD_HEADER: &str = "X-ExportDocManager-Migration-Password";
pub(super) const ACTION_RESTORE_DATABASE: &str = "restore-database";
pub(super) const ACTION_RESTORE_SERVER: &str = "restore-server";
const MAX_PASSWORD_CHARS: usize = 1024;
const MIN_PACKAGE_PASSWORD_CHARS: usize = 8;
pub(super) const PG_STORAGE_POLICY: &str = "PostgreSQL 团队库物理备份默认写入运行数据根 Backups/PostgreSQL/，优先使用程序根 Tools/PostgreSQL/bin 下的 pg_dump/pg_restore/psql；custom-format .dump 包含完整业务数据但本身不加密，复制到外部介质前必须使用受控加密存储；不把 PostgreSQL 工具或备份默认放到系统 C 盘、AppData 或 ProgramData。";
pub(super) const PLAN_STORAGE_POLICY: &str = "PostgreSQL 还原计划写入运行数据根 Backups/PostgreSQL/RestorePlans/，仅供管理员复核后离线执行。日常恢复请使用界面暂存与 --restore-pending 维护流程，它同时校验数据库版本、实例锁和权限；生成脚本本身不会恢复数据。";
pub(super) const MIGRATION_STORAGE_POLICY: &str = "服务器迁移包写入运行数据根 Backups/ServerMigration/，包含 PostgreSQL 数据库、凭据主密钥和用户文件模板，整体密码加密。暂存后必须停止应用并运行 --restore-pending 维护命令，成功后再启动；普通重启不会自动恢复。";
pub(super) const DISASTER_STORAGE_POLICY: &str = "持卡机灾备包默认写入运行数据根 DisasterRecovery/，包含当前 SQLite 数据库快照与本机主密钥，使用密码派生的 AES-256-GCM 密封；包与密码必须分开保管，并至少复制一份到脱机介质。";
pub(super) const CLOUD_STORAGE_POLICY: &str = "WebDAV 云备份只读取保存在运行数据根配置中的 WebDAV 设置，只上传运行数据根 Backups/ 中当前数据库对应的最新备份；不接受任意本地路径，不读取发票/报关业务表，也不读取付款/报销业务表。";
pub(super) const OWNERSHIP_STORAGE_POLICY: &str = "共享库归属改派只更新发票、付款报销、客户、出口商、收款人、CRM、供应商、商机、邮件/报表模板和装柜方案的 ownerUserId、departmentId、companyScope 归属字段；关联子记录继续随所属业务聚合访问，不移动附件、不生成导出目录、不读取用户显式导出文件。";

// --- 受管目录与路径辅助。所有写入必须落在受管根目录内。 ---

pub(super) fn backup_root(paths: &RuntimePaths) -> Result<PathBuf> {
    directory(paths.data_root.join("Backups"))
}
pub(super) fn postgres_root(paths: &RuntimePaths) -> Result<PathBuf> {
    directory(backup_root(paths)?.join("PostgreSQL"))
}
pub(super) fn restore_plan_root(paths: &RuntimePaths) -> Result<PathBuf> {
    directory(postgres_root(paths)?.join("RestorePlans"))
}
pub(super) fn cloud_staging_root(paths: &RuntimePaths) -> Result<PathBuf> {
    directory(backup_root(paths)?.join(".cloud-downloads"))
}
pub(super) fn migration_root(paths: &RuntimePaths) -> Result<PathBuf> {
    directory(backup_root(paths)?.join("ServerMigration"))
}
pub(super) fn disaster_root(paths: &RuntimePaths) -> Result<PathBuf> {
    directory(paths.data_root.join("DisasterRecovery"))
}
fn directory(path: PathBuf) -> Result<PathBuf> {
    ensure_safe_absolute(&path).map_err(invalid)?;
    crate::secrets::private_directory(&path).map_err(unavailable)?;
    ensure_safe_absolute(&path).map_err(invalid)?;
    Ok(path)
}
/// 将一个已校验的文件名拼接到受管目录，拒绝路径穿越、符号链接与非法文件名。
pub(super) fn managed_file(root: &Path, file_name: &str, extension: &str) -> Result<PathBuf> {
    if !crate::paths::valid_file_name(file_name)
        || Path::new(file_name)
            .extension()
            .is_none_or(|value| !value.eq_ignore_ascii_case(extension))
    {
        return Err(invalid("只能选择当前备份列表中的文件名，不能传入路径。"));
    }
    let path = root.join(file_name);
    ensure_safe_absolute(&path).map_err(invalid)?;
    if path.is_symlink() {
        return Err(invalid("拒绝通过符号链接访问备份文件。"));
    }
    Ok(path)
}
/// 读取请求体中的文本字段（去首尾空白）。
pub(super) fn records_text(body: &Value, key: &str) -> String {
    super::records::text(body, key)
}

/// 取回声明在契约中的请求头参数（服务器已把 in: header 参数放进 parameters）。
pub(super) fn header<'a>(parameters: &'a [(&str, String)], name: &str) -> &'a str {
    crate::engine::request_values::find(parameters, name)
        .unwrap_or("")
        .trim()
}
pub(super) fn confirmation(parameters: &[(&str, String)], expected: &str) -> Result<()> {
    if header(parameters, CONFIRMATION_HEADER) != expected {
        return Err(invalid(format!("恢复前需要输入确认文本 {expected}。")));
    }
    Ok(())
}
/// 重新认证当前管理员。密码使用与登录相同的 PBKDF2-HMAC-SHA256 哈希验证。
pub(super) fn verify_admin_password(store: &Store, actor: &Actor, password: &str) -> Result<()> {
    if !actor.admin {
        return Err(error(403, "只有管理员可以执行敏感操作。"));
    }
    if password.is_empty() || password.chars().count() > MAX_PASSWORD_CHARS {
        return Err(invalid("管理员密码无效。"));
    }
    let credential = store
        .connection()?
        .credential(actor.id)?
        .ok_or_else(|| error(401, "管理员凭证缺失，不能执行敏感操作。"))?;
    let computed = pbkdf2_hmac_array::<Sha256, 32>(
        password.as_bytes(),
        &credential.salt,
        credential.iterations,
    );
    if credential.hash.len() != computed.len() || computed.ct_eq(&credential.hash).unwrap_u8() != 1
    {
        return Err(error(401, "管理员密码不正确。"));
    }
    Ok(())
}
pub(super) fn validate_package_password(password: &str) -> Result<()> {
    let length = password.chars().count();
    if !(MIN_PACKAGE_PASSWORD_CHARS..=MAX_PASSWORD_CHARS).contains(&length) {
        return Err(invalid(format!(
            "恢复包密码须为 {MIN_PACKAGE_PASSWORD_CHARS}–{MAX_PASSWORD_CHARS} 个字符。"
        )));
    }
    Ok(())
}
pub(super) fn audit(store: &Store, actor: &Actor, action: &str, note: &str) -> Result<()> {
    store.transaction(|connection| {
        connection.append_audit_details(
            &AuditWrite {
                kind: "team-backup",
                record_id: 0,
                version: 1,
                action,
                actor_id: actor.id,
                occurred_at: &super::store::timestamp(),
                note,
            },
            &json!({}),
        )?;
        Ok(())
    })
}
#[cfg(test)]
pub(super) fn sha256_hex(bytes: &[u8]) -> String {
    let mut digest = Sha256::new();
    digest.update(bytes);
    hex(&digest.finalize())
}
pub(super) use crate::paths::hex;
/// 受管目录中最新的本地 SQLite 备份（云备份上传与状态接口使用）。
pub(super) fn latest_local_backup(paths: &RuntimePaths) -> Result<Option<(String, u64, PathBuf)>> {
    let root = backup_root(paths)?;
    let mut latest: Option<(String, u64, std::time::SystemTime)> = None;
    for entry in fs::read_dir(&root)? {
        let entry = entry?;
        let path = entry.path();
        ensure_safe_absolute(&path).map_err(invalid)?;
        if path.is_symlink() || !entry.metadata()?.is_file() {
            continue;
        }
        if path
            .extension()
            .is_none_or(|extension| extension != "sqlite3")
        {
            continue;
        }
        let modified = entry.metadata()?.modified()?;
        let name = match path.file_name().and_then(|value| value.to_str()) {
            Some(name) => name.to_string(),
            None => continue,
        };
        match &mut latest {
            Some((_, _, previous)) if *previous >= modified => {}
            _ => latest = Some((name, entry.metadata()?.len(), modified)),
        }
    }
    Ok(latest.map(|(name, size, _)| (name.clone(), size, root.join(name))))
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    match operation {
        GET_BACKUP_PLAN => scheduled::status(service, actor),
        SAVE_BACKUP_PLAN => scheduled::save(service, actor, body),
        RUN_BACKUP_PLAN => scheduled::run_now(service, actor),
        TEST_BACKUP_REPLICA => scheduled::test_replica(service, actor),
        CREATE_RECOVERY_ARCHIVE_DOWNLOAD_TICKET => {
            scheduled::ticket(service, actor, header(parameters, "id"))
        }
        STAGE_RECOVERY_ARCHIVE_RESTORE => {
            scheduled::stage_restore(service, actor, header(parameters, "id"), body)
        }
        GET_CLOUD_BACKUP_STATUS => cloud::status(service, actor),
        TEST_CLOUD_BACKUP_CONNECTION => cloud::test_connection(service, actor),
        UPLOAD_LATEST_DATABASE_BACKUP_TO_CLOUD => cloud::upload_latest(service, actor),
        LIST_CLOUD_DATABASE_BACKUPS => cloud::list(service, actor),
        DOWNLOAD_CLOUD_DATABASE_BACKUP => cloud::download(service, actor, body),
        GET_DISASTER_RECOVERY_STATUS => disaster::status(service, actor),
        CREATE_DISASTER_RECOVERY_PACKAGE => disaster::create_package(service, actor, body),
        RESTORE_DISASTER_RECOVERY_PACKAGE => disaster::restore_package(service, actor, body),
        GET_SHARED_DATABASE_OWNERSHIP_SUMMARY => ownership::summary(service, actor),
        TRANSFER_SHARED_DATABASE_OWNERSHIP => ownership::transfer(service, actor, body),
        LIST_POSTGRE_SQL_PHYSICAL_BACKUPS => postgres::list(service, actor),
        CREATE_POSTGRE_SQL_PHYSICAL_BACKUP => postgres::create_backup(service, actor),
        CREATE_POSTGRE_SQL_RESTORE_PLAN => postgres::create_restore_plan(service, actor, body),
        CREATE_POSTGRE_SQL_PHYSICAL_BACKUP_DOWNLOAD_TICKET => {
            postgres::create_download_ticket(service, actor, query)
        }
        RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP => postgres::restore(service, actor, parameters, body),
        GET_SERVER_MIGRATION_STATUS => migration::status(service, actor),
        CREATE_SERVER_MIGRATION_PACKAGE => migration::create_package(service, actor, body),
        STAGE_SERVER_MIGRATION_RESTORE => migration::stage_restore(service, actor, parameters),
        AUTHORIZE_SERVER_MIGRATION_OPERATION => migration::authorize(service, actor, body),
        _ => Err(unsupported(format!(
            "此项原生操作尚未接入：{}。",
            operation.id
        ))),
    }
}

/// 上传型操作：上传并恢复 PostgreSQL 物理备份、暂存服务器迁移包。
pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    _metadata: &Value,
    file_name: &str,
    content: &[u8],
) -> Result<Value> {
    let authorization = transfer::begin(service, actor, operation, parameters, file_name)?;
    transfer::finish(service, actor, authorization, content)
}
