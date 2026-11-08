//! Cheap candidate predicates precede permission evaluation and DTO allocation.
use crate::{DataScope, QuerySql, Result, ScopedQuery, generic_query, sql};

pub(crate) fn build(
    query: &ScopedQuery<'_>,
    actor: i64,
    customer_scope: &DataScope<'_>,
    pg: bool,
) -> Result<(QuerySql, String)> {
    let (mut statement, mut projection) = generic_query::scoped(query, pg)?;
    if query.kind == "crm-follow-ups" {
        let allowed = customer_scope.filter_for(&mut statement.values, pg, "c");
        let parent = sql::field("crmCustomerId", pg);
        let relation =
            format!("c.kind='crm-customers' AND c.id=CAST({parent} AS BIGINT) AND ({allowed})");
        statement.filter += &format!(" AND EXISTS(SELECT 1 FROM records c WHERE {relation})");
        let name = sql::field_from("c.body", "name", pg);
        let name = format!("(SELECT {name} FROM records c WHERE {relation})");
        projection = if pg {
            format!("(({projection})::jsonb || jsonb_build_object('customerName',{name}))::text")
        } else {
            format!("json_set({projection},'$.customerName',{name})")
        };
    }
    let status = sql::field("status", pg);
    let candidate = match query.kind {
        "invoices" | "crm-follow-ups" => {
            let owner = sql::bind(&mut statement.values, actor, pg);
            let state = if query.kind == "invoices" {
                format!("{status}='Draft'")
            } else if pg {
                "COALESCE(r.body->>'isCompleted','false')<>'true'".into()
            } else {
                "COALESCE(json_extract(r.body,'$.isCompleted'),0)<>1".into()
            };
            format!("r.owner_id=CAST({owner} AS BIGINT) AND {state}")
        }
        "bookings" => format!("{status} IN ('Pending','Approved','InUse')"),
        "supply-requests" => format!("{status} IN ('Pending','Approved','Issued')"),
        "people" => format!("COALESCE({status},'')<>'Departed'"),
        _ => return Err(crate::Error::unavailable("未知待办来源。")),
    };
    statement.filter += &format!(" AND ({candidate})");
    Ok((statement, projection))
}
