//! Offline PostgreSQL restore. The API never receives the maintenance credential.
use super::super::migration;
#[cfg(feature = "postgres")]
use super::super::{package, restore_files};
use super::*;

pub(crate) fn ensure_no_pending(paths: &RuntimePaths) -> Result<()> {
    for marker in [restart_marker(paths)?, migration::marker_path(paths)?] {
        crate::paths::ensure_safe_absolute(&marker).map_err(invalid)?;
        if marker.try_exists()? {
            return Err(error(
                409,
                "恢复已暂存。请停止 API，运行 --restore-pending 维护命令后再启动。",
            ));
        }
    }
    Ok(())
}
pub(crate) fn dump_to(
    store: &Store,
    protector: &crate::secrets::Protector,
    tool: &Path,
    target: &Path,
) -> Result<()> {
    dump(&Endpoint::read_parts(store, protector)?, tool, target)
}
fn dump(endpoint: &Endpoint, tool: &Path, target: &Path) -> Result<()> {
    crate::paths::ensure_safe_absolute(target).map_err(invalid)?;
    crate::paths::ensure_safe_absolute(tool).map_err(invalid)?;
    let temporary = target.with_extension(format!(
        "{}.tmp",
        crate::paths::nonce().map_err(unavailable)?
    ));
    crate::paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
    let mut command = Command::new(tool);
    endpoint.apply(&mut command);
    let output = run(
        command
            .args([
                "--format",
                "custom",
                "--no-owner",
                "--no-privileges",
                "--dbname",
                &endpoint.database,
            ])
            .arg("--file")
            .arg(&temporary),
        BACKUP_TIMEOUT,
    );
    let output = match output {
        Ok(output) => output,
        Err(cause) => {
            let _ = fs::remove_file(&temporary);
            return Err(cause);
        }
    };
    if !output.status.success() {
        let _ = fs::remove_file(&temporary);
        return Err(unavailable(format!(
            "pg_dump 失败：{}",
            output.stderr.trim()
        )));
    }
    if fs::metadata(&temporary)?.len() == 0 {
        let _ = fs::remove_file(&temporary);
        return Err(unavailable("pg_dump 没有生成有效备份。"));
    }
    if target.try_exists()? {
        let _ = fs::remove_file(&temporary);
        return Err(error(409, "备份目标已存在。"));
    }
    fs::rename(temporary, target)?;
    Ok(())
}
#[cfg(feature = "postgres")]
fn restore(endpoint: &Endpoint, tool: &Path, source: &Path, owner: &str) -> Result<()> {
    crate::paths::ensure_safe_absolute(source).map_err(invalid)?;
    crate::paths::ensure_safe_absolute(tool).map_err(invalid)?;
    let mut command = Command::new(tool);
    endpoint.apply(&mut command);
    let output = run(
        command
            .args([
                "--dbname",
                &endpoint.database,
                "--clean",
                "--if-exists",
                "--exit-on-error",
                "--single-transaction",
                "--no-owner",
                "--no-privileges",
                "--role",
                owner,
            ])
            .arg(source),
        RESTORE_TIMEOUT,
    )?;
    if !output.status.success() {
        return Err(unavailable(format!(
            "pg_restore 失败，事务已回滚：{}",
            output.stderr.trim()
        )));
    }
    Ok(())
}
pub fn apply_pending(
    paths: &RuntimePaths,
    maintenance: &str,
    owner: &str,
    application: &str,
) -> Result<()> {
    #[cfg(not(feature = "postgres"))]
    {
        let _ = (paths, maintenance, owner, application);
        Err(unsupported("当前构建不包含 PostgreSQL。"))
    }
    #[cfg(feature = "postgres")]
    {
        let _gate = package::lock(paths)?;
        let physical_marker = restart_marker(paths)?;
        let migration_marker = migration::marker_path(paths)?;
        if physical_marker.try_exists()? && migration_marker.try_exists()? {
            return Err(invalid("发现相互冲突的恢复计划，已停止恢复。"));
        }
        let migrated = package::staged(&migration_marker, package::POSTGRES)?;
        let (marker, source, staging) = if let Some(staging) = migrated.as_ref() {
            (
                migration_marker,
                staging.join(package::POSTGRES),
                staging.clone(),
            )
        } else if physical_marker.try_exists()? {
            let value: Value = serde_json::from_slice(&fs::read(&physical_marker)?)
                .map_err(|_| invalid("恢复标记无效。"))?;
            let name = value["sourceFileName"]
                .as_str()
                .ok_or_else(|| invalid("恢复文件名缺失。"))?;
            let source = super::super::managed_file(&postgres_root(paths)?, name, "dump")?;
            let hash = super::super::sha256_hex(&fs::read(&source)?);
            if value["sha256"] != hash {
                return Err(invalid("备份文件已变化，恢复已拒绝。"));
            }
            let id = value["restoreId"]
                .as_str()
                .filter(|id| crate::paths::valid_file_name(id))
                .ok_or_else(|| invalid("恢复标识无效。"))?;
            (
                physical_marker,
                source,
                postgres_root(paths)?.join(format!("safety-{id}")),
            )
        } else {
            return Ok(());
        };
        let endpoint = Endpoint::parse_url(maintenance)?;
        let application = Endpoint::parse_url(application)?;
        if (
            endpoint.host.as_str(),
            endpoint.port.as_str(),
            endpoint.database.as_str(),
        ) != (
            application.host.as_str(),
            application.port.as_str(),
            application.database.as_str(),
        ) {
            return Err(invalid("维护连接与业务连接必须指向同一数据库。"));
        }
        let tools = Tools::resolve(paths);
        if !tools.ready() {
            return Err(unavailable("PostgreSQL 18 客户端工具缺失。"));
        }
        let mut lease = export_doc_storage::PostgresMaintenanceLease::acquire(maintenance, owner)?;
        package::private_directory(&staging)?;
        let safety = staging.join("before-restore.dump");
        if !safety.try_exists()? {
            dump(&endpoint, &tools.pg_dump, &safety)?;
        }
        // Recover an interrupted filesystem install before trying the same plan again.
        if staging.join("install-journal.json").try_exists()? {
            restore_files::rollback(paths, &staging)?;
            restore(&endpoint, &tools.pg_restore, &safety, owner)?;
            lease.finish(owner, &application.username)?;
        }
        let result = (|| {
            restore(&endpoint, &tools.pg_restore, &source, owner)?;
            lease.finish(owner, &application.username)?;
            if migrated.is_some() {
                restore_files::install(paths, &staging, false)?;
            }
            Ok(())
        })();
        if let Err(cause) = result {
            let rollback = restore(&endpoint, &tools.pg_restore, &safety, owner).and_then(|_| {
                lease
                    .finish(owner, &application.username)
                    .map_err(Into::into)
            });
            if let Err(rollback) = rollback {
                return Err(unavailable(format!(
                    "恢复和数据库回滚均失败：{cause}；{rollback}。保留安全备份并停止服务。"
                )));
            }
            return Err(cause);
        }
        fs::remove_file(marker)?;
        Ok(())
    }
}
