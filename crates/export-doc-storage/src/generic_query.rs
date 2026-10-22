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
    pub departments: &'a [String],
    pub personnel_memberships: bool,
    pub user_id: i64,
}
impl DataScope<'_> {
    pub(crate) fn filter(&self, values: &mut Vec<String>, pg: bool) -> String {
        if self.all {
            return "1=1".into();
        }
        let company = bind(values, self.company, pg);
        let mut alternatives = Vec::new();
        if self.company_wide {
            alternatives.push(format!("r.company={company}"));
        }
        if self.department_wide {
            let departments = std::iter::once(self.department)
                .chain(self.departments.iter().map(String::as_str))
                .map(|department| bind(values, department, pg))
                .collect::<Vec<_>>()
                .join(",");
            let membership = department_filter(&departments, self.personnel_memberships, pg);
            alternatives.push(format!("(r.company={company} AND ({membership}))"));
        }
        if self.own {
            let owner = bind(values, self.user_id, pg);
            alternatives.push(format!(
                "(r.company={company} AND r.owner_id=CAST({owner} AS BIGINT))"
            ));
        }
        if alternatives.is_empty() {
            format!("({company} IS NOT NULL AND 1=0)")
        } else {
            format!("({})", alternatives.join(" OR "))
        }
    }
}
pub(crate) fn department_filter(parameters: &str, personnel: bool, pg: bool) -> String {
    let direct = format!("r.department IN ({parameters})");
    if !personnel {
        return direct;
    }
    let assignments = if pg {
        "jsonb_array_elements(COALESCE(r.body->'organization'->'additionalAssignments','[]'::jsonb)) assignment"
    } else {
        "json_each(r.body,'$.organization.additionalAssignments') assignment"
    };
    let department = if pg {
        "assignment->>'departmentId'"
    } else {
        "assignment.value->>'departmentId'"
    };
    format!(
        "({direct} OR EXISTS (SELECT 1 FROM {assignments} WHERE {department} IN ({parameters})))"
    )
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
    clauses.push(q.scope.filter(&mut values, pg));
    for (name, value) in q.filters {
        sql::valid_path(name)?;
        let expression = field(name, pg);
        let expression = if pg {
            expression
        } else if matches!(value.as_str(), "true" | "false") {
            format!(
                "CASE json_type(r.body,'$.{name}') WHEN 'true' THEN 'true' WHEN 'false' THEN 'false' ELSE CAST({expression} AS TEXT) END"
            )
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
