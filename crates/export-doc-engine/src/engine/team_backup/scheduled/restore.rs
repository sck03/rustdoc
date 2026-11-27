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
    super::super::repository::valid_id(id)?;
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
    let options = body.clone();
    let (store, paths, protector, owner, id, saved) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
        id.to_owned(),
        load(&service.store)?,
    );
    service.jobs.start_backup(
        actor,
        STAGE_RECOVERY_ARCHIVE_RESTORE,
        "RecoveryArchiveRestore",
        "核验恢复点与目标文件库",
        move |_| {
            administrator(&auth::current_actor(&store, owner)?)?;
            let gate = package::lock(&paths)?;
            if pg {
                postgres::ensure_no_pending(&paths)?;
            }
            let repo = points::open(&store, &paths, &protector, &saved, &password, false)?;
            let point = repo.commit(&id)?;
            if (point.provider == "PostgreSQL") != pg {
                return Err(invalid("恢复点与当前数据库类型不匹配。"));
            }
            let marker = if pg {
                migration::marker_path(&paths)?
            } else {
                disaster::marker(&paths)?
            };
            let prepared =
                super::super::repository::restore::prepare(&paths, &repo, &id, &marker, &options)?;
            if pg {
                prepared.verify_postgres(&paths)?;
            }
            Ok(prepared.into_task(
                gate,
                "stage-recovery-point",
                if pg {
                    "恢复点与目标文件库已核验。请停止业务服务，在维护模式完成数据库与配置切换。"
                } else {
                    "恢复点已核验并暂存，请退出并重新打开桌面程序完成恢复。"
                },
            ))
        },
    )
}
