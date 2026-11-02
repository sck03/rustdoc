//! Meeting handover and stock transitions share the same transaction boundary.
use super::{
    auth,
    error::{Result, conflict, error, invalid},
    office::{instant, integer, supply_totals},
    office_events,
    records::{positive, required, text},
    store::{self, Actor},
};
use crate::engine::store::Connection;
use serde_json::{Value, json};

pub fn action(
    service: &super::NativeService,
    actor: &Actor,
    operation: &str,
    id: i64,
    body: Value,
    business_date: chrono::NaiveDate,
) -> Result<Value> {
    let (kind, permission, action) = match operation {
        "RestockOfficeSupply" => ("supplies", "office.supplies", "restock"),
        "StocktakeOfficeSupply" => ("supplies", "office.supplies", "manage"),
        "ApproveOfficeSupplyRequest" | "RejectOfficeSupplyRequest" => {
            ("supply-requests", "office.supplies", "approve")
        }
        "CancelOfficeSupplyRequest" => ("supply-requests", "office.supplies", "cancel"),
        "IssueOfficeSupply" => ("supply-requests", "office.supplies", "issue"),
        "ReturnOfficeSupply" => ("supply-requests", "office.supplies", "return"),
        "ApproveMeetingBooking" | "RejectMeetingBooking" => ("bookings", "office.rooms", "approve"),
        "CancelMeetingBooking" => ("bookings", "office.rooms", "cancel"),
        "IssueMeetingRoomKey" => ("bookings", "office.rooms", "issue"),
        "ReturnMeetingRoomKey" => ("bookings", "office.rooms", "return"),
        _ => return Err(invalid("未知行政操作。")),
    };
    auth::authorize(actor, permission, action)?;
    let note = text(&body, "note");
    if note.chars().count() > 500
        || ((operation.starts_with("Reject") || operation.starts_with("Cancel")) && note.is_empty())
    {
        return Err(invalid("取消或驳回须填写处理原因，备注不得超过 500 字。"));
    }
    let mut result = service.store.transaction_as(actor, |tx, actor| {
        auth::authorize(actor, permission, action)?;
        let mut value = store::get(tx, kind, id)?;
        let allowed = if action == "approve" {
            super::oa::assigned_to(tx, actor, permission, &value)?
        } else {
            super::handling::resource_access(
                tx,
                actor,
                permission,
                action,
                &value,
                kind == "supplies",
            )?
        };
        let allowed = allowed
            || kind != "supplies" && super::oa::fulfillment::can_handle(tx, actor, &value, action)?;
        if !allowed {
            return Err(error(403, "没有办理此记录的权限。"));
        }
        if kind == "supplies" {
            return stock(tx, actor, operation, value, &body);
        }
        if super::command_receipts::prepare(tx, actor, &mut value, operation, &body)? {
            return super::oa::project(tx, actor, value, true);
        }
        store::check_version(&value, store::expected(&body))?;
        if matches!(action, "issue" | "return") {
            super::oa::materials::authorize_edit(tx, actor, &value)?;
        }
        if action == "approve" && value["ownerUserId"] == actor.id {
            return Err(error(
                403,
                "不能审批本人提交的申请，请由另一位有权限的人员办理。",
            ));
        }
        if action == "issue" && value["reservationState"] == "Insufficient" {
            super::oa::fulfillment::system_result(
                &mut value,
                export_doc_domain::approval_graph::SystemAction::ReserveSupply,
                export_doc_domain::approval_graph::NodeState::Completed,
                "",
            )?;
        }
        let decision = if action == "approve" {
            Some(super::oa::approval::review(
                tx,
                actor,
                &mut value,
                if operation.starts_with("Reject") {
                    "reject"
                } else {
                    "approve"
                },
                &note,
                &text(&body, "taskKey"),
                &body,
            )?)
        } else {
            None
        };
        if decision == Some("Pending") {
            value["status"] = json!("Pending");
        } else if decision == Some("Rejected") {
            value["status"] = json!("Rejected");
        } else if kind == "bookings" {
            meeting(tx, actor, operation, &mut value)?;
        } else {
            supply(tx, actor, operation, &mut value, &body, business_date)?;
        }
        if decision == Some("Approved") && kind == "supply-requests" {
            if super::oa::fulfillment::active_system(
                &value,
                export_doc_domain::approval_graph::SystemAction::ReserveSupply,
            )? {
                reserve(tx, actor, &mut value, business_date)?;
            }
            let insufficient = value["reservationState"] == "Insufficient";
            super::oa::fulfillment::system_result(
                &mut value,
                export_doc_domain::approval_graph::SystemAction::ReserveSupply,
                if insufficient {
                    export_doc_domain::approval_graph::NodeState::Blocked
                } else {
                    export_doc_domain::approval_graph::NodeState::Completed
                },
                if insufficient {
                    "StockInsufficient"
                } else {
                    ""
                },
            )?;
        }
        if matches!(action, "issue" | "return") {
            let finished = action == "issue"
                || matches!(value["status"].as_str(), Some("Returned" | "Completed"));
            super::oa::fulfillment::progress(tx, actor, &mut value, &body, action, finished)?;
        }
        let identity = Some(text(&value, "requestKey"));
        let saved = store::save(tx, kind, id, value, identity, actor, action)?;
        let event = if operation.starts_with("Approve") {
            "Approve"
        } else if operation.starts_with("Reject") {
            "Reject"
        } else if operation.starts_with("Cancel") {
            "Cancel"
        } else if operation.starts_with("Issue") {
            "Issue"
        } else {
            "Return"
        };
        let quantity = if kind == "supply-requests" {
            if event == "Return" {
                body["quantity"].as_i64().unwrap_or(0)
            } else {
                saved["quantity"].as_i64().unwrap_or(0)
            }
        } else {
            0
        };
        office_events::append(
            tx,
            actor,
            kind,
            &saved,
            if decision == Some("Pending") && operation.starts_with("Approve") {
                "ApproveStep"
            } else {
                event
            },
            quantity,
            &note,
        )?;
        super::oa::project(tx, actor, saved, true)
    })?;
    if operation.starts_with("Approve")
        && result["status"] == "Approved"
        && result["submissionId"].as_i64().is_some()
        && super::oa::fulfillment::active_system(
            &result,
            export_doc_domain::approval_graph::SystemAction::GenerateApprovalDocument,
        )?
    {
        match super::oa::resource_document(service, actor, kind, &result) {
            Ok(document) => result["approvalDocument"] = document,
            Err(cause) => eprintln!(
                "Resource approval {} committed; document scheduling failed: {cause}",
                result["id"]
            ),
        }
    }
    Ok(result)
}

fn stock(
    tx: &Connection,
    actor: &Actor,
    operation: &str,
    mut value: Value,
    body: &Value,
) -> Result<Value> {
    let id = positive(&value, "id", "物品编号")?;
    let operation_id = text(body, "operationId");
    if operation_id.is_empty() || operation_id.len() > 100 {
        return Err(invalid("库存操作缺少有效的幂等编号。"));
    }
    let quantity = integer(
        body,
        "quantity",
        if operation == "StocktakeOfficeSupply" {
            0
        } else {
            1
        },
        1_000_000,
        "数量",
    )?;
    required(body, "note", "库存变动说明", 500)?;
    let kind = if operation == "StocktakeOfficeSupply" {
        "Stocktake"
    } else {
        "Restock"
    };
    if let Some(existing) = tx.find_identity("stock-movements", &store::normalize(&operation_id))? {
        if existing["operationId"] != operation_id
            || existing["officeSupplyId"] != id
            || existing["ownerUserId"] != actor.id
            || existing["kind"] != kind
            || existing["requestedQuantity"] != quantity
            || existing["note"] != text(body, "note")
        {
            return Err(conflict("同一操作标识不能用于不同的库存变动。"));
        }
        return Ok(existing);
    }
    store::check_version(&value, store::expected(body))?;
    let current = integer(&value, "stockQuantity", 0, 1_000_000, "在库数量")?;
    let next = if kind == "Stocktake" {
        quantity
    } else {
        current
            .checked_add(quantity)
            .filter(|next| *next <= 1_000_000)
            .ok_or_else(|| invalid("库存数量超出范围。"))?
    };
    if next < value["reservedQuantity"].as_i64().unwrap_or(0) {
        return Err(conflict("盘点库存不能少于已预留数量。"));
    }
    value["stockQuantity"] = json!(next);
    supply_totals(&mut value);
    let identity = Some(text(&value, "name"));
    store::save(tx, "supplies", id, value, identity, actor, kind)?;
    store::save(
        tx,
        "stock-movements",
        0,
        json!({"officeSupplyId":id,"operationId":operation_id,
        "kind":kind,"requestedQuantity":quantity,"quantityDelta":next-current,"stockAfter":next,"note":text(body,"note"),"actorName":actor.name}),
        Some(operation_id),
        actor,
        kind,
    )
}

fn meeting(tx: &Connection, actor: &Actor, operation: &str, value: &mut Value) -> Result<()> {
    let state = text(value, "status");
    let next = match (operation, state.as_str()) {
        ("ApproveMeetingBooking", "Pending") => "Approved",
        ("RejectMeetingBooking", "Pending") => "Rejected",
        ("CancelMeetingBooking", "Draft" | "Pending" | "Approved") => "Cancelled",
        ("IssueMeetingRoomKey", "Approved") => "InUse",
        ("ReturnMeetingRoomKey", "InUse") => "Completed",
        _ => return Err(conflict("当前预约状态不能执行此操作。")),
    };
    let room = super::records::parent(
        tx,
        actor,
        "rooms",
        positive(value, "meetingRoomId", "会议室")?,
    )?;
    let now = chrono::Utc::now();
    if next == "Approved"
        && (room["isActive"] != true
            || instant(value, "endsAt")? <= now
            || value["attendeeCount"].as_i64() > room["capacity"].as_i64())
    {
        return Err(conflict(
            "预约已过期、会议室已停用或容量不足，请驳回后重新申请。",
        ));
    }
    if next == "InUse" {
        if room["isActive"] != true
            || now < instant(value, "startsAt")? - chrono::Duration::minutes(30)
            || now >= instant(value, "endsAt")?
        {
            return Err(conflict("请在预约开始前 30 分钟至结束前办理交接。"));
        }
        if tx.record_exists(
            "bookings",
            &[
                ("meetingRoomId", value["meetingRoomId"].to_string()),
                ("status", "InUse".into()),
            ],
        )? {
            return Err(conflict("上一场使用尚未结束或钥匙尚未归还，请先核实归还。"));
        }
        value["issuedAt"] = json!(now.to_rfc3339());
    }
    if next == "Completed" {
        value["returnedAt"] = json!(now.to_rfc3339());
    }
    value["status"] = json!(next);
    Ok(())
}

pub(super) fn reserve(
    tx: &Connection,
    actor: &Actor,
    value: &mut Value,
    business_date: chrono::NaiveDate,
) -> Result<()> {
    let id = positive(value, "officeSupplyId", "物品")?;
    let mut supply = super::records::parent(tx, actor, "supplies", id)?;
    if supply["isActive"] != true
        || value["returnDueDate"]
            .as_str()
            .is_some_and(|day| day < business_date.to_string().as_str())
    {
        return Err(conflict("物品已停用或借用日期已过。"));
    }
    if matches!(
        value["reservationState"].as_str(),
        Some("Reserved" | "Consumed")
    ) {
        return Ok(());
    }
    let stock = integer(&supply, "stockQuantity", 0, 1_000_000, "在库数量")?;
    let reserved = integer(&supply, "reservedQuantity", 0, stock, "预留数量")?;
    let quantity = positive(value, "quantity", "数量")?;
    if stock - reserved < quantity {
        value["reservationState"] = json!("Insufficient");
    } else {
        supply["reservedQuantity"] = json!(reserved + quantity);
        supply_totals(&mut supply);
        let identity = Some(text(&supply, "name"));
        store::save(tx, "supplies", id, supply, identity, actor, "reserve")?;
        value["reservationState"] = json!("Reserved");
    }
    Ok(())
}
pub(super) fn compensate(
    tx: &Connection,
    actor: &Actor,
    value: &mut Value,
    body: &Value,
    pending: bool,
) -> Result<()> {
    let kind = text(value, "kind");
    let room = matches!(kind.as_str(), "bookings" | "room");
    let resource = if room {
        "office.rooms"
    } else {
        "office.supplies"
    };
    let action = if pending { "cancel" } else { "return" };
    auth::authorize(actor, resource, action)?;
    if !auth::restriction_allows(actor, resource, action, value, true) {
        return Err(error(403, "补偿超出当前身份的资源范围。"));
    }
    if pending && matches!(value["status"].as_str(), Some("Issued" | "InUse")) {
        return Err(conflict("资源已经实际交付，须先办理真实归还再终止。"));
    }
    if room {
        meeting(
            tx,
            actor,
            if pending {
                "CancelMeetingBooking"
            } else {
                "ReturnMeetingRoomKey"
            },
            value,
        )?;
    } else {
        if !pending {
            let outstanding = positive(value, "quantity", "数量")?
                - value["returnedQuantity"].as_i64().unwrap_or(0);
            if body["quantity"] != outstanding {
                return Err(invalid("子流程资源补偿须登记全部尚未归还数量。"));
            }
        }
        supply(
            tx,
            actor,
            if pending {
                "CancelOfficeSupplyRequest"
            } else {
                "ReturnOfficeSupply"
            },
            value,
            body,
            super::units::today(tx)?,
        )?;
    }
    Ok(())
}
fn supply(
    tx: &Connection,
    actor: &Actor,
    operation: &str,
    value: &mut Value,
    body: &Value,
    business_date: chrono::NaiveDate,
) -> Result<()> {
    let supply_id = positive(value, "officeSupplyId", "物品")?;
    let mut supply = super::records::parent(tx, actor, "supplies", supply_id)?;
    if matches!(
        operation,
        "ApproveOfficeSupplyRequest" | "IssueOfficeSupply"
    ) && (supply["isActive"] != true
        || value["returnDueDate"]
            .as_str()
            .is_some_and(|day| day < business_date.to_string().as_str()))
    {
        return Err(conflict(
            "物品已停用或预计归还日期已过，请关闭申请后重新提交。",
        ));
    }
    let stock = integer(&supply, "stockQuantity", 0, 1_000_000, "在库数量")?;
    let reserved = integer(&supply, "reservedQuantity", 0, stock, "预留数量")?;
    let quantity = positive(value, "quantity", "数量")?;
    let mut delta = 0;
    match (operation, text(value, "status").as_str()) {
        ("CancelOfficeSupplyRequest", "Approved") => {
            if value["reservationState"] == "Reserved" && reserved < quantity {
                return Err(conflict("库存与预留状态不一致，已停止取消。"));
            }
            if value["reservationState"] == "Reserved" {
                supply["reservedQuantity"] = json!(reserved - quantity);
            }
            value["status"] = json!("Cancelled");
        }
        ("CancelOfficeSupplyRequest", "Draft" | "Pending") => value["status"] = json!("Cancelled"),
        ("RejectOfficeSupplyRequest", "Pending") => value["status"] = json!("Rejected"),
        ("ApproveOfficeSupplyRequest", "Pending") => {
            if supply["isActive"] != true {
                return Err(conflict("物品已停用。"));
            }
            value["status"] = json!("Approved");
        }
        ("IssueOfficeSupply", "Approved") => {
            let has_reservation = value["reservationState"] == "Reserved";
            if supply["isActive"] != true
                || (has_reservation && reserved < quantity)
                || (!has_reservation && stock - reserved < quantity)
            {
                return Err(conflict("物品已停用或预留库存不足。"));
            }
            delta = -quantity;
            supply["stockQuantity"] = json!(stock - quantity);
            supply["reservedQuantity"] = json!(if has_reservation {
                reserved - quantity
            } else {
                reserved
            });
            value["reservationState"] = json!("Consumed");
            value["status"] = json!("Issued");
        }
        ("ReturnOfficeSupply", "Issued") => {
            if value["isReturnable"] != true {
                return Err(conflict("此物品属于消耗品，不办理归还。"));
            }
            let returned = integer(value, "returnedQuantity", 0, quantity, "已归还数量")?;
            delta = integer(body, "quantity", 1, quantity - returned, "本次归还数量")?;
            if stock + delta > 1_000_000 {
                return Err(conflict("归还后库存超出范围。"));
            }
            supply["stockQuantity"] = json!(stock + delta);
            value["returnedQuantity"] = json!(returned + delta);
            if returned + delta == quantity {
                value["status"] = json!("Returned");
            }
        }
        _ => return Err(conflict("当前领用状态不能执行此操作。")),
    }
    if supply["stockQuantity"] != stock || supply["reservedQuantity"] != reserved {
        supply_totals(&mut supply);
        let identity = Some(text(&supply, "name"));
        store::save(
            tx, "supplies", supply_id, supply, identity, actor, operation,
        )?;
    }
    if delta != 0 {
        let kind = if delta > 0 { "Return" } else { "Issue" };
        store::save(
            tx,
            "stock-movements",
            0,
            json!({"officeSupplyId":supply_id,"officeSupplyRequestId":value["id"],
            "kind":kind,"quantityDelta":delta,"stockAfter":stock+delta,"note":text(body,"note"),"actorName":actor.name}),
            None,
            actor,
            kind,
        )?;
    }
    Ok(())
}
