//! Select an owned archive by ID; no browser round trip and no arbitrary server path.
use super::*;
pub(in crate::engine) fn stage_restore(
    service: &NativeService,
    actor: &Actor,
    id: &str,
    body: &Value,
) -> Result<Value> {
    administrator(actor)?;
    export_doc_contracts::validation::structure(
        contracts::request(STAGE_RECOVERY_ARCHIVE_RESTORE.id),
        body,
    )
    .map_err(invalid)?;
    let pg = service.provider()? == "PostgreSQL";
    if body["confirmationText"] != if pg { "MIGRATE" } else { "RECOVER" } {
        return Err(invalid("恢复确认文本不匹配。"));
    }
    if pg {
        postgres::require_ready(service)?;
        super::super::verify_admin_password(
            &service.store,
            actor,
            body["adminPassword"].as_str().unwrap_or(""),
        )?;
    }
    let password = zeroize::Zeroizing::new(body["password"].as_str().unwrap_or("").to_owned());
    validate_package_password(&password)?;
    let archive = repository::read(&service.paths, id)?;
    if archive.deleting {
        return Err(error(404, "此备份已清理。"));
    }
    let (store, paths, owner) = (service.store.clone(), service.paths.clone(), actor.id);
    service.jobs.start_backup(
        actor,
        STAGE_RECOVERY_ARCHIVE_RESTORE,
        "RecoveryArchiveRestore",
        "验证并暂存完整恢复",
        move |_| {
            let actor = auth::current_actor(&store, owner)?;
            administrator(&actor)?;
            let _gate = package::lock(&paths)?;
            if pg {
                postgres::ensure_no_pending(&paths)?;
            }
            let source = repository::root(&paths).join(&archive.file_name);
            if crate::engine::backup_io::digest_file(&source)?
                != (archive.size_bytes, archive.sha256)
            {
                return Err(unavailable("备份文件已变化，已拒绝恢复。"));
            }
            package::stage(
                &if pg {
                    migration::marker_path(&paths)?
                } else {
                    disaster::marker(&paths)?
                },
                if pg {
                    migration::MAGIC
                } else {
                    disaster::MAGIC
                },
                &password,
                fs::File::open(&source)?,
                if pg {
                    package::POSTGRES
                } else {
                    package::SQLITE
                },
            )?;
            super::super::audit(
                &store,
                &actor,
                "stage-recovery-archive",
                "已校验并暂存完整恢复，等待维护窗口执行。",
            )?;
            Ok(tasks::TaskOutput {
                managed_file: None,
                publication: None,
                archive: None,
                file: None,
                destination: None,
                directory: None,
                detail: if pg {
                    "完整恢复已暂存。请停止应用，执行 --restore-pending 维护命令后启动。"
                } else {
                    "完整恢复已暂存。请退出并重新打开桌面程序。"
                }
                .into(),
            })
        },
    )
}
