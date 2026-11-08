//! Select from authoritative data, and evaluate projections only for the page.
use crate::{GenericQuery, QuerySql};

#[derive(Clone, Copy)]
pub(crate) enum Source {
    Records,
    Projected,
    Workflow,
}
impl Source {
    pub(crate) fn for_kind(kind: &str) -> Self {
        if crate::identity_runtime::supports(kind) || crate::workflow_runtime::projects_body(kind) {
            Self::Projected
        } else {
            Self::Records
        }
    }
    fn table(self) -> &'static str {
        match self {
            Self::Records => "records",
            Self::Projected => "record_rows",
            Self::Workflow => "workflow_record_filters",
        }
    }
}
pub(crate) fn slice(sql: &QuerySql, projection: &str, offset: i64, limit: i64) -> String {
    let table = sql.source.table();
    let projected = if matches!(sql.source, Source::Workflow) {
        "workflow_list_records"
    } else {
        table
    };
    format!(
        "WITH selected AS MATERIALIZED (SELECT r.id FROM {table} r WHERE {filter} ORDER BY {order} LIMIT {limit} OFFSET {offset}) SELECT {projection} FROM selected p JOIN {projected} r ON r.id=p.id ORDER BY {order}",
        filter = sql.filter,
        order = sql.order,
        limit = limit.clamp(1, 2000),
        offset = offset.max(0)
    )
}
pub(crate) fn page(sql: &QuerySql, projection: &str, offset: i64, limit: i64, pg: bool) -> String {
    let selected = slice(sql, projection, offset, limit.clamp(1, 200));
    let aggregate = if pg {
        "COALESCE((SELECT jsonb_agg(page.body::jsonb) FROM page),'[]'::jsonb)::text"
    } else {
        "(SELECT json_group_array(json(page.body)) FROM page)"
    };
    format!(
        "WITH page(body) AS ({selected}) SELECT (SELECT COUNT(*) FROM {table} r WHERE {filter}), {aggregate}",
        table = sql.source.table(),
        filter = sql.filter
    )
}
pub(crate) fn generic(query: &GenericQuery<'_>, pg: bool) -> crate::Result<(String, Vec<String>)> {
    let (sql, projection) = crate::generic_query::build(query, pg)?;
    Ok((
        page(&sql, &projection, query.offset, query.limit, pg),
        sql.values,
    ))
}
pub(crate) fn identity(pg: bool) -> String {
    let marker = if pg { "$" } else { "?" };
    let body = if pg { "body::text" } else { "body" };
    format!("SELECT {body} FROM record_rows WHERE kind={marker}1 AND identity={marker}2")
}
pub(crate) fn aggregate(sql: &QuerySql, projection: &str) -> String {
    format!(
        "SELECT {projection} FROM {} r WHERE {}",
        sql.source.table(),
        sql.filter
    )
}
