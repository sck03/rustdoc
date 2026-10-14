use super::*;

fn webdav(settings: &Value) -> Value {
    json!({"enabled": settings["webDav"]["enabled"], "url": settings["webDav"]["url"], "userName": settings["webDav"]["userName"]})
}
fn retained_secret(
    service: &NativeService,
    name: &str,
    old: &str,
    value: &Value,
) -> Result<String> {
    match value.as_str() {
        None => Ok(old.into()),
        Some(value) => service.protector.protect(name, value).map_err(unavailable),
    }
}
pub(in crate::engine) fn status(service: &NativeService, actor: &Actor) -> Result<Value> {
    administrator(actor)?;
    let saved = load(&service.store)?;
    let mut state = state(&service.store)?;
    let settings = settings::current(&service.store)?;
    let protected = service
        .store
        .settings("protected-credentials")?
        .unwrap_or_else(|| json!({}));
    let mut running = state["runningJobId"].as_str().map(str::to_owned);
    if let Some(id) = &running {
        match service.jobs.get(actor, id) {
            Ok(job)
                if matches!(
                    job["status"].as_str(),
                    Some("Succeeded" | "Failed" | "Canceled")
                ) =>
            {
                if job["status"] != "Succeeded" && state["lastResult"] == "Running" {
                    state["lastResult"] = job["status"].clone();
                    state["lastError"] = job["errorMessage"].clone();
                }
                running = None;
            }
            Err(cause) if cause.status == Some(404) => {
                running = None;
                if state["lastResult"] == "Running" {
                    state["lastResult"] = json!("Interrupted");
                    state["lastError"] = json!("任务记录已不存在，请检查最近备份后重新执行。");
                }
            }
            Err(cause) => return Err(cause),
            _ => {}
        }
    }
    let archives = repository::list(&service.paths)?.into_iter().filter(|entry| !entry.deleting).take(20)
        .map(|entry| json!({"id": entry.id, "fileName": entry.file_name, "createdAt": entry.created_at,
            "sizeBytes": entry.size_bytes, "replicaStatus": entry.replica_status, "message": entry.message})).collect::<Vec<_>>();
    Ok(
        json!({"revision": saved.revision, "settingsRevision": settings["revision"], "plan": saved.plan,
        "webDav": webdav(&settings), "webDavPasswordSet": protected["/webDav/password"].as_str().is_some_and(|value| !value.is_empty()),
        "s3SecretAccessKeySet": !saved.s3_secret.is_empty(), "s3SessionTokenSet": !saved.s3_token.is_empty(), "s3Available": cfg!(feature = "s3-backup"),
        "encryptionPasswordSet": !saved.password.is_empty(), "timeZone": service.clock.now().map_err(invalid)?.time_zone,
        "nextRunAt": scheduler::next_run(service, &saved.plan)?, "runningJobId": running, "lastRunAt": state["lastRunAt"],
        "lastResult": state["lastResult"].as_str().unwrap_or("NotRun"), "lastError": state["lastError"].as_str().unwrap_or(""), "archives": archives}),
    )
}
pub(in crate::engine) fn save(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    administrator(actor)?;
    export_doc_contracts::validation::structure(contracts::request(SAVE_BACKUP_PLAN.id), body)
        .map_err(invalid)?;
    let plan: BackupPlan =
        serde_json::from_value(body["plan"].clone()).map_err(|_| invalid("日常灾备设置无效。"))?;
    plan.validate().map_err(invalid)?;
    match plan.replica_mode.as_str() {
        "WebDav" => cloud::validate_recovery_settings(&body["webDav"])?,
        "S3" => {
            #[cfg(feature = "s3-backup")]
            super::super::s3::validate(&plan.s3)?;
            #[cfg(not(feature = "s3-backup"))]
            return Err(unsupported("此构建未包含 S3 备份模块。"));
        }
        _ => {}
    }
    let password = body["encryptionPassword"].as_str().unwrap_or("");
    if !password.is_empty() {
        validate_package_password(password)?;
        if password.chars().count() < 12 {
            return Err(invalid("新灾备包密码至少需要 12 个字符。"));
        }
    }
    {
        let _gate = service
            .backup_scheduler
            .gate
            .lock()
            .map_err(|_| unavailable("日常灾备状态异常。"))?;
        service.store.transaction_as(actor, |tx, actor| {
            administrator(actor)?;
            let old: SavedPlan = tx.settings(PLAN)?.map(serde_json::from_value).transpose()
                .map_err(|_| unavailable("日常灾备设置损坏。"))?.unwrap_or_default();
            if body["expectedRevision"].as_i64() != Some(old.revision) {
                return Err(error(409, "日常灾备计划已被修改，请重新加载后核对。"));
            }
            let mut settings = settings::current_in(tx)?;
            let revision = settings["revision"].as_i64().unwrap_or(0);
            if body["expectedSettingsRevision"].as_i64() != Some(revision) {
                return Err(error(409, "系统连接配置已被修改，请重新加载后核对。"));
            }
            let password = if password.is_empty() { old.password } else { service.protector.protect(PASSWORD, password).map_err(unavailable)? };
            if plan.enabled && password.is_empty() { return Err(invalid("启用前请设置并单独保管灾备包密码。")); }
            if plan.enabled && std::env::var_os("EXPORTDOCMANAGER_MASTER_KEY").is_some() {
                return Err(invalid("环境主密钥部署请先配置独立密钥灾备，当前完整包需要文件主密钥。"));
            }
            let s3_secret = retained_secret(service, S3_SECRET, &old.s3_secret, &body["s3SecretAccessKey"])?;
            let s3_token = retained_secret(service, S3_TOKEN, &old.s3_token, &body["s3SessionToken"])?;
            if plan.replica_mode == "S3" && s3_secret.is_empty() { return Err(invalid("请填写 S3 密钥。")); }
            let mut protected = tx.settings("protected-credentials")?.unwrap_or_else(|| json!({}));
            if !protected.is_object() { return Err(unavailable("加密凭证存储损坏。")); }
            let old_webdav = webdav(&settings);
            for key in ["enabled", "url", "userName"] { settings["webDav"][key] = body["webDav"][key].clone(); }
            if !body["webDavPassword"].is_null() {
                protected["/webDav/password"] = json!(retained_secret(service, "/webDav/password", "", &body["webDavPassword"])?);
                tx.set_settings("protected-credentials", revision + 1, &protected)?;
            }
            if old_webdav != body["webDav"] || !body["webDavPassword"].is_null() {
                settings["revision"] = json!(revision + 1);
                tx.set_settings("settings", revision + 1, &settings)?;
            }
            let repository = repository::ensure(&service.paths)?;
            let saved = SavedPlan { revision: old.revision + 1, owner: actor.id, password, s3_secret, s3_token, repository, plan: plan.clone() };
            tx.set_settings(PLAN, saved.revision, &serde_json::to_value(&saved)?)?;
            tx.append_audit_details(&AuditWrite { kind: "backup-plan", record_id: 0, version: saved.revision, action: "configure", actor_id: actor.id,
                occurred_at: &store::timestamp(), note: "" }, &json!({"enabled": plan.enabled, "frequency": plan.frequency, "replicaMode": plan.replica_mode}))?;
            Ok(())
        })?;
    }
    service.backup_scheduler.refresh(service)?;
    status(service, actor)
}
