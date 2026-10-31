//! Resource drafts and archives reuse OA files and publication; inventory and
//! physical handover remain in the resource workflow module.
use super::*;

pub(super) fn resource_kind(value: &Value) -> Option<&'static str> {
    match value["kind"].as_str() {
        Some("room") => Some("bookings"),
        Some("supply") => Some("supply-requests"),
        _ => None,
    }
}
pub(in crate::engine) fn created(tx: &Connection, actor: &Actor, mut row: Value) -> Result<Value> {
    let kind = resource_kind(&row).ok_or_else(|| invalid("未知资源申请。"))?;
    let event = if row["status"] == "Pending" {
        submissions::retain(tx, actor, &mut row)?;
        row = store::save(
            tx,
            kind,
            records::positive(&row, "id", "申请")?,
            row.clone(),
            Some(text(&row, "requestKey")),
            actor,
            "submit-snapshot",
        )?;
        "Submit"
    } else {
        "Draft"
    };
    super::super::office_events::append(
        tx,
        actor,
        kind,
        &row,
        event,
        row["quantity"].as_i64().unwrap_or(0),
        "",
    )?;
    Ok(row)
}
pub(super) fn save(
    tx: &Connection,
    actor: &Actor,
    row: Value,
    action: &str,
    note: &str,
) -> Result<Value> {
    let kind = resource_kind(&row).ok_or_else(|| invalid("未知资源申请。"))?;
    let id = records::positive(&row, "id", "申请")?;
    let saved = store::save(
        tx,
        kind,
        id,
        row.clone(),
        Some(text(&row, "requestKey")),
        actor,
        action,
    )?;
    let event = match action {
        "submit" => "Submit",
        "approval-reassign" => "ApprovalReassign",
        "upload" => "Upload",
        "delete-attachment" => "DeleteAttachment",
        _ => "Edit",
    };
    super::super::office_events::append(tx, actor, kind, &saved, event, 0, note)?;
    project(tx, actor, saved, true)
}
pub(super) fn change(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    mut row: Value,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    mutable(&row)?;
    let submit = meta["action"] == "resource-submit";
    if !submit {
        let request = contracts::dto(
            contracts::request(if meta["kind"] == "room" {
                "UpdateDraftOfMeetingBooking"
            } else {
                "UpdateDraftOfOfficeSupplyRequest"
            }),
            body.clone(),
        );
        for (key, value) in request
            .as_object()
            .ok_or_else(|| invalid("资源草稿内容无效。"))?
        {
            if key != "expectedVersion" {
                row[key] = value.clone();
            }
        }
    }
    if text(body, "note").chars().count() > 500 {
        return Err(invalid("说明最多 500 字。"));
    }
    row["submitImmediately"] = json!(submit);
    let kind = resource_kind(&row).ok_or_else(|| invalid("未知资源申请。"))?;
    super::super::office::validate(tx, actor, kind, 0, &row.clone(), &mut row, today)?;
    if submit {
        submissions::retain(tx, actor, &mut row)?;
    }
    save(
        tx,
        actor,
        row,
        if submit { "submit" } else { "edit" },
        &text(body, "note"),
    )
}
pub(in crate::engine) fn event(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    action: &str,
    note: &str,
) -> Result<()> {
    let action = if action == "cancel" && row["submissionId"].as_i64().is_some_and(|id| id > 0) {
        let snapshot =
            submissions::checked(tx, row, records::positive(row, "submissionId", "提交版本")?)?;
        if snapshot["status"] == "Approved" {
            "void"
        } else {
            "withdraw"
        }
    } else {
        action
    };
    submissions::after_save(tx, actor, row, action, note)
}
pub(in crate::engine) fn schedule(
    service: &NativeService,
    actor: &Actor,
    kind: &str,
    row: &Value,
) -> Result<Value> {
    let kind = match kind {
        "bookings" => "room",
        "supply-requests" => "supply",
        _ => return Err(invalid("未知资源申请。")),
    };
    documents::start_approved(service, actor, &json!({"kind":kind}), row)
}
