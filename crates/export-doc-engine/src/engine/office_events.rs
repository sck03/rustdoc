//! Request history is business data, committed with the request and its stock changes.
use super::{
    auth,
    error::{Result, error},
    records::text,
    store::{self, Actor, Store},
};
use crate::generated_api::*;
use export_doc_storage::Connection;
use serde_json::{Value, json};

pub fn append(
    tx: &Connection,
    actor: &Actor,
    kind: &str,
    record: &Value,
    action: &str,
    quantity: i64,
    note: &str,
) -> Result<()> {
    let event = store::save(
        tx,
        "office-events",
        0,
        json!({
            "requestKind":kind, "requestId":record["id"], "action":action,
            "quantity":quantity,"actorName":actor.name,"note":note,"approvalPlan":record["approvalPlan"]
        }),
        None,
        actor,
        action,
    )?;
    if matches!(kind, "supply-requests" | "bookings") {
        let action = match action {
            "Approve" => "approve",
            "Submit" => "submit",
            "ApproveStep" => "approve-step",
            "Edit" if record["status"] == "Pending" => "submit",
            "Edit" => "approve",
            "Issue" => "issue",
            "Return" => "return",
            "Reject" => "reject",
            "Cancel" => "cancel",
            "Reassign" => "reassign",
            _ => "",
        };
        let mut request = record.clone();
        let supply = kind == "supply-requests";
        request["kind"] = json!(if supply { "supply" } else { "room" });
        request["title"] = json!(format!(
            "{} · {}",
            text(record, if supply { "supplyName" } else { "roomName" }),
            text(record, if supply { "purpose" } else { "title" })
        ));
        let mut event = event;
        event["action"] = json!(action);
        super::communication::on_event(
            tx,
            actor,
            &request,
            &event,
            &json!({"resource":if supply { "office.supplies" } else { "office.rooms" }}),
        )?;
    }
    Ok(())
}

pub fn history(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    id: i64,
    query: &[(&str, String)],
) -> Result<Value> {
    let (kind, permission, action) = match operation {
        GET_MEETING_BOOKING_HISTORY => ("bookings", "office.rooms", "view"),
        GET_OFFICE_STOCK_HISTORY => ("supplies", "office.supplies", "restock"),
        _ => ("supply-requests", "office.supplies", "view"),
    };
    auth::authorize(actor, permission, action)?;
    store.read_for(actor, operation, query, |tx, actor| {
        let record = store::get(tx, kind, id)?;
        let allowed = super::handling::resource_access(
            tx,
            actor,
            permission,
            action,
            &record,
            kind == "supplies",
        )?;
        if !allowed {
            return Err(error(403, "没有查看这项记录历史的权限。"));
        }
        let stock = operation == GET_OFFICE_STOCK_HISTORY;
        let mut filters = vec![("companyScope", actor.company.clone())];
        if stock {
            filters.push(("officeSupplyId", id.to_string()));
        } else {
            filters.extend([("requestKind", kind.into()), ("requestId", id.to_string())]);
        }
        if let Some((_, status)) = query
            .iter()
            .find(|(key, value)| *key == "status" && !value.is_empty())
        {
            filters.push(("status", status.clone()));
        }
        let keyword = store::normalize(
            query
                .iter()
                .find(|(key, _)| *key == "keyword")
                .map(|(_, value)| value.as_str())
                .unwrap_or(""),
        );
        let (page, size, offset) = store::page_parameters(query);
        let (total, events) = tx.query_ordered_records(
            &export_doc_storage::GenericQuery {
                kind: if stock {
                    "stock-movements"
                } else {
                    "office-events"
                },
                scope: export_doc_storage::DataScope {
                    all: true,
                    ..Default::default()
                },
                filters: &filters,
                keyword: &keyword,
                search_root: None,
                fields: &[],
                offset,
                limit: size as i64,
            },
            export_doc_storage::RecordOrder::CreatedDescending,
        )?;
        Ok(crate::contracts::page(events, total as usize, page, size))
    })
}
