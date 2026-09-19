//! Meeting handover and stock transitions share the same transaction boundary.
use super::{
    auth,
    error::{Result, conflict, error, invalid},
    office::{instant, integer, supply_totals},
    office_events,
    records::{positive, required, text},
    store::{self, Actor, Store},
};
use export_doc_storage::Connection;
use serde_json::{Value, json};

pub fn action(
    store: &Store,
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
    store.transaction(|tx| {
        let mut value = store::get(tx, kind, id)?;
        if value["companyScope"] != actor.company
            || !auth::visible(actor, permission, action, &value)
        {
            return Err(error(403, "没有办理此记录的权限。"));
        }
        if kind == "supplies" {
            return stock(tx, actor, operation, value, &body);
        }
        store::check_version(&value, store::expected(&body))?;
        if action == "approve" && value["ownerUserId"] == actor.id {
            return Err(error(
                403,
                "不能审批本人提交的申请，请由另一位有权限的人员办理。",
            ));
        }
        if kind == "bookings" {
            meeting(tx, actor, operation, &mut value)?;
        } else {
            supply(tx, actor, operation, &mut value, &body, business_date)?;
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
        office_events::append(tx, actor, kind, &saved, event, quantity, &note)?;
        Ok(saved)
    })
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
    if let Some(existing) = store::all(tx, "stock-movements")?
        .into_iter()
        .find(|row| row["operationId"] == operation_id)
    {
        if existing["officeSupplyId"] != id
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
        ("CancelMeetingBooking", "Pending" | "Approved") => "Cancelled",
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
        if store::all(tx, "bookings")?
            .iter()
            .any(|row| row["meetingRoomId"] == value["meetingRoomId"] && row["status"] == "InUse")
        {
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
            if reserved < quantity {
                return Err(conflict("库存与预留状态不一致，已停止取消。"));
            }
            supply["reservedQuantity"] = json!(reserved - quantity);
            value["status"] = json!("Cancelled");
        }
        ("CancelOfficeSupplyRequest", "Pending") => value["status"] = json!("Cancelled"),
        ("RejectOfficeSupplyRequest", "Pending") => value["status"] = json!("Rejected"),
        ("ApproveOfficeSupplyRequest", "Pending") => {
            if supply["isActive"] != true || stock - reserved < quantity {
                return Err(conflict("物品已停用或可用库存不足。"));
            }
            supply["reservedQuantity"] = json!(reserved + quantity);
            value["status"] = json!("Approved");
        }
        ("IssueOfficeSupply", "Approved") => {
            if supply["isActive"] != true || reserved < quantity {
                return Err(conflict("物品已停用或预留库存不足。"));
            }
            delta = -quantity;
            supply["stockQuantity"] = json!(stock - quantity);
            supply["reservedQuantity"] = json!(reserved - quantity);
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
    supply_totals(&mut supply);
    let identity = Some(text(&supply, "name"));
    store::save(
        tx, "supplies", supply_id, supply, identity, actor, operation,
    )?;
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
