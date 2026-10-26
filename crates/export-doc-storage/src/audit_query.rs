//! Audit filtering stays in storage; only the selected events leave the database.
use crate::{QuerySql, sql};
use chrono::{DateTime, Utc};
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
}
pub(crate) fn build(query: &AuditQuery<'_>, pg: bool) -> (QuerySql, String) {
    let mut values = Vec::new();
    let names = query
        .entities
        .iter()
        .filter(|_| {
            !query.entity.is_empty() || !query.keyword.is_empty() || !query.invoice.is_empty()
        })
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
        (
            format!(
                "CASE WHEN ({entity})='Invoice' THEN CAST(r.record_id AS TEXT) ELSE '' END||' '||({old})||' '||({new})"
            ),
            query.invoice,
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
            filter: filters.join(" AND "),
            order: "r.id DESC".into(),
            values,
        },
        projection,
    )
}
