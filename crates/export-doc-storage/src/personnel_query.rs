//! Bounded personnel projections, ordered by the immutable normalized employee number.
use crate::{DataScope, QuerySql, Result, sql};

pub struct PersonnelQuery<'a> {
    pub company: &'a str,
    pub department: &'a str,
    pub status: &'a str,
    pub keyword: &'a str,
    pub search_fields: &'a [&'a str],
    /// Each scope is required; alternatives within a scope remain a union.
    pub scopes: &'a [DataScope<'a>],
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}

pub(crate) fn build(q: &PersonnelQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = Vec::new();
    let company = sql::bind(&mut values, q.company, pg);
    let mut clauses = vec![format!("r.kind='people' AND r.company={company}")];
    for scope in q.scopes {
        clauses.push(scope.filter(&mut values, pg));
    }
    if !q.department.is_empty() {
        clauses.push(crate::generic_query::department_filter(
            &sql::bind(&mut values, q.department, pg),
            true,
            pg,
        ));
    }
    let status = sql::field("status", pg);
    clauses.push(if q.status.is_empty() {
        format!("{status}<>'Departed'")
    } else {
        format!("{status}={}", sql::bind(&mut values, q.status, pg))
    });
    if !q.keyword.is_empty() {
        let fields = q
            .search_fields
            .iter()
            .map(|field| {
                sql::valid_path(field)?;
                Ok(format!("COALESCE({},'')", sql::field(field, pg)))
            })
            .collect::<Result<Vec<_>>>()?;
        let text = format!("({})", fields.join(" || ' ' || "));
        let keyword = sql::bind(&mut values, q.keyword, pg);
        clauses.push(sql::contains(&text, &keyword, pg));
    }
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order: if pg {
                "r.identity COLLATE \"C\" ASC, r.id ASC"
            } else {
                "r.identity ASC, r.id ASC"
            }
            .into(),
            values,
        },
        sql::projection(q.fields, pg)?,
    ))
}
