//! Append-only attachment revision and event metadata, independently paged.
use crate::{Adapter, Error, Result, sql};
use serde_json::{Value, json};

pub(crate) fn append(
    db: &(impl Adapter + ?Sized),
    record: i64,
    kind: &str,
    body: &Value,
) -> Result<()> {
    if !matches!(kind, "revision" | "event") || !body.is_object() {
        return Err(Error::unavailable("资料历史格式无效。"));
    }
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let record = sql::bind(&mut values, record, pg);
    let kind = sql::bind(&mut values, kind, pg);
    let body = sql::bind(&mut values, serde_json::to_string(body)?, pg);
    let data = if pg {
        format!("CAST({body} AS JSONB)")
    } else {
        body
    };
    db.execute_statement(&format!("INSERT INTO attachment_history(record_id,kind,sequence,body) SELECT CAST({record} AS BIGINT),{kind},COALESCE(MAX(sequence),0)+1,{data} FROM attachment_history WHERE record_id=CAST({record} AS BIGINT) AND kind={kind}"), &values)?;
    Ok(())
}
pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    record: i64,
    kind: &str,
    before: Option<i64>,
    limit: i64,
) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let record = sql::bind(&mut values, record, pg);
    let kind = sql::bind(&mut values, kind, pg);
    let filter = format!("record_id=CAST({record} AS BIGINT) AND kind={kind}");
    let total = db.query_json(
        &format!("SELECT CAST(COUNT(*) AS TEXT) FROM attachment_history WHERE {filter}"),
        &values,
    )?;
    let before = before
        .map(|value| {
            format!(
                " AND sequence<CAST({} AS BIGINT)",
                sql::bind(&mut values, value, pg)
            )
        })
        .unwrap_or_default();
    let count = limit.clamp(1, 200);
    let body = if pg {
        "jsonb_build_object('sequence',sequence,'value',body)"
    } else {
        "json_object('sequence',sequence,'value',json(body))"
    };
    let select = format!(
        "SELECT {body} AS body FROM attachment_history WHERE {filter}{before} ORDER BY sequence DESC LIMIT {}",
        count + 1
    );
    let statement = if pg {
        format!("SELECT COALESCE(jsonb_agg(body),'[]'::jsonb)::text FROM ({select}) selected")
    } else {
        format!("SELECT json_group_array(json(body)) FROM ({select}) selected")
    };
    let mut rows: Vec<Value> = serde_json::from_value(db.query_json(&statement, &values)?)?;
    let more = rows.len() > count as usize;
    rows.truncate(count as usize);
    let next = if more {
        rows.last().map(|row| row["sequence"].clone())
    } else {
        None
    };
    Ok(
        json!({"items":rows.into_iter().map(|row|row["value"].clone()).collect::<Vec<_>>(),"totalCount":total,"nextCursor":next}),
    )
}
