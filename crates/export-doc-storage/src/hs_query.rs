//! HS ordering/projection is storage-owned; semantic synonym matching remains in Domain.
use crate::{
    Error, QuerySql, Result,
    sql::{self, bind, field},
};

pub struct HsQuery<'a> {
    pub kind: &'a str,
    pub current_year: Option<i32>,
    pub prefix: &'a str,
    pub ranked_examples: bool,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}

pub(crate) fn build(query: &HsQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    if !matches!(
        query.kind,
        "hs-codes" | "hs-examples" | "hs-feedback" | "hs-replacements"
    ) || query.offset < 0
        || !(1..=2000).contains(&query.limit)
    {
        return Err(Error::unavailable("HS 查询边界无效。"));
    }
    let mut values = vec![];
    let mut clauses = vec![format!("r.kind={}", bind(&mut values, query.kind, pg))];
    if let Some(year) = query.current_year {
        clauses.push(format!(
            "{}='active' AND CAST({} AS TEXT)={}",
            format!("lower({})", field("status", pg)),
            field("effectiveYear", pg),
            bind(&mut values, year, pg)
        ));
    }
    if !query.prefix.is_empty() {
        let prefix = bind(&mut values, query.prefix, pg);
        let paths = if query.kind == "hs-examples" {
            vec!["rawReportedHsCode", "resolvedCurrentHsCode"]
        } else {
            vec!["code"]
        };
        clauses.push(format!(
            "({})",
            paths
                .iter()
                .map(|path| format!("substr({},1,length({prefix}))={prefix}", field(path, pg)))
                .collect::<Vec<_>>()
                .join(" OR ")
        ));
    }
    let order = if query.kind == "hs-codes" {
        if pg {
            "r.body->>'code' COLLATE \"C\", r.id"
        } else {
            "json_extract(r.body,'$.code') COLLATE BINARY, r.id"
        }
    } else if query.kind == "hs-examples" && query.ranked_examples {
        if pg {
            "CASE WHEN r.body->>'isManuallyVerified'='true' THEN 0 ELSE 1 END, COALESCE((r.body->>'useCount')::bigint,0) DESC, r.body->>'updatedAt' DESC, r.id"
        } else {
            "CASE WHEN json_extract(r.body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END, COALESCE(json_extract(r.body,'$.useCount'),0) DESC, json_extract(r.body,'$.updatedAt') DESC, r.id"
        }
    } else if query.kind == "hs-examples" {
        if pg {
            "CASE WHEN r.body->>'isManuallyVerified'='true' THEN 0 ELSE 1 END, r.body->>'updatedAt' DESC, r.id"
        } else {
            "CASE WHEN json_extract(r.body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END, json_extract(r.body,'$.updatedAt') DESC, r.id"
        }
    } else {
        "r.id"
    };
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order: order.into(),
            values,
        },
        sql::projection(query.fields, pg)?,
    ))
}
