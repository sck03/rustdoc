//! Audit filtering stays in storage; only the selected events leave the database.
use crate::{QuerySql, sql};
use chrono::{DateTime, Utc};
#[derive(Debug, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AuditStatistics {
    pub total_events: i64,
    pub oldest_event: Option<String>,
}

/// A single read snapshot returns exact totals and the chronologically oldest instant.
/// No audit bodies are decoded, and invalid timestamps retain the storage error.
pub(crate) fn statistics(pg: bool) -> String {
    let (seconds, nanos) = sql::instant_order("occurred_at", pg);
    format!(
        "WITH instants AS (SELECT id, occurred_at, {seconds} AS seconds, {nanos} AS nanos FROM audit_logs) SELECT (SELECT COUNT(*) FROM instants WHERE seconds IS NOT NULL AND nanos IS NOT NULL), (SELECT occurred_at FROM instants ORDER BY seconds, nanos, id LIMIT 1)"
    )
}

pub struct AuditQuery<'a> {
    pub invoice: &'a str,
    pub entity: &'a str,
    pub action: &'a str,
    pub user: &'a str,
    pub keyword: &'a str,
    pub start: Option<DateTime<Utc>>,
    pub end: Option<DateTime<Utc>>,
    pub entities: &'a [(&'a str, &'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
    pub count_all: bool,
    pub before_id: Option<i64>,
}
pub(crate) fn build(query: &AuditQuery<'_>, pg: bool) -> (QuerySql, String) {
    let mut values = Vec::new();
    let names = query
        .entities
        .iter()
        .filter(|_| !query.entity.is_empty() || !query.keyword.is_empty())
        .map(|(kind, name, _)| {
            let kind = sql::bind(&mut values, kind, pg);
            let name = sql::bind(&mut values, name, pg);
            format!("WHEN {kind} THEN {name}")
        })
        .collect::<Vec<_>>()
        .join(" ");
    let entity = if names.is_empty() {
        "r.kind".into()
    } else {
        format!("CASE r.kind {names} ELSE r.kind END")
    };
    let action =
        "CASE r.action WHEN 'delete' THEN 'Deleted' WHEN 'create' THEN 'Added' ELSE 'Modified' END";
    let old = if pg {
        "exportdoc_record_text(COALESCE(r.body->'oldValues','{}'::jsonb))"
    } else {
        "record_json(COALESCE(r.body->'oldValues','{}'))"
    };
    let new = if pg {
        "exportdoc_record_text(COALESCE(r.body->'newValues',jsonb_build_object('versionNumber',r.version)))"
    } else {
        "record_json(COALESCE(r.body->'newValues',json_object('versionNumber',r.version)))"
    };
    let mut filters = Vec::new();
    if let Some(id) = query.before_id {
        filters.push(format!(
            "r.id<CAST({} AS BIGINT)",
            sql::bind(&mut values, id, pg)
        ));
    }
    for (expression, value) in [(&entity[..], query.entity), (action, query.action)] {
        if !value.is_empty() {
            filters.push(format!(
                "({expression})={}",
                sql::bind(&mut values, value, pg)
            ));
        }
    }
    for (expression, value) in [
        ("CAST(r.actor_id AS TEXT)".to_owned(), query.user),
        (
            format!("({entity})||' '||r.record_id||' '||r.actor_id||' '||({old})||' '||({new})"),
            query.keyword,
        ),
    ] {
        if !value.is_empty() {
            filters.push(sql::contains(
                &expression,
                &sql::bind(&mut values, value, pg),
                pg,
            ));
        }
    }
    if !query.invoice.is_empty() {
        // Resolve the current business number without retaining unredacted text in audits.
        // Deleted invoices remain searchable through the other audit criteria.
        let number = sql::field_from("invoice.body", "invoiceNo", pg);
        let needle = sql::bind(&mut values, query.invoice, pg);
        let matches = sql::contains(&number, &needle, pg);
        filters.push(format!("r.kind='invoices' AND EXISTS (SELECT 1 FROM records invoice WHERE invoice.id=r.record_id AND invoice.kind='invoices' AND {matches})"));
    }
    let (seconds, nanos) = sql::instant_order("r.occurred_at", pg);
    // Parse selected timestamps even when the caller supplied no time range.
    filters.push(format!("({seconds}) IS NOT NULL AND ({nanos}) IS NOT NULL"));
    for (time, comparison) in [(query.start, ">="), (query.end, "<=")] {
        if let Some(time) = time {
            let second = sql::bind(&mut values, time.timestamp(), pg);
            let nano = sql::bind(&mut values, time.timestamp_subsec_nanos(), pg);
            filters.push(format!("(({seconds}),({nanos})){comparison}(CAST({second} AS BIGINT),CAST({nano} AS BIGINT))"));
        }
    }
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let body = if pg { "r.body" } else { "json(r.body)" };
    let projection = format!(
        "{object}('id',r.id,'kind',r.kind,'recordId',r.record_id,'versionNumber',r.version,'action',r.action,'actorUserId',r.actor_id,'timestamp',r.occurred_at,'body',{body})"
    );
    (
        QuerySql {
            source: crate::query_source::Source::Projected,
            filter: filters.join(" AND "),
            order: "r.id DESC".into(),
            values,
        },
        projection,
    )
}

pub(crate) fn ids(query: &AuditQuery<'_>, pg: bool) -> (String, Vec<String>) {
    let (sql, _) = build(query, pg);
    (
        format!(
            "SELECT r.id FROM audit_logs r WHERE {} ORDER BY {} LIMIT {} OFFSET {}",
            sql.filter,
            sql.order,
            query.limit.clamp(1, 2_000),
            query.offset.max(0)
        ),
        sql.values,
    )
}
