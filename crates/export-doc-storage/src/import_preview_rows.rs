//! Immutable preview rows: indexed pages and bounded reads share one payload.
use crate::{Adapter, Error, Result, sql};
use serde_json::Value;

pub(crate) fn insert(db: &(impl Adapter + ?Sized), preview: i64, payload: &str) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let id = sql::bind(&mut values, preview, pg);
    let payload = sql::bind(&mut values, payload, pg);
    let rows = if pg {
        format!(
            "SELECT CAST({id} AS BIGINT), ordinal-1, item::text FROM json_array_elements(CAST({payload} AS JSON)) WITH ORDINALITY AS rows(item,ordinal)"
        )
    } else {
        format!("SELECT CAST({id} AS INTEGER), key, value FROM json_each({payload})")
    };
    db.execute_statement(
        &format!("INSERT INTO import_preview_rows(preview_id,position,body) {rows}"),
        &values,
    )?;
    Ok(())
}

fn filter(preview: i64, change: &str, values: &mut Vec<String>, pg: bool) -> String {
    let id = sql::bind(values, preview, pg);
    let mut condition = format!("preview_id=CAST({id} AS BIGINT)");
    if !change.is_empty() {
        let change = sql::bind(values, change, pg);
        condition.push_str(&format!(" AND change_type={change}"));
    }
    condition
}

pub(crate) fn count(db: &(impl Adapter + ?Sized), preview: i64, change: &str) -> Result<i64> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let condition = filter(preview, change, &mut values, pg);
    db.query_json(
        &format!("SELECT CAST(COUNT(*) AS TEXT) FROM import_preview_rows WHERE {condition}"),
        &values,
    )?
    .as_i64()
    .ok_or_else(|| Error::unavailable("导入预检行数无效。"))
}

pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    preview: i64,
    change: &str,
    offset: i64,
    limit: i64,
) -> Result<Vec<Value>> {
    if offset < 0 || !(1..=200).contains(&limit) {
        return Err(Error::unavailable("导入预检分页范围无效。"));
    }
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let condition = filter(preview, change, &mut values, pg);
    let limit = sql::bind(&mut values, limit, pg);
    let offset = sql::bind(&mut values, offset, pg);
    let aggregate = if pg {
        "COALESCE(json_agg(body::json ORDER BY position),'[]'::json)::text"
    } else {
        "json_group_array(json(body))"
    };
    let result = db.query_json(&format!("SELECT {aggregate} FROM (SELECT position,body FROM import_preview_rows WHERE {condition} ORDER BY position LIMIT CAST({limit} AS BIGINT) OFFSET CAST({offset} AS BIGINT)) rows"), &values)?;
    serde_json::from_value(result).map_err(Into::into)
}

pub(crate) fn remove(db: &(impl Adapter + ?Sized), preview: i64) -> Result<()> {
    let mut values = vec![];
    let id = sql::bind(&mut values, preview, db.provider() == "PostgreSQL");
    db.execute_statement(
        &format!("DELETE FROM import_preview_rows WHERE preview_id=CAST({id} AS BIGINT)"),
        &values,
    )?;
    Ok(())
}
