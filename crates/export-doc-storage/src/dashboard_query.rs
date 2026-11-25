//! Counts and bounded pages share the same authorized preferred invoice selection.
use crate::{Adapter, DataScope, QuerySql, Result, sql};
use serde_json::Value;

pub(crate) fn scan(
    db: &(impl Adapter + ?Sized),
    scope: &DataScope<'_>,
    fields: &[(&str, &str)],
    before: Option<i64>,
    window: Option<(&str, &str)>,
    status: &str,
    limit: i64,
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut query = selection(scope, pg);
    if let Some((start, end)) = window {
        let date = sql::field("invoiceDate", pg);
        let start = sql::bind(&mut query.values, start, pg);
        let end = sql::bind(&mut query.values, end, pg);
        query.filter += &format!(" AND {date}>={start} AND {date}<{end}");
    }
    if !status.is_empty() {
        query.filter += &format!(
            " AND {}={}",
            sql::field("status", pg),
            sql::bind(&mut query.values, status, pg)
        );
    }
    if let Some(before) = before {
        query.filter += &format!(
            " AND r.id<CAST({} AS BIGINT)",
            sql::bind(&mut query.values, before, pg)
        );
    }
    db.query_slice(
        &query,
        &sql::projection(fields, pg)?,
        0,
        limit.clamp(1, 200),
    )
}

#[derive(Debug, serde::Deserialize)]
pub struct DashboardCounts {
    pub total: i64,
    pub draft: i64,
    pub verified: i64,
    pub shipped: i64,
    pub completed: i64,
}

pub(crate) fn counts(
    db: &(impl Adapter + ?Sized),
    scope: &DataScope<'_>,
) -> Result<DashboardCounts> {
    let pg = db.provider() == "PostgreSQL";
    let query = selection(scope, pg);
    let status = sql::field("status", pg);
    let count = |value| format!("COALESCE(SUM(CASE WHEN {status}='{value}' THEN 1 ELSE 0 END),0)");
    let projection = format!(
        "{}('total',COUNT(*),'draft',{},'verified',{},'shipped',{},'completed',{}){}",
        sql::object(pg),
        count("Draft"),
        count("Verified"),
        count("Shipped"),
        count("Completed"),
        if pg { "::text" } else { "" }
    );
    Ok(serde_json::from_value(db.query_json(
        &crate::query_source::aggregate(&query, &projection),
        &query.values,
    )?)?)
}

fn selection(scope: &DataScope<'_>, pg: bool) -> QuerySql {
    let mut values = vec![];
    let visible = scope.filter(&mut values, pg);
    let other_visible = scope.filter_for(&mut values, pg, "v");
    let field = |source, key| sql::field_from(source, key, pg);
    let rank = |source| {
        let kind = field(source, "type");
        format!("CASE WHEN {kind} LIKE '%实际%' THEN 2 WHEN {kind} LIKE '%报关%' THEN 1 ELSE 0 END")
    };
    let same_number = format!(
        "{}={}",
        field("v.body", "invoiceNo"),
        field("r.body", "invoiceNo")
    );
    let higher = format!(
        "({} > {} OR ({} = {} AND v.id>r.id))",
        rank("v.body"),
        rank("r.body"),
        rank("v.body"),
        rank("r.body")
    );
    let filter = format!(
        "r.kind='invoices' AND {visible} AND {}<>'Cancelled' AND NOT EXISTS (SELECT 1 FROM records v WHERE v.kind='invoices' AND v.company=r.company AND {same_number} AND {}<>'Cancelled' AND {other_visible} AND {higher})",
        field("r.body", "status"),
        field("v.body", "status")
    );
    QuerySql {
        source: crate::query_source::Source::Records,
        filter,
        order: "r.id DESC".into(),
        values,
    }
}
