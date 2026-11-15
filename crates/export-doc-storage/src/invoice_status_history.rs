//! Immutable status facts; like audit history, they survive explicit invoice purging.
//! Actor names and reasons do not depend on mutable accounts.
use crate::{Adapter, Result, sql};
use serde::Serialize;
use serde_json::{Value, json};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InvoiceStatusEvent {
    pub id: i64,
    pub invoice_id: i64,
    pub from_status: String,
    pub to_status: String,
    pub note: String,
    pub changed_by_user_id: i64,
    pub changed_by_username: String,
    pub changed_at: String,
}

pub(crate) fn append(db: &(impl Adapter + ?Sized), event: &InvoiceStatusEvent) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let invoice = sql::bind(&mut values, event.invoice_id, pg);
    let version = sql::bind(&mut values, event.id, pg);
    let body = sql::bind(&mut values, serde_json::to_string(event)?, pg);
    let body = if pg {
        format!("CAST({body} AS JSONB)")
    } else {
        body
    };
    db.execute_statement(&format!("INSERT INTO invoice_status_history(invoice_id,version,body) VALUES(CAST({invoice} AS BIGINT),CAST({version} AS BIGINT),{body})"), &values)?;
    Ok(())
}

pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    record: i64,
    before: Option<i64>,
    limit: i64,
) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let invoice = sql::bind(&mut values, record, pg);
    let before = before
        .map(|id| {
            format!(
                " AND version<CAST({} AS BIGINT)",
                sql::bind(&mut values, id, pg)
            )
        })
        .unwrap_or_default();
    let limit = limit.clamp(1, 200) as usize;
    let select = format!(
        "SELECT body,version FROM invoice_status_history WHERE invoice_id=CAST({invoice} AS BIGINT){before} ORDER BY version DESC LIMIT {}",
        limit + 1
    );
    let query = if pg {
        format!(
            "SELECT COALESCE(jsonb_agg(body ORDER BY version DESC),'[]'::jsonb)::text FROM ({select}) selected"
        )
    } else {
        format!("SELECT json_group_array(json(body)) FROM ({select})")
    };
    let mut items: Vec<Value> = serde_json::from_value(db.query_json(&query, &values)?)?;
    let more = items.len() > limit;
    items.truncate(limit);
    let next = if more {
        items.last().map(|item| item["id"].clone())
    } else {
        None
    };
    Ok(json!({"items":items,"nextCursor":next}))
}
