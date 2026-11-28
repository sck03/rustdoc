use super::super::repository::Commit;
use super::*;

pub(super) fn start(
    service: &NativeService,
    actor: &Actor,
    saved: &SavedPlan,
    id: String,
    password: zeroize::Zeroizing<String>,
    package_lock: fs::File,
) -> Result<Value> {
    let pg = service.provider()? == "PostgreSQL";
    let tool = if pg {
        Some(postgres::require_ready(service)?.pg_dump)
    } else {
        None
    };
    let (store, paths, protector, owner, saved) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
        saved.clone(),
    );
    service.jobs.start_backup(
        actor,
        RUN_BACKUP_PLAN,
        "ScheduledRecoveryBackup",
        "创建恢复点",
        move |_| {
            let _package = package_lock;
            let result = (|| {
                administrator(&auth::current_actor(&store, owner)?)?;
                let _templates = crate::engine::report_template_files::storage_lock(&paths)?;
                if pg {
                    postgres::ensure_no_pending(&paths)?;
                } else if super::super::disaster_root(&paths)?
                    .join(".pending-restore.json")
                    .try_exists()?
                {
                    return Err(error(409, "已有恢复等待维护处理。"));
                }
                let repository = points::open(&store, &paths, &protector, &saved, &password, true)?;
                let working = sealed::working_directory(&paths.cache_root)?;
                package::private_directory(&working)?;
                let capture = (|| {
                    let snapshot = working.join(if pg {
                        "database.dump"
                    } else {
                        "snapshot.sqlite3"
                    });
                    if let Some(tool) = &tool {
                        postgres::dump_to(&store, &protector, tool, &snapshot, &paths.disk_space)?;
                    } else {
                        super::super::snapshot::backup_sqlite(&store, &paths, &snapshot)?;
                    }
                    super::super::repository::capture::create(
                        &repository,
                        &store,
                        &paths,
                        &protector,
                        &snapshot,
                        if pg { "PostgreSQL" } else { "SQLite" },
                        &id,
                    )
                })();
                let cleanup = fs::remove_dir_all(&working).map_err(crate::api::ApiError::from);
                let commit = capture?;
                let retention =
                    repository.retain(saved.plan.retention_count, saved.plan.retention_days);
                complete(&store, &id, &commit, cleanup, retention.map(|_| ()))
            })();
            if let Err(cause) = &result {
                let _ = finish_state(&store, &id, failure_state(&id, cause));
            }
            result
        },
    )
}

fn failure_state(id: &str, cause: &crate::api::ApiError) -> Value {
    let mut patch = json!({
        "lastResult": if cause.partially_committed { "Partial" } else if cause.outcome_unknown { "Unknown" } else { "Failed" },
        "lastError": cause.public_body(id)["message"],
    });
    if cause.partially_committed || cause.outcome_unknown {
        patch["lastPointId"] = json!(id);
    }
    patch
}

fn complete(
    store: &Store,
    id: &str,
    commit: &Commit,
    cleanup: Result<()>,
    retention: Result<()>,
) -> Result<tasks::TaskOutput> {
    let warnings: Vec<_> = [("临时快照清理", cleanup), ("保留策略", retention)]
        .into_iter()
        .filter_map(|(stage, result)| {
            result.err().map(|cause| {
                format!(
                    "{stage}未完成：{}",
                    cause.public_body(id)["message"]
                        .as_str()
                        .unwrap_or("请查询任务回执。")
                )
            })
        })
        .collect();
    let warning = warnings.join("；");
    finish_state(
        store,
        id,
        json!({
            "lastResult": if warnings.is_empty() { "Succeeded" } else { "Partial" },
            "lastError": warning, "lastPointId": commit.id, "repositoryId": commit.repository,
            "logicalBytes": commit.size_bytes, "newBytes": commit.new_bytes,
        }),
    )
    .map_err(|mut cause| {
        let reason = cause.public_body(id)["message"]
            .as_str()
            .unwrap_or("请查询任务回执。")
            .to_owned();
        cause.partially_committed = true;
        cause.code = Some(crate::api::ErrorCode::PartialCommit);
        cause.message = format!(
            "恢复点 {} 已提交并核验，状态记录未完成：{reason} {warning} 请刷新恢复点列表核对结果。",
            commit.id
        );
        cause
    })?;
    Ok(tasks::TaskOutput {
        committed: true,
        file: None,
        managed_file: None,
        destination: None,
        directory: None,
        publication: None,
        archive: None,
        detail: if warnings.is_empty() {
            format!(
                "恢复点 {} 已提交并核验：{} 个文件，新增 {} 字节。",
                commit.id, commit.files, commit.new_bytes
            )
        } else {
            format!(
                "恢复点 {} 已提交并核验；{warning} 请检查灾备状态后处理，无需重复创建恢复点。",
                commit.id
            )
        },
    })
}

#[cfg(test)]
#[path = "tests/run.rs"]
mod tests;
