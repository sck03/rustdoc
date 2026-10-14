//! Opt-in complete recovery packages, using the same format as one-time migration.
use super::*;
use chrono::{DateTime, Utc};
use export_doc_domain::backup_plan::BackupPlan;
use serde::{Deserialize, Serialize};
mod configuration;
mod replica;
mod repository;
mod restore;
mod run;
mod scheduler;
#[cfg(test)]
mod tests;
pub(in crate::engine) use configuration::{save, status};
pub(in crate::engine) use restore::stage_restore;
pub(in crate::engine) use scheduler::Scheduler;
const PLAN: &str = "scheduled-recovery-plan";
const STATE: &str = "scheduled-recovery-state";
const PASSWORD: &str = "scheduled-recovery-password";
const S3_SECRET: &str = "scheduled-recovery-s3-secret";
const S3_TOKEN: &str = "scheduled-recovery-s3-token";

#[derive(Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct SavedPlan {
    revision: i64,
    owner: i64,
    password: String,
    s3_secret: String,
    s3_token: String,
    repository: String,
    plan: BackupPlan,
}
fn load(store: &Store) -> Result<SavedPlan> {
    let saved: SavedPlan = store
        .settings(PLAN)?
        .map(serde_json::from_value)
        .transpose()
        .map_err(|_| unavailable("日常灾备配置损坏。"))?
        .unwrap_or_default();
    saved.plan.validate().map_err(invalid)?;
    Ok(saved)
}
fn administrator(actor: &Actor) -> Result<()> {
    if !actor.admin {
        return Err(error(403, "只有系统管理员可以管理完整灾备。"));
    }
    auth::authorize(actor, "system.backup", "manage")?;
    auth::authorize(actor, "system.disaster-recovery", "manage")
}
fn state(store: &Store) -> Result<Value> {
    let value = store.settings(STATE)?.unwrap_or_else(|| json!({}));
    if !value.is_object() {
        return Err(unavailable("日常灾备状态记录损坏。"));
    }
    Ok(value)
}
fn update_state(store: &Store, id: Option<&str>, patch: Value) -> Result<()> {
    store.transaction(|tx| {
        let mut current = tx.settings(STATE)?.unwrap_or_else(|| json!({}));
        if !current.is_object() {
            return Err(unavailable("日常灾备状态记录损坏。"));
        }
        if id.is_some_and(|id| current["archiveId"] != id) {
            return Ok(());
        }
        let revision = current["revision"]
            .as_i64()
            .unwrap_or(0)
            .checked_add(1)
            .ok_or_else(|| unavailable("灾备状态版本超出范围。"))?;
        for (key, value) in patch.as_object().ok_or_else(|| invalid("备份状态无效。"))? {
            current[key] = value.clone();
        }
        current["revision"] = json!(revision);
        tx.set_settings(STATE, revision, &current)?;
        Ok(())
    })
}
fn finish_state(store: &Store, id: &str, patch: Value) -> Result<()> {
    // Terminal status must remain observable after cancellation.
    crate::operation::OperationScope::new(Duration::from_secs(30))
        .run(|| update_state(store, Some(id), patch))
}
pub(in crate::engine) fn run_now(service: &NativeService, actor: &Actor) -> Result<Value> {
    administrator(actor)?;
    service
        .backup_scheduler
        .run(service, Some(actor), Utc::now())?
        .ok_or_else(|| unavailable("灾备任务未启动。"))
}
pub(in crate::engine) fn test_replica(service: &NativeService, actor: &Actor) -> Result<Value> {
    administrator(actor)?;
    let saved = load(&service.store)?;
    if saved.plan.replica_mode == "None" {
        return Err(invalid("请先选择并保存异机副本存储。"));
    }
    let (store, protector, paths, owner) = (
        service.store.clone(),
        service.protector.clone(),
        service.paths.clone(),
        actor.id,
    );
    service.jobs.start(
        actor,
        "BackupReplicaTest",
        "验证异机副本存储",
        move |_| {
            administrator(&auth::current_actor(&store, owner)?)?;
            let target = replica::Target::read(&store, &protector, &saved)?;
            let directory = paths.cache_root.join(format!(
                "replica-test-{}",
                crate::paths::nonce().map_err(unavailable)?
            ));
            package::private_directory(&directory)?;
            let name = format!(
                "connection-test-{}.bin",
                crate::paths::nonce().map_err(unavailable)?
            );
            let path = directory.join(&name);
            let result: Result<tasks::TaskOutput> = (|| {
                let bytes = b"ExportDocManager backup storage write/read/delete verification\n";
                sealed::atomic_write(&path, bytes)?;
                let (size, digest) = super::super::backup_io::digest_file(&path)?;
                let uploaded = target.upload(&path, &name, size, &digest, true);
                let cleanup = crate::operation::OperationScope::new(Duration::from_secs(30))
                    .run(|| replica::Target::read(&store, &protector, &saved)?.delete(&name));
                uploaded?;
                cleanup?;
                Ok(tasks::TaskOutput {
                    managed_file: None,
                    file: None,
                    detail: "存储写入、回读校验和删除均成功。".into(),
                    destination: None,
                    directory: None,
                })
            })();
            let cleanup = fs::remove_dir_all(&directory);
            let output = result?;
            cleanup?;
            Ok(output)
        },
    )
}
pub(in crate::engine) fn ticket(service: &NativeService, actor: &Actor, id: &str) -> Result<Value> {
    administrator(actor)?;
    let archive = repository::read(&service.paths, id)?;
    if archive.deleting {
        return Err(error(404, "此备份已清理。"));
    }
    let mut ticket = super::issue_download_ticket(id.into())?;
    ticket["downloadUrl"] = json!(
        DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET
            .path
            .replace("{token}", ticket["token"].as_str().unwrap())
    );
    Ok(ticket)
}
pub(in crate::engine) fn download(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<crate::engine::backup_io::Download> {
    administrator(actor)?;
    let id = super::consume_download_ticket(super::header(parameters, "token"))?;
    repository::download(&service.paths, &id)
}
