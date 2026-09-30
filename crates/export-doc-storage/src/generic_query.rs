use crate::{
    QuerySql, Result,
    sql::{self, bind, field},
};

/// Permission alternatives are a union: own records can be outside the actor's
/// current department, so reducing grants to only the largest rank is incorrect.
#[derive(Default)]
pub struct DataScope<'a> {
    pub all: bool,
    pub company_wide: bool,
    pub department_wide: bool,
    pub own: bool,
    pub company: &'a str,
    pub department: &'a str,
    pub user_id: i64,
}
pub struct GenericQuery<'a> {
    pub kind: &'a str,
    pub scope: DataScope<'a>,
    pub filters: &'a [(&'a str, String)],
    pub keyword: &'a str,
    pub search_root: Option<&'a str>,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}
pub(crate) fn build(q: &GenericQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = Vec::new();
    let mut clauses = vec![format!("r.kind={}", bind(&mut values, q.kind, pg))];
    let scope = &q.scope;
    if !scope.all {
        let company = bind(&mut values, scope.company, pg);
        let mut alternatives = Vec::new();
        if scope.company_wide {
            alternatives.push(format!("r.company={company}"));
        }
        if scope.department_wide {
            let department = bind(&mut values, scope.department, pg);
            alternatives.push(format!(
                "(r.company={company} AND r.department={department})"
            ));
        }
        if scope.own {
            let owner = bind(&mut values, scope.user_id, pg);
            alternatives.push(format!(
                "(r.company={company} AND r.owner_id=CAST({owner} AS BIGINT))"
            ));
        }
        if alternatives.is_empty() {
            clauses.push(format!("({company} IS NOT NULL AND 1=0)"));
        } else {
            clauses.push(format!("({})", alternatives.join(" OR ")));
        }
    }
    for (name, value) in q.filters {
        sql::valid_path(name)?;
        let expression = field(name, pg);
        let expression = if pg {
            expression
        } else {
            format!("CAST({expression} AS TEXT)")
        };
        clauses.push(format!("{expression}={}", bind(&mut values, value, pg)));
    }
    if !q.keyword.is_empty() {
        let root = if let Some(root) = q.search_root {
            sql::valid_path(root)?;
            sql::json_field(root, pg)
        } else {
            "r.body".into()
        };
        // Ordinary words cannot span JSON delimiters. Only structured/escaped
        // searches need the compact canonical serializer on PostgreSQL.
        let text = if pg
            && !q
                .keyword
                .contains(['{', '}', '[', ']', ':', ',', '"', '\\'])
        {
            format!("({root})::text")
        } else if pg {
            format!("exportdoc_record_text({root})")
        } else {
            format!("record_json({root})")
        };
        let keyword = bind(&mut values, q.keyword, pg);
        clauses.push(sql::contains(&text, &keyword, pg));
    }
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order: "r.id DESC",
            values,
        },
        sql::projection(q.fields, pg)?,
    ))
}
