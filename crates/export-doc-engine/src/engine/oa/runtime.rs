//! Durable personnel execution and workflow reminders share task scheduling.
use super::*;
#[cfg(test)]
#[path = "runtime_tests.rs"]
mod tests;
pub(in crate::engine) fn tick(
    service: &NativeService,
    now: chrono::DateTime<chrono::Utc>,
) -> Result<()> {
    super::publication_schedule::tick(service, now)?;
    super::reservation_schedule::tick(service)?;
    let orders = service
        .store
        .read(|tx| Ok(tx.due_personnel_orders(&now.to_rfc3339())?))?;
    for order in orders {
        crate::operation::check()?;
        let id = records::positive(&order, "requestId", "定时变动")?;
        let result = service.store.transaction(|tx| {
            if tx
                .personnel_order(id)?
                .is_none_or(|o| o["state"] != "Pending")
            {
                return Ok(());
            }
            let row = store::get(tx, "oa-personnel", id)?;
            if approval::plan(&row)?.is_some_and(|p| !p.suspended_at.is_empty()) {
                return Ok(());
            }
            if row["status"] != "Approved" || row["submissionNumber"] != order["revision"] {
                return Err(conflict("定时变动的批准版本已改变。"));
            }
            let actor = super::super::units::resolve(
                tx,
                records::positive(&order, "actorId", "执行身份")?,
                service.store.edition,
                &text(&order, "contextId"),
            )?;
            auth::authorize(&actor, "office.personnel-changes", "approve")?;
            let today = tx
                .clock
                .at(now)
                .map_err(super::super::error::unavailable)?
                .today;
            personnel_changes::commit(
                tx,
                &actor,
                &json!({"kind":"personnel","resource":"office.personnel-changes"}),
                row,
                today,
                "complete",
                "已批准的人员变动按期生效。",
            )?;
            Ok(())
        });
        if let Err(cause) = result {
            if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) {
                service.store.transaction(|tx| {
                    let mut row = store::get(tx, "oa-personnel", id)?;
                    if row["status"] != "Approved"
                        || row["submissionNumber"] != order["revision"]
                        || tx
                            .personnel_order(id)?
                            .is_none_or(|o| o["state"] != "Pending")
                    {
                        return Ok(());
                    }
                    tx.personnel_order_state(
                        id,
                        "Blocked",
                        &format!("Execution{}", cause.status.unwrap_or(409)),
                    )?;
                    super::fulfillment::system_result(
                        &mut row,
                        export_doc_domain::approval_graph::SystemAction::ApplyPersonnel,
                        export_doc_domain::approval_graph::NodeState::Blocked,
                        &format!("Execution{}", cause.status.unwrap_or(409)),
                    )?;
                    super::super::workflow_tasks::persist(tx, "oa-personnel", &row)?;
                    Ok(())
                })?;
            } else {
                return Err(cause);
            }
        }
    }
    super::escalations::tick(service, now)?;
    super::document_schedule::tick(service)?;
    Ok(())
}
pub(super) fn schedule(tx: &Connection, actor: &Actor, row: &Value) -> Result<()> {
    let date = personnel_changes::effective_date(row)?;
    let (effective_at, _) = tx.clock.day_range(&date.to_string()).map_err(invalid)?;
    tx.schedule_personnel_order(&json!({"requestId":row["id"],"revision":row["submissionNumber"],"actorId":actor.id,"contextId":actor.context,"effectiveAt":effective_at}))?;
    Ok(())
}

pub(super) fn execute(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    mut row: Value,
    body: &Value,
    today: chrono::NaiveDate,
    cancel: bool,
) -> Result<Value> {
    required(body, "note", "处理说明", 500)?;
    let id = records::positive(&row, "id", "人员申请")?;
    if row["status"] != "Approved"
        || tx
            .personnel_order(id)?
            .is_none_or(|o| !matches!(o["state"].as_str(), Some("Pending" | "Blocked")))
    {
        return Err(conflict("只有已批准、尚未生效的定时人员变动可以办理。"));
    }
    if cancel {
        if row["ownerUserId"] != actor.id {
            super::super::units::demand(actor, &actor.company, "organization.workflows")?;
        }
        row["status"] = json!("Cancelled");
        tx.personnel_order_state(id, "Cancelled", "")?;
        save(
            tx,
            actor,
            meta,
            row,
            "cancel-execution",
            &text(body, "note"),
        )
    } else {
        super::qualification::person_id(tx, actor)?;
        let Some(mut plan) = approval::plan(&row)? else {
            return Err(conflict("人员生效流程缺失。"));
        };
        for node in plan.nodes.iter_mut().filter(|n| {
            n.system_action == export_doc_domain::approval_graph::SystemAction::ApplyPersonnel
                && n.state == export_doc_domain::approval_graph::NodeState::Blocked
        }) {
            node.state = export_doc_domain::approval_graph::NodeState::Active;
            node.error_code.clear();
        }
        row["approvalPlan"] = serde_json::to_value(plan)?;
        personnel_changes::commit(tx, actor, meta, row, today, "complete", &text(body, "note"))
    }
}
