//! Exact company-scoped candidate lookup; ambiguity is a business-layer decision.
use crate::{Adapter, QuerySql, Result, sql};
use serde_json::Value;

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
