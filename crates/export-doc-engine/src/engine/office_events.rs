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
    store::save(
        tx,
        "office-events",
        0,
        json!({
            "requestKind":kind, "requestId":record["id"], "action":action,
            "quantity":quantity,"actorName":actor.name,"note":note
        }),
        None,
        actor,
        action,
    )?;
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
    store.transaction(|tx| {
        let record = store::get(tx, kind, id)?;
        if record["companyScope"] != actor.company
            || !auth::visible(actor, permission, action, &record)
        {
            return Err(error(403, "没有查看这项记录历史的权限。"));
        }
        let stock = operation == GET_OFFICE_STOCK_HISTORY;
        let mut events: Vec<_> = store::all(
            tx,
            if stock {
                "stock-movements"
            } else {
                "office-events"
            },
        )?
        .into_iter()
        .filter(|event| {
            event["companyScope"] == actor.company
                && if stock {
                    event["officeSupplyId"] == id
                } else {
                    event["requestKind"] == kind && event["requestId"] == id
                }
        })
        .collect();
        events.sort_by_key(|event| {
            std::cmp::Reverse((text(event, "createdAt"), event["id"].as_i64()))
        });
        Ok(store::paged(events, query))
    })
}
