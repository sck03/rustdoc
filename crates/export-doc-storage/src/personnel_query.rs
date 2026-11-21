//! Bounded personnel projections, ordered by the immutable normalized employee number.
use crate::{DataScope, QuerySql, Result, sql};

pub struct PersonnelQuery<'a> {
    pub as_of: &'a str,
    pub company: &'a str,
    pub department: &'a str,
    pub status: &'a str,
    pub attention_through: Option<&'a str>,
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
    let membership =
        crate::personnel_memberships::filter(&mut values, pg, &company, None, Some(q.as_of), "r");
    let mut clauses = vec![format!("r.kind='people' AND ({membership})")];
    for scope in q.scopes {
        clauses.push(scope.filter(&mut values, pg));
    }
    if !q.department.is_empty() {
        let department = sql::bind(&mut values, q.department, pg);
        clauses.push(crate::personnel_memberships::filter(
            &mut values,
            pg,
            &company,
            Some(&department),
            Some(q.as_of),
            "r",
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
    if let Some(through) = q.attention_through {
        let through = sql::bind(&mut values, through, pg);
        let profile = "SELECT p.body FROM personnel_private_profiles p WHERE p.person_id=r.id";
        let private = |key| sql::field_from("p.body", key, pg);
        let long_term = format!(
            "(SELECT {} FROM personnel_private_profiles p WHERE p.person_id=r.id)",
            sql::json_field_from("p.body", "identityLongTerm", pg)
        );
        let identity_date = format!(
            "(SELECT {} FROM personnel_private_profiles p WHERE p.person_id=r.id)",
            private("identityValidUntil")
        );
        let mut due = Vec::new();
        for (enabled, date) in [
            (
                format!("{status}='Probation'"),
                sql::field("probationEndsOn", pg),
            ),
            ("TRUE".into(), sql::field("contractEndsOn", pg)),
            (
                format!("COALESCE({long_term},'false')<>'true'"),
                identity_date,
            ),
        ] {
            due.push(format!("CASE WHEN {enabled} AND COALESCE({date},'')<>'' THEN CASE WHEN {}<={} THEN 1 ELSE 0 END ELSE 0 END", strict_date(&date, pg), if pg {format!("({through})::date")} else {through.clone()}));
        }
        // Arithmetic evaluates each applicable date, including malformed dates outside the page.
        // A missing private profile must fail closed rather than disappear from the count.
        let attention = format!(
            "CASE WHEN NOT EXISTS({profile}) THEN {} IS NULL WHEN {status}='Departed' THEN FALSE ELSE ({})>0 END",
            strict_date(
                "('missing personnel private profile: ' || CAST(r.id AS TEXT))",
                pg
            ),
            due.join(" + ")
        );
        // SQL optimizers may reorder AND predicates. Validate private dates only after
        // the complete authorized selection, including keyword and department filters.
        clauses.push(format!(
            "CASE WHEN {} THEN {attention} ELSE FALSE END",
            clauses.join(" AND ")
        ));
    }
    Ok((
        QuerySql {
            source: crate::query_source::Source::Projected,
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

fn strict_date(value: &str, pg: bool) -> String {
    if pg {
        format!(
            "(CASE WHEN {value} ~ '^[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}$' THEN {value} ELSE 'invalid personnel date: '||COALESCE({value},'') END)::date"
        )
    } else {
        format!("record_date({value})")
    }
}
