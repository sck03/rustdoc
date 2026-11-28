//! Explicit orphan collection plans bind candidate objects to one library revision.
use super::*;
use crate::engine::{NativeService, auth, store::Actor};
use serde_json::{Value, json};
const PLAN: &str = "file-collection-plan";

pub(in crate::engine) fn preview(service: &NativeService, actor: &Actor) -> Result<Value> {
    let _maintenance = super::admin::maintenance_lock(&service.store.files)?;
    let _backup = crate::engine::team_backup::package::lock(&service.paths)?;
    let config = service.store.files.configuration()?;
    service.store.files.check_identity(&config)?;
    let previous = service
        .store
        .settings("file-collection-cursor")?
        .unwrap_or_default();
    let mut cursor = if previous["identity"] == config.identity {
        previous["after"].as_str().unwrap_or("").to_owned()
    } else {
        String::new()
    };
    let mut candidates = std::collections::BTreeMap::new();
    let discovered = service
        .store
        .files
        .discover_intents(&service.store, &config)?;
    for object in discovered.orphans {
        candidates.insert(object.digest.clone(), object);
    }
    let mut scanned = 0;
    while scanned < 500 && candidates.len() < 1000 {
        crate::operation::check()?;
        let page = service.store.connection()?.file_objects(
            &cursor,
            (500 - scanned)
                .min(200)
                .min((1000 - candidates.len()) as i64),
            true,
        )?;
        if page.is_empty() {
            cursor.clear();
            break;
        }
        for object in page {
            scanned += 1;
            cursor = object.digest.clone();
            candidates.insert(object.digest.clone(), object);
        }
    }
    let mut objects = Vec::new();
    let mut bytes = 0u64;
    let mut waiting = 0u64;
    let root = service.paths.data_root.join("Configuration/FileCollection");
    crate::secrets::private_directory(&root).map_err(unavailable)?;
    for object in candidates.into_values() {
        crate::operation::check()?;
        let mark = root.join(format!("{}-{}.json", config.identity, object.digest));
        paths::ensure_safe_absolute(&mark).map_err(invalid)?;
        if !mark.try_exists()? {
            paths::atomic_write(&mark,&serde_json::to_vec(&json!({"identity":config.identity,"digest":object.digest,"markedAt":chrono::Utc::now().to_rfc3339()}))?).map_err(unavailable)?;
            waiting += 1;
            continue;
        }
        let record: Value = serde_json::from_reader(fs::File::open(mark)?.take(4096))?;
        let marked = record["markedAt"]
            .as_str()
            .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
            .ok_or_else(|| invalid("无引用文件隔离标记损坏，请核对维护记录。"))?;
        if record["digest"] != object.digest || record["identity"] != config.identity {
            return Err(invalid("无引用文件隔离身份不匹配。"));
        }
        if marked > chrono::Utc::now() - chrono::Duration::hours(24) {
            waiting += 1;
            continue;
        }
        bytes += object.size;
        objects.push(object);
    }
    let token = paths::nonce().map_err(unavailable)?;
    let archive_cleanup = discovered.copies;
    let archive_tokens: Vec<_> = archive_cleanup
        .iter()
        .map(|(token, _)| token.clone())
        .collect();
    let archive_bytes = archive_cleanup.iter().map(|(_, bytes)| bytes).sum::<u64>();
    let expires = (chrono::Utc::now() + chrono::Duration::minutes(15)).to_rfc3339();
    service.store.transaction_as(actor,|tx,actor|{if !actor.admin{return Err(crate::engine::error::error(403,"无引用回收仅限管理员。"));}tx.set_settings("file-collection-cursor",1,&json!({"identity":config.identity,"after":cursor}))?;tx.set_settings(PLAN,1,&json!({"token":token,"expiresAt":expires,"identity":config.identity,"revision":config.revision,"objects":objects,"archiveCleanup":archive_tokens,"uploadIntents":discovered.uploads,"owner":actor.id}))?;Ok(())})?;
    Ok(
        json!({"token":token,"expiresAt":expires,"count":objects.len()+archive_tokens.len(),"bytes":bytes+archive_bytes,"archiveCleanupCount":archive_tokens.len(),"waitingObjects":waiting,"message":"无引用对象须隔离至少 24 小时；归档副本须重新核验正式副本后清理。每轮扫描最多 1000 条操作记录和 500 个目录对象。"}),
    )
}
pub(super) fn execute(
    store: &crate::engine::store::Store,
    actor: &Actor,
    token: &str,
) -> Result<u64> {
    let actor = store.read(|tx| auth::refresh_actor_in(tx, actor))?;
    if !actor.admin {
        return Err(crate::engine::error::error(403, "管理员权限已撤销。"));
    }
    let plan = store
        .settings(PLAN)?
        .ok_or_else(|| invalid("请先预览回收计划。"))?;
    let expires = plan["expiresAt"]
        .as_str()
        .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
        .ok_or_else(|| invalid("回收计划期限无效。"))?;
    let config = store.files.configuration()?;
    if plan["token"] != token
        || plan["owner"] != actor.id
        || expires < chrono::Utc::now()
        || plan["identity"] != config.identity
        || plan["revision"] != config.revision
    {
        return Err(conflict("回收预检已过期或文件库/权限已变化，请重新预览。"));
    }
    let objects: Vec<FileObject> = serde_json::from_value(plan["objects"].clone())?;
    if objects.len() > 1000 {
        return Err(invalid("回收计划超出预算。"));
    }
    let digests = objects
        .iter()
        .map(|object| object.digest.clone())
        .collect::<Vec<_>>();
    for state in store.connection()?.file_object_states(&digests)? {
        if state.references > 0 {
            return Err(conflict("候选对象出现新引用，已停止本轮回收，请重新预览。"));
        }
    }
    let archive_tokens: Vec<String> = serde_json::from_value(plan["archiveCleanup"].clone())?;
    let upload_intents: Vec<(String, String)> =
        serde_json::from_value(plan["uploadIntents"].clone())?;
    if upload_intents.len() > 1000 {
        return Err(invalid("文件操作记录清单超过预算。"));
    }
    let mut progress = CollectionProgress::default();
    let result = (|| {
        store
            .files
            .reconcile_copies(store, &archive_tokens, &mut progress)?;
        let mut removed = std::collections::BTreeSet::new();
        for object in objects {
            crate::operation::check()?;
            if store
                .files
                .collect(store, &config, &object, &mut progress)?
            {
                removed.insert(object.digest.clone());
            }
        }
        progress.stage = "回收记录清理";
        store
            .files
            .finish_collection(&config, &removed, &upload_intents)?;
        for digest in &removed {
            let mark = store
                .files
                .configuration_path
                .parent()
                .unwrap()
                .join("FileCollection")
                .join(format!("{}-{digest}.json", config.identity));
            paths::ensure_safe_absolute(&mark).map_err(unavailable)?;
            if mark.try_exists()? {
                fs::remove_file(mark)?;
            }
        }
        progress.stage = "回收审计写入";
        store.transaction(|tx| {
            tx.append_audit_details(
                &export_doc_storage::AuditWrite {
                    kind: "file-collection",
                    record_id: 0,
                    version: config.revision as i64,
                    action: "collect",
                    actor_id: actor.id,
                    occurred_at: &crate::engine::store::timestamp(),
                    note: "",
                },
                &json!({"token":token,"bytes":progress.bytes,"objects":progress.objects}),
            )?;
            Ok(())
        })?;
        Ok(progress.bytes)
    })();
    result.map_err(|cause| progress.failure(cause))
}
