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
        "日常完整灾备",
        move |_| {
            let target = repository::root(&paths).join(format!(
                "{id}.{}",
                if pg { "edmmigration" } else { "edmrecovery" }
            ));
            let mut registered = false;
            let result: Result<tasks::TaskOutput> = (|| {
                let actor = auth::current_actor(&store, owner)?;
                administrator(&actor)?;
                {
                    let _package = package_lock;
                    let _templates = crate::engine::report_template_files::storage_lock(&paths)?;
                    if pg {
                        postgres::ensure_no_pending(&paths)?;
                    } else if super::super::disaster_root(&paths)?
                        .join(".pending-restore.json")
                        .try_exists()?
                    {
                        return Err(error(409, "已有灾备恢复等待处理。"));
                    }
                    let working = sealed::working_directory(&repository::root(&paths))?;
                    package::private_directory(&working)?;
                    let created: Result<()> = (|| {
                        let snapshot = working.join(if pg {
                            "database.dump"
                        } else {
                            "snapshot.sqlite3"
                        });
                        if let Some(tool) = &tool {
                            postgres::dump_to(&store, &protector, tool, &snapshot)?;
                        } else {
                            store.connection()?.backup(&snapshot)?;
                        }
                        let package = package::create(
                            &paths,
                            &protector,
                            &snapshot,
                            if pg {
                                package::POSTGRES
                            } else {
                                package::SQLITE
                            },
                            &working,
                            if pg {
                                migration::MAGIC
                            } else {
                                disaster::MAGIC
                            },
                            &password,
                        )?;
                        crate::operation::check()?;
                        fs::rename(package, &target)?;
                        Ok(())
                    })();
                    let cleanup = fs::remove_dir_all(&working);
                    created?;
                    cleanup?;
                }
                let mut output =
                    tasks::TaskOutput::managed_file(target.clone(), "日常完整灾备已保存。".into())?;
                let file = output.managed_file.as_ref().unwrap();
                let mut archive = repository::Archive {
                    id: id.clone(),
                    repository: saved.repository.clone(),
                    file_name: target.file_name().unwrap().to_string_lossy().into_owned(),
                    created_at: Utc::now(),
                    size_bytes: file.size,
                    sha256: file.digest.clone(),
                    replica_status: if saved.plan.replica_mode == "None" {
                        "NotRequested"
                    } else {
                        "Pending"
                    }
                    .into(),
                    replica_target: String::new(),
                    message: String::new(),
                    deleting: false,
                };
                repository::write(&paths, &archive)?;
                registered = true;
                let replica: Result<replica::Target> = (|| {
                    let replica = replica::Target::read(&store, &protector, &saved)?;
                    archive.replica_target = replica.identity();
                    // Register ownership before remote I/O, including failures and cancellation.
                    repository::write(&paths, &archive)?;
                    replica.upload(
                        &target,
                        &archive.file_name,
                        archive.size_bytes,
                        &archive.sha256,
                        saved.plan.verify_replica,
                    )?;
                    Ok(replica)
                })();
                let replica = match replica {
                    Ok(replica) => {
                        if saved.plan.replica_mode != "None" {
                            archive.replica_status = if saved.plan.verify_replica {
                                "Verified"
                            } else {
                                "Uploaded"
                            }
                            .into();
                        }
                        replica
                    }
                    Err(cause) => {
                        archive.replica_status = "Failed".into();
                        archive.message =
                            "本地备份已保存，异机副本未完成；本次未清理旧备份。".into();
                        output.detail = archive.message.clone();
                        crate::operation::OperationScope::new(Duration::from_secs(30))
                            .run(|| repository::write(&paths, &archive))?;
                        finish_state(
                            &store,
                            &id,
                            json!({"lastResult":"Partial", "lastError":cause.message}),
                        )?;
                        return Ok(output);
                    }
                };
                repository::write(&paths, &archive)?;
                match repository::prune(&paths, &saved.plan, &replica) {
                    Ok(()) => finish_state(
                        &store,
                        &id,
                        json!({"lastResult":"Succeeded", "lastError":""}),
                    )?,
                    Err(cause) => {
                        output.detail = "备份已完成，保留策略清理未完成；请检查灾备状态。".into();
                        finish_state(
                            &store,
                            &id,
                            json!({"lastResult":"Partial", "lastError":cause.message}),
                        )?;
                    }
                }
                Ok(output)
            })();
            if let Err(cause) = &result {
                if !registered && target.try_exists().unwrap_or(false) {
                    let _ = fs::remove_file(&target);
                }
                let _ = finish_state(
                    &store,
                    &id,
                    json!({"lastResult":"Failed", "lastError":cause.message}),
                );
            }
            result
        },
    )
}
