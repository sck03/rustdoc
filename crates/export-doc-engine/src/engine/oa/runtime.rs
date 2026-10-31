//! Durable personnel execution and workflow reminders share task scheduling.
use super::*;
#[cfg(test)]
#[path = "runtime_tests.rs"]
mod tests;
pub(in crate::engine) fn tick(
    service: &NativeService,
    now: chrono::DateTime<chrono::Utc>,
) -> Result<()> {
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
                    tx.personnel_order_state(
                        id,
                        "Blocked",
                        &format!("Execution{}", cause.status.unwrap_or(409)),
                    )?;
                    Ok(())
                })?;
            } else {
                return Err(cause);
            }
        }
    }
    let nodes = service
        .store
        .read(|tx| Ok(tx.due_workflow_nodes(&now.to_rfc3339())?))?;
    for node in nodes {
        crate::operation::check()?;
        service.store.transaction(|tx| {
            let id = records::positive(&node, "requestId", "超时申请")?;
            let mut found = None;
            for kind in super::approval_references::WORKFLOWS {
                if let Some(row) = tx.get(kind, id)? { found = Some(row); break; }
            }
            let row = found.ok_or_else(|| super::super::error::unavailable("审批实例缺少申请。"))?;
            if row["status"] != "Pending" || row["submissionNumber"] != node["revision"] {
                return Ok(());
            }
            let plan = approval::plan(&row)?.ok_or_else(|| super::super::error::unavailable("审批实例缺少流程。"))?;
            if json!(plan.generation) != node["generation"]
                || !plan.current_indices().iter().any(|i| {
                    let current = &plan.nodes[plan.steps[*i].stage];
                    json!(current.key) == node["nodeKey"] && json!(current.due_at) == node["dueAt"]
                }) { return Ok(()); }
            let level = node["level"].as_i64().unwrap_or(0);
            if !tx.workflow_reminder_level(&node, level + 1)? { return Ok(()); }
            let actor = Actor {
                scope_rules: vec![], context: String::new(), edition: service.store.edition,
                id: 0, name: "流程运行任务".into(), company: text(&row, "companyScope"),
                department: text(&row, "departmentId"), departments: vec![],
                oa_assigned_only: false, admin: false, grants: vec![],
            };
            let mut request = row.clone();
            let kind = text(&row, "kind");
            request["kind"] = json!(match kind.as_str() {
                "bookings" => "room", "supply-requests" => "supply",
                other => other.strip_prefix("oa-").unwrap_or(other),
            });
            let event = store::save_in_scope(tx, "oa-event", 0, json!({
                "requestId": id, "requestVersion":row["versionNumber"], "action":"remind",
                "actorName":actor.name, "occurredAt":now.to_rfc3339(),
                "note":if level==0 {"审批时限已到，请办理或交接。"} else {"审批超时已满 24 小时，已进入督办。"},
                "executionIdentity":"System"
            }), None, &actor, "scheduled-reminder", Some(&row))?;
            let resource = approval::resource(&kind)
                .ok_or_else(|| super::super::error::unavailable("审批类型损坏。"))?;
            super::super::communication::on_event(tx, &actor, &request, &event, &json!({"resource":resource}))?;
            Ok(())
        })?;
    }
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
        personnel_changes::commit(tx, actor, meta, row, today, "complete", &text(body, "note"))
    }
}
