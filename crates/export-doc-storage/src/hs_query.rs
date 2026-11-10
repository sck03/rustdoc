//! HS ordering/projection is storage-owned; semantic synonym matching remains in Domain.
use crate::{
    Error, QuerySql, Result,
    sql::{self, bind, field},
};

pub struct HsQuery<'a> {
    pub kind: &'a str,
    pub current_year: Option<i32>,
    pub prefix: &'a str,
    pub since: &'a str,
    pub ranked_examples: bool,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}

/// Preserve catalog ordering while seeking past the last row, without OFFSET rescans.
pub(crate) fn scan(
    db: &(impl crate::Adapter + ?Sized),
    query: &HsQuery<'_>,
    after: Option<&serde_json::Value>,
) -> Result<(Option<serde_json::Value>, Vec<serde_json::Value>)> {
    let pg = db.provider() == "PostgreSQL";
    let (mut statement, projection) = build(query, pg)?;
    let keys = sort_keys(query, pg);
    if let Some(after) = after {
        let after = after
            .as_array()
            .filter(|v| v.len() == keys.len())
            .ok_or_else(|| Error::unavailable("HS 游标无效。"))?;
        let mut equal = Vec::new();
        let mut choices = Vec::new();
        for ((key, desc, numeric), value) in keys.iter().zip(after) {
            let value = if *numeric {
                value.as_i64().map(|v| v.to_string())
            } else {
                value.as_str().map(str::to_owned)
            }
            .ok_or_else(|| Error::unavailable("HS 游标类型无效。"))?;
            let value = bind(&mut statement.values, value, pg);
            let value = if *numeric {
                format!("CAST({value} AS BIGINT)")
            } else {
                value
            };
            let comparison = format!("{key}{}{value}", if *desc { "<" } else { ">" });
            choices.push(format!(
                "({})",
                equal
                    .iter()
                    .cloned()
                    .chain(std::iter::once(comparison))
                    .collect::<Vec<_>>()
                    .join(" AND ")
            ));
            equal.push(format!("{key}={value}"));
        }
        statement.filter += &format!(" AND ({})", choices.join(" OR "));
    }
    let keys = keys
        .iter()
        .map(|(key, _, _)| key.as_str())
        .collect::<Vec<_>>()
        .join(",");
    let projection = if pg {
        format!(
            "jsonb_build_object('row',({projection})::jsonb,'cursor',jsonb_build_array({keys}))::text"
        )
    } else {
        format!("json_object('row',json({projection}),'cursor',json_array({keys}))")
    };
    let mut rows = db.query_slice(&statement, &projection, 0, query.limit)?;
    let cursor = rows.last().map(|row| row["cursor"].clone());
    for row in &mut rows {
        *row = row["row"].take();
    }
    Ok((cursor, rows))
}

// Keep both reads on the exact expressions covered by the catalog indexes.
fn sort_keys(query: &HsQuery<'_>, pg: bool) -> Vec<(String, bool, bool)> {
    let verified = if pg {
        "CASE WHEN r.body->>'isManuallyVerified'='true' THEN 0 ELSE 1 END"
    } else {
        "CASE WHEN json_extract(r.body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END"
    };
    let mut keys: Vec<(String, bool, bool)> = match query.kind {
        "hs-codes" => vec![(
            format!(
                "{} COLLATE {}",
                field("code", pg),
                if pg { "\"C\"" } else { "BINARY" }
            ),
            false,
            false,
        )],
        "hs-examples" => {
            let mut keys = vec![(verified.into(), false, true)];
            if query.ranked_examples {
                keys.push((
                    if pg {
                        format!("COALESCE(CAST({} AS BIGINT),0)", field("useCount", pg))
                    } else {
                        format!("COALESCE({},0)", field("useCount", pg))
                    },
                    true,
                    true,
                ));
            }
            keys.push((field("updatedAt", pg), true, false));
            keys
        }
        "hs-candidates" => vec![(field("lastSeenAt", pg), true, false)],
        _ => vec![],
    };
    keys.push(("r.id".into(), query.kind == "hs-candidates", true));
    keys
}

pub(crate) fn build(query: &HsQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    if !matches!(
        query.kind,
        "hs-codes" | "hs-examples" | "hs-feedback" | "hs-replacements" | "hs-candidates"
    ) || query.offset < 0
        || !(1..=2000).contains(&query.limit)
    {
        return Err(Error::unavailable("HS 查询边界无效。"));
    }
    let mut values = vec![];
    let mut clauses = vec![format!("r.kind={}", bind(&mut values, query.kind, pg))];
    if !query.since.is_empty() {
        let updated = field(
            if query.kind == "hs-codes" {
                "updateTime"
            } else {
                "updatedAt"
            },
            pg,
        );
        let since = bind(&mut values, query.since, pg);
        let (seconds, nanos) = sql::instant_order(&updated, pg);
        let (since_seconds, since_nanos) = sql::instant_order(&since, pg);
        clauses.push(format!("CASE WHEN {updated} IS NULL THEN TRUE ELSE ({seconds},{nanos})>=({since_seconds},{since_nanos}) END"));
    }
    if let Some(year) = query.current_year {
        clauses.push(format!(
            "{}='active' AND CAST({} AS TEXT)={}",
            format!("lower({})", field("status", pg)),
            field("effectiveYear", pg),
            bind(&mut values, year, pg)
        ));
    }
    if !query.prefix.is_empty() {
        let paths = if query.kind == "hs-examples" {
            vec!["rawReportedHsCode", "resolvedCurrentHsCode"]
        } else {
            vec!["code"]
        };
        clauses.push(format!(
            "({})",
            paths
                .iter()
                .map(|path| sql::prefix(&field(path, pg), query.prefix, &mut values, pg))
                .collect::<Vec<_>>()
                .join(" OR ")
        ));
    }
    let order = sort_keys(query, pg)
        .iter()
        .map(|(key, desc, _)| format!("{key} {}", if *desc { "DESC" } else { "ASC" }))
        .collect::<Vec<_>>()
        .join(",");
    Ok((
        QuerySql {
            source: crate::query_source::Source::Records,
            filter: clauses.join(" AND "),
            order,
            values,
        },
        sql::projection(query.fields, pg)?,
    ))
}
