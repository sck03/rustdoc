//! Explicit, previewed snapshot cleanup; current content and business audit remain intact.
use super::*;
use crate::engine::store::Connection;
use crate::engine::{error::conflict, import_previews, records::required};
use export_doc_contracts::template_history as limits;
use export_doc_storage::{AuditWrite, TemplateVersionQuery};

fn keep(value: Option<i64>) -> Result<i64> {
    value
        .filter(|value| (limits::MIN_RETAIN..=limits::MAX_RETAIN).contains(value))
        .ok_or_else(|| invalid("历史保留数量须在 5–200 版之间。"))
}
fn template(tx: &Connection, actor: &Actor, id: i64, write: bool) -> Result<Value> {
    let template = tx
        .report_template_metadata(id)?
        .ok_or_else(|| error(404, "报表模板不存在。"))?;
    visible(actor, &template)?;
    if write && !policy::can_manage(actor, &template, "archive") {
        return Err(error(
            403,
            "只有模板所有者或管理员可以清理历史，并须有归档权限。",
        ));
    }
    Ok(template)
}
fn selection(id: i64, retain: i64) -> TemplateVersionQuery<'static> {
    TemplateVersionQuery {
        kind: KIND,
        template_id: id,
        offset: retain,
        limit: limits::MAX_CLEANUP,
    }
}
fn stats(tx: &Connection, actor: &Actor, template: &Value, retain: i64) -> Result<Value> {
    let usage = tx.template_history_usage(
        &selection(template["id"].as_i64().unwrap_or(0), retain),
        retain,
    )?;
    Ok(
        json!({"totalCount":usage.count,"totalBytes":usage.bytes,"eligibleCount":usage.eligible_count,
        "eligibleBytes":usage.eligible_bytes,"retainLatest":retain,"maxCleanupCount":limits::MAX_CLEANUP,
        "canClean":policy::can_manage(actor,template,"archive"),
        "policy":"历史不会自动删除。按实际快照顺序保留最近指定数量，至少 5 版；每次最多清理 5000 版。当前模板、业务审计和仍被引用的图片保留。容量为快照正文估算，释放的数据库页面可复用，不自动压缩数据库。"}),
    )
}

pub(super) fn handle(
    service: &crate::engine::NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    queries: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let id = super::super::records::id(parameters)?;
    if operation == GET_USER_REPORT_TEMPLATE_HISTORY_RETENTION {
        let requested = query(queries, "retainLatest");
        let retain = keep(if requested.is_empty() {
            Some(limits::DEFAULT_RETAIN)
        } else {
            requested.parse().ok()
        })?;
        return service
            .store
            .read_for(actor, operation, queries, |tx, actor| {
                stats(tx, actor, &template(tx, actor, id, false)?, retain)
            });
    }
    if operation == PREVIEW_USER_REPORT_TEMPLATE_HISTORY_CLEANUP {
        let retain = keep(if body["retainLatest"].is_null() {
            Some(limits::DEFAULT_RETAIN)
        } else {
            body["retainLatest"].as_i64()
        })?;
        let (current, retention, items) =
            service
                .store
                .read_for(actor, operation, queries, |tx, actor| {
                    let current = template(tx, actor, id, true)?;
                    let retention = stats(tx, actor, &current, retain)?;
                    let items = tx.template_history_candidates(&selection(id, retain))?;
                    Ok((current, retention, items))
                })?;
        let plan = json!({"templateId":id,"templateVersion":current["versionNumber"],"retainLatest":retain,"items":items});
        let prepared = import_previews::prepare("report-history-cleanup", &[plan])?;
        let token = service
            .store
            .transaction_for(actor, operation, queries, |tx, actor| {
                store::check_version(&template(tx, actor, id, true)?, store::expected(&current))?;
                import_previews::save_prepared(tx, actor, &service.clock, &prepared)
            })?;
        return Ok(
            json!({"token":token,"templateVersion":current["versionNumber"],"retention":retention,"items":items}),
        );
    }
    required(body, "reason", "清理原因", 1000)?;
    let _access = super::super::report_template_files::storage_lock(&service.paths)?;
    service.store.transaction_for(actor, operation, queries, |tx, actor| {
        let current = template(tx, actor, id, true)?;
        let plans = import_previews::consume(tx, actor, &service.clock, "report-history-cleanup", &text(body, "token"))?;
        let plan = plans.first().filter(|_| plans.len() == 1).filter(|plan| plan["templateId"] == id)
            .ok_or_else(|| conflict("清理预检与当前模板不一致，请重新预检。"))?;
        store::check_version(&current, plan["templateVersion"].as_i64().unwrap_or(0))?;
        let retain = keep(plan["retainLatest"].as_i64())?;
        let items = tx.template_history_candidates(&selection(id, retain))?;
        if json!(items) != plan["items"] { return Err(conflict("历史在预检后已变化，请重新预检。")); }
        for item in &items {
            crate::operation::check()?;
            if item["versionNumber"] == current["versionNumber"] { return Err(conflict("不能清理当前模板的快照。")); }
            let record = super::super::records::positive(item, "id", "历史快照")?;
            let version = super::super::records::positive(item, "recordVersion", "历史记录版本")?;
            tx.delete_blobs(record)?;
            if !tx.delete("template-versions", record, version)? { return Err(conflict("历史版本已变化，清理已取消。")); }
        }
        if !items.is_empty() {
            tx.append_audit_details(&AuditWrite { kind: KIND, record_id: id, version: store::expected(&current),
                action: "history-cleanup", actor_id: actor.id, occurred_at: &store::timestamp(), note: &text(body,"reason") },
                &json!({"removedVersions":items.iter().map(|item| item["versionNumber"].clone()).collect::<Vec<_>>(),"retainLatest":retain}))?;
        }
        Ok(json!({"success":true,"message":format!("已清理 {} 版旧快照，保留最近 {retain} 版及当前模板；审计与有效图片引用保留。",items.len())}))
    })
}
