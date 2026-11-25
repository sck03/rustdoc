//! Exact company-scoped candidate lookup; ambiguity is a business-layer decision.
use crate::{Adapter, QuerySql, Result, sql};
use serde_json::Value;

/// Match only imported candidate names, with bounded parameters and no directory materialization.
pub(crate) fn existing_names(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    scope: &crate::DataScope<'_>,
    names: &[String],
) -> Result<Vec<String>> {
    let pg = db.provider() == "PostgreSQL";
    if names.is_empty() {
        return Ok(vec![]);
    }
    if names.len() > 5000 {
        return Err(crate::Error::unavailable("导入候选名称超过容量限制。"));
    }
    let mut values = vec![];
    let visible = scope.filter(&mut values, pg);
    let kind = sql::bind(&mut values, kind, pg);
    let name = sql::normalized(&sql::field("name", pg), pg);
    // A single JSON parameter avoids repeated directory scans and SQL parameter-count limits.
    let candidates = sql::bind(&mut values, serde_json::to_string(names)?, pg);
    let source = if pg {
        format!("jsonb_array_elements_text({candidates}::jsonb) AS candidate(value)")
    } else {
        format!("json_each({candidates}) AS candidate")
    };
    let candidate = sql::normalized("candidate.value", pg);
    let query = QuerySql {
        source: crate::query_source::Source::Records,
        filter: format!(
            "r.kind={kind} AND {visible} AND {name} IN (SELECT {candidate} FROM {source})"
        ),
        order: String::new(),
        values,
    };
    let aggregate = if pg {
        format!("COALESCE(jsonb_agg(DISTINCT {name}),'[]'::jsonb)::text")
    } else {
        format!("json_group_array(DISTINCT {name})")
    };
    Ok(serde_json::from_value(db.query_json(
        &crate::query_source::aggregate(&query, &aggregate),
        &query.values,
    )?)?)
}

/// Only project names and stop at the second distinct authorized name. Keyset
/// pages bound memory even when a large directory contains one repeated name.
pub(crate) fn sole_exporter_name(
    db: &(impl Adapter + ?Sized),
    scope: &crate::DataScope<'_>,
) -> Result<Option<String>> {
    let query = crate::GenericQuery {
        kind: "exporters",
        scope: scope.clone(),
        fields: &[("name", "exporterNameCN")],
        limit: 256,
        ..Default::default()
    };
    let mut before = None;
    let mut name: Option<String> = None;
    loop {
        crate::operation::check()?;
        let (next, rows) = db.scan_generic(&query, before)?;
        if rows.is_empty() {
            return Ok(name);
        }
        for row in rows {
            let candidate = row["name"].as_str().unwrap_or("").trim();
            if candidate.is_empty() {
                continue;
            }
            match &name {
                Some(name) if !name.eq_ignore_ascii_case(candidate) => return Ok(None),
                None => name = Some(candidate.to_owned()),
                _ => {}
            }
        }
        before = next;
    }
}

pub(crate) fn candidates(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    company: &str,
    keys: &[(&str, String)],
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let kind = sql::bind(&mut values, kind, pg);
    let company = sql::bind(&mut values, company, pg);
    let mut matches = vec![];
    for (field, value) in keys.iter().filter(|(_, v)| !v.is_empty()) {
        sql::valid_path(field)?;
        let field = sql::normalized(&format!("COALESCE({},'')", sql::field(field, pg)), pg);
        let value = sql::normalized(&sql::bind(&mut values, value, pg), pg);
        matches.push(format!("{field}={value}"));
    }
    if matches.is_empty() {
        return Ok(vec![]);
    }
    db.query_slice(
        &QuerySql {
            source: crate::query_source::Source::Records,
            filter: format!(
                "r.kind={kind} AND r.company={company} AND ({})",
                matches.join(" OR ")
            ),
            order: "r.id DESC".into(),
            values,
        },
        if pg { "r.body::text" } else { "r.body" },
        0,
        2,
    )
}
