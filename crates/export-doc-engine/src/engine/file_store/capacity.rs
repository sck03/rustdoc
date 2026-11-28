//! Cached counters and bounded daily history, sampled by the existing scheduler.
//! Remote total/free capacity remains unknown unless the provider reports it.
use super::*;
use crate::{
    engine::{
        NativeService, auth,
        store::{Actor, Store},
    },
    generated_api::*,
};
use serde_json::{Value, json};
const POLICY: &str = "storage-capacity-policy";
const STATE: &str = "storage-capacity-state";
fn default_policy() -> Value {
    json!({"warningPercent":85,"criticalPercent":95,"minimumFreeBytes":1024*1024*1024u64,"growthDays":14})
}
fn default_settings() -> Value {
    json!({"revision":0,"policy":default_policy()})
}
fn disk(area: &mut Value, path: &Path, policy: &Value) -> Result<()> {
    paths::ensure_safe_absolute(path).map_err(invalid)?;
    let total = fs2::total_space(path)?;
    let available = fs2::available_space(path)?;
    area["availableBytes"] = json!(available);
    area["totalBytes"] = json!(total);
    let used = if total == 0 {
        0
    } else {
        100u64.saturating_sub(available.saturating_mul(100) / total)
    };
    let state = if used >= policy["criticalPercent"].as_u64().unwrap_or(95)
        || available < policy["minimumFreeBytes"].as_u64().unwrap_or(0)
    {
        "Critical"
    } else if used >= policy["warningPercent"].as_u64().unwrap_or(85) {
        "Warning"
    } else {
        "Normal"
    };
    area["state"] = json!(state);
    area["message"] = json!(match state {
        "Critical" => "可用空间已低于容量策略，请扩容或预检可回收内容。",
        "Warning" => "容量接近阈值，请检查近期增长并安排扩容。",
        _ => "容量正常。",
    });
    Ok(())
}
pub(in crate::engine) fn observe(service: &NativeService, force: bool) -> Result<()> {
    let _sampling = service
        .capacity_sampling
        .write(std::time::Duration::from_secs(30))?;
    observe_locked(service, force)
}
fn observe_locked(service: &NativeService, force: bool) -> Result<()> {
    crate::operation::check()?;
    let previous = service.store.settings(STATE)?.unwrap_or_else(|| json!({}));
    let saved = service
        .store
        .settings(POLICY)?
        .unwrap_or_else(default_settings);
    let now = chrono::Utc::now();
    if !force
        && previous["policyRevision"] == saved["revision"]
        && previous["updatedAt"]
            .as_str()
            .and_then(|v| chrono::DateTime::parse_from_rfc3339(v).ok())
            .is_some_and(|v| now - v.with_timezone(&chrono::Utc) < chrono::Duration::minutes(15))
    {
        return Ok(());
    }
    let policy = &saved["policy"];
    let config = service.store.files.configuration()?;
    let stats = service.store.connection()?.file_statistics()?;
    let mut runtime =
        json!({"id":"Runtime","name":"A 运行盘","state":"Unknown","message":"容量未知。"});
    if let Err(cause) = disk(&mut runtime, &service.paths.data_root, policy) {
        runtime["state"] = json!("Unavailable");
        runtime["message"] = json!(cause.public_body("capacity-runtime")["message"]);
    }
    let mut files = json!({"id":"Files","name":"生产文件库","physicalBytes":stats["physicalBytes"],"logicalBytes":stats["logicalBytes"],"reclaimableBytes":stats["unreferencedBytes"],"protectedBytes":stats["protectedBytes"],"state":"Unknown","message":"远端总容量与剩余容量未知；文件占用来自业务计数。"});
    if let Backend::Local { .. } = &config.backend {
        let result = service
            .store
            .files
            .check_identity(&config)
            .and_then(|_| service.store.files.local_root(&config))
            .and_then(|root| disk(&mut files, &root, policy));
        if let Err(cause) = result {
            files["state"] = json!("Unavailable");
            files["message"] = json!(cause.public_body("capacity-files")["message"]);
        }
    }
    let plan = service
        .store
        .settings("scheduled-recovery-plan")?
        .unwrap_or_else(|| json!({}));
    let backup = service
        .store
        .settings("scheduled-recovery-state")?
        .unwrap_or_else(|| json!({}));
    let mut target = json!({"id":"Backup","name":"恢复仓库","state":"Unknown","message":if plan["plan"]["enabled"]==true{"目标总容量未知；最近成功恢复点及核验结果见日常灾备。"}else{"自动备份未启用。"}});
    if let Some(size) = backup["logicalBytes"].as_u64() {
        target["logicalBytes"] = json!(size);
    }
    let directory = match plan["plan"]["replicaMode"].as_str() {
        Some("None") => Some(service.paths.data_root.clone()),
        Some("Directory") => service
            .paths
            .file_storage_roots
            .get(plan["plan"]["backupLocation"].as_str().unwrap_or(""))
            .cloned(),
        _ => None,
    };
    if let Some(directory) = directory {
        if let Err(cause) = disk(&mut target, &directory, policy) {
            target["state"] = json!("Unavailable");
            target["message"] = json!(cause.public_body("capacity-backup")["message"]);
        }
    }
    if let (Some(total), Some(free)) = (
        runtime["totalBytes"].as_u64(),
        runtime["availableBytes"].as_u64(),
    ) {
        runtime["physicalBytes"] = json!(total.saturating_sub(free));
    }
    let areas = vec![runtime, files, target];
    crate::operation::check()?;
    service.store.transaction(|tx|{
        if tx.settings(POLICY)?.unwrap_or_else(default_settings) != saved {
            return Err(conflict("容量策略在采样期间变化，请刷新容量状态。"));
        }
        let previous = tx.settings(STATE)?.unwrap_or_else(|| json!({}));
        let today=crate::engine::units::today(tx)?;let oldest=(today-chrono::Duration::days(365)).to_string();
        let mut alerts=previous["alerts"].as_array().cloned().unwrap_or_default();
        for area in &areas{
            if let Some(physical)=area["physicalBytes"].as_u64(){tx.sample_capacity(&today.to_string(),area["id"].as_str().unwrap(),physical,area["logicalBytes"].as_u64().unwrap_or(0),area["reclaimableBytes"].as_u64().unwrap_or(0),&oldest)?;}
            let old=previous["areas"].as_array().and_then(|a|a.iter().find(|a|a["id"]==area["id"]));
            if old.is_some_and(|a|a["state"]!=area["state"])||old.is_none()&&matches!(area["state"].as_str(),Some("Warning"|"Critical"|"Unavailable")){
                let alert=json!({"area":area["id"],"state":area["state"],"message":area["message"],"at":now.to_rfc3339()});
                crate::engine::communication::capacity_alert(tx,service.store.edition,&alert)?;alerts.push(alert);
            }
        }
        if alerts.len()>100{alerts.drain(..alerts.len()-100);}
        let revision = previous["revision"].as_i64().unwrap_or(0) + 1;
        tx.set_settings(STATE,revision,&json!({"revision":revision,"policyRevision":saved["revision"],"updatedAt":now.to_rfc3339(),"areas":areas,"alerts":alerts}))?;Ok(())
    })
}
pub(in crate::engine) fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    if !actor.admin {
        return Err(crate::engine::error::error(
            403,
            "容量与保留设置仅限系统管理员。",
        ));
    }
    let _sampling = service
        .capacity_sampling
        .write(std::time::Duration::from_secs(30))?;
    if operation == SAVE_STORAGE_CAPACITY_POLICY {
        let policy = &body["policy"];
        if policy["warningPercent"].as_u64() >= policy["criticalPercent"].as_u64() {
            return Err(invalid("严重阈值必须高于预警阈值。"));
        }
        return service.store.transaction_as(actor, |tx, actor| {
            auth::authorize_operation(actor, operation, &[])?;
            let current = tx
                .settings(POLICY)?
                .unwrap_or_else(|| json!({"revision":0}));
            if current["revision"] != body["expectedVersion"] {
                return Err(conflict("容量策略已变化，请刷新。"));
            }
            let revision = current["revision"].as_i64().unwrap_or(0) + 1;
            let saved = json!({"revision":revision,"policy":policy});
            tx.set_settings(POLICY, revision, &saved)?;
            Ok(saved)
        });
    }
    observe_locked(service, false)?;
    status(&service.store)
}
fn status(store: &Store) -> Result<Value> {
    let saved = store.settings(POLICY)?.unwrap_or_else(default_settings);
    let state = store.settings(STATE)?.unwrap_or_else(|| json!({}));
    store.read(|tx|{let since=(crate::engine::units::today(tx)?-chrono::Duration::days(saved["policy"]["growthDays"].as_i64().unwrap_or(14))).to_string();Ok(json!({"revision":saved["revision"],"policy":saved["policy"],"areas":state["areas"],"alerts":state["alerts"],"updatedAt":state["updatedAt"],"samples":tx.capacity_history(&since)?}))})
}
