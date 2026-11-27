//! Opt-in complete recovery packages, using the same format as one-time migration.
use super::*;
use chrono::{DateTime, Utc};
use export_doc_domain::backup_plan::BackupPlan;
use serde::{Deserialize, Serialize};
use std::fs;
mod configuration;
mod maintenance;
pub(in crate::engine) use maintenance::{maintain, maintenance_status};
mod points;
mod restore;
mod run;
mod save_archive;
mod scheduler;
#[cfg(test)]
mod tests;
pub(in crate::engine) use configuration::{save, status};
pub(in crate::engine) use restore::stage_restore;
pub(in crate::engine) use save_archive::save_to_path;
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
    #[serde(default)]
    writer_identity: String,
    #[serde(default)]
    medium_identity: String,
    plan: BackupPlan,
}
fn load(store: &Store) -> Result<SavedPlan> {
    let saved: SavedPlan = store
        .settings(PLAN)?
        .map(serde_json::from_value)
        .transpose()
        .map_err(|_| {
            unavailable("日常灾备配置损坏。").coded(crate::api::ErrorCode::DependencyUnavailable)
        })?
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
        return Err(unavailable("日常灾备状态记录损坏。")
            .coded(crate::api::ErrorCode::DependencyUnavailable));
    }
    Ok(value)
}
fn update_state(store: &Store, id: Option<&str>, patch: Value) -> Result<()> {
    store.transaction(|tx| {
        let mut current = tx.settings(STATE)?.unwrap_or_else(|| json!({}));
        if !current.is_object() {
            return Err(unavailable("日常灾备状态记录损坏。")
                .coded(crate::api::ErrorCode::DependencyUnavailable));
        }
        if id.is_some_and(|id| current["archiveId"] != id) {
            return Ok(());
        }
        let revision = current["revision"]
            .as_i64()
            .unwrap_or(0)
            .checked_add(1)
            .ok_or_else(|| {
                unavailable("灾备状态版本超出范围。")
                    .coded(crate::api::ErrorCode::DependencyUnavailable)
            })?;
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
    crate::operation::OperationScope::detached(Duration::from_secs(30))
        .run(|| update_state(store, Some(id), patch))
}
pub(in crate::engine) fn run_now(service: &NativeService, actor: &Actor) -> Result<Value> {
    administrator(actor)?;
    service
        .backup_scheduler
        .run(service, Some(actor), Utc::now())?
        .ok_or_else(|| {
            unavailable("灾备任务未启动。").coded(crate::api::ErrorCode::DependencyUnavailable)
        })
}
pub(in crate::engine) fn test_replica(service: &NativeService, actor: &Actor) -> Result<Value> {
    administrator(actor)?;
    let saved = load(&service.store)?;
    if saved.password.is_empty() {
        return Err(invalid("请先保存恢复凭证。"));
    }
    let (store, paths, protector, owner) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
    );
    service.jobs.start(
        actor,
        TEST_BACKUP_REPLICA,
        "RecoveryRepositoryTest",
        "核验恢复仓库",
        move |_| {
            administrator(&auth::current_actor(&store, owner)?)?;
            let _gate = package::lock(&paths)?;
            let password = protector
                .unprotect(PASSWORD, &saved.password)
                .map_err(unavailable)?;
            let _repository = points::open(&store, &paths, &protector, &saved, &password, true)?;
            Ok(tasks::TaskOutput {
                file: None,
                managed_file: None,
                destination: None,
                directory: None,
                publication: None,
                archive: None,
                detail: "仓库身份、恢复凭证和所需存储能力已核验。此检查不替代恢复演练。".into(),
            })
        },
    )
}
pub(in crate::engine) fn archives(
    service: &NativeService,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    administrator(actor)?;
    points::page(service, query)
}
