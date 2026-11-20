//! Preferred invoice versions are selected before keyset paging, with no COUNT/OFFSET scan.
use crate::{Adapter, DataScope, QuerySql, Result, sql};
use serde_json::Value;

pub(crate) fn scan(
    db: &(impl Adapter + ?Sized),
    scope: &DataScope<'_>,
    fields: &[(&str, &str)],
    before: Option<i64>,
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
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
    let mut filter = format!(
        "r.kind='invoices' AND {visible} AND {}<>'Cancelled' AND NOT EXISTS (SELECT 1 FROM records v WHERE v.kind='invoices' AND v.company=r.company AND {same_number} AND {}<>'Cancelled' AND {other_visible} AND {higher})",
        field("r.body", "status"),
        field("v.body", "status")
    );
    if let Some(before) = before {
        filter += &format!(
            " AND r.id<CAST({} AS BIGINT)",
            sql::bind(&mut values, before, pg)
        );
    }
    db.query_slice(
        &QuerySql {
            source: crate::query_source::Source::Records,
            filter,
            order: "r.id DESC".into(),
            values,
        },
        &sql::projection(fields, pg)?,
        0,
        200,
    )
}
