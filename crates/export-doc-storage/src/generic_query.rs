use crate::{
    QuerySql, Result,
    sql::{self, bind, field},
};

/// Permission alternatives are a union: own records can be outside the actor's
/// current department, so reducing grants to only the largest rank is incorrect.
#[derive(Default)]
pub struct DataScope<'a> {
    pub restrictions: Vec<ScopeFilter>,
    pub all: bool,
    pub company_wide: bool,
    pub department_wide: bool,
    pub own: bool,
    pub company: &'a str,
    pub department: &'a str,
    pub departments: &'a [String],
    pub personnel_memberships: bool,
    pub personnel_date: Option<&'a str>,
    pub user_id: i64,
}
#[derive(Clone)]
pub enum ScopeFilter {
    Deny,
    Units(Vec<String>),
    Departments(Vec<(String, String)>),
    Projects(Vec<String>),
}
impl DataScope<'_> {
    pub(crate) fn filter(&self, values: &mut Vec<String>, pg: bool) -> String {
        let base = self.base_filter(values, pg);
        let mut clauses = vec![format!("({base})")];
        for rule in &self.restrictions {
            clauses.push(match rule {
                ScopeFilter::Deny => "1=0".into(),
                ScopeFilter::Units(units) => {
                    if units.is_empty() {
                        "1=0".into()
                    } else {
                        format!(
                            "r.company IN ({})",
                            units
                                .iter()
                                .map(|unit| bind(values, unit, pg))
                                .collect::<Vec<_>>()
                                .join(",")
                        )
                    }
                }
                ScopeFilter::Departments(departments) => {
                    if departments.is_empty() {
                        "1=0".into()
                    } else {
                        format!(
                            "({})",
                            departments
                                .iter()
                                .map(|(unit, department)| {
                                    let u = bind(values, unit, pg);
                                    let d = bind(values, department, pg);
                                    format!("(r.company={u} AND r.department={d})")
                                })
                                .collect::<Vec<_>>()
                                .join(" OR ")
                        )
                    }
                }
                ScopeFilter::Projects(projects) => {
                    if projects.is_empty() {
                        "1=0".into()
                    } else {
                        format!(
                            "{} IN ({})",
                            field("projectId", pg),
                            projects
                                .iter()
                                .map(|project| bind(values, project, pg))
                                .collect::<Vec<_>>()
                                .join(",")
                        )
                    }
                }
            });
        }
        clauses.join(" AND ")
    }
    fn base_filter(&self, values: &mut Vec<String>, pg: bool) -> String {
        if self.all {
            return "1=1".into();
        }
        if !self.company_wide && !self.department_wide && !self.own {
            return "1=0".into();
        }
        let company = bind(values, self.company, pg);
        let mut alternatives = Vec::new();
        if self.company_wide {
            alternatives.push(if self.personnel_memberships {
                crate::personnel_memberships::filter(
                    values,
                    pg,
                    &company,
                    None,
                    self.personnel_date,
                )
            } else {
                format!("r.company={company}")
            });
        }
        if self.department_wide {
            let departments = std::iter::once(self.department)
                .chain(self.departments.iter().map(String::as_str))
                .map(|department| bind(values, department, pg))
                .collect::<Vec<_>>()
                .join(",");
            let membership = if self.personnel_memberships {
                crate::personnel_memberships::filter(
                    values,
                    pg,
                    &company,
                    Some(&departments),
                    self.personnel_date,
                )
            } else {
                format!("r.company={company} AND r.department IN ({departments})")
            };
            alternatives.push(format!("({membership})"));
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
/// An aggregate may require multiple actions; all scopes must allow each row.
pub struct ScopedQuery<'a> {
    pub kind: &'a str,
    pub scopes: &'a [DataScope<'a>],
    pub company: Option<&'a str>,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}
pub(crate) fn scoped(q: &ScopedQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = vec![];
    let mut clauses = vec![format!("r.kind={}", sql::bind(&mut values, q.kind, pg))];
    for scope in q.scopes {
        clauses.push(scope.filter(&mut values, pg));
    }
    if let Some(company) = q.company {
        clauses.push(format!("r.company={}", sql::bind(&mut values, company, pg)));
    }
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order: "r.id DESC".into(),
            values,
        },
        sql::projection(q.fields, pg)?,
    ))
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
            order: "r.id DESC".into(),
            values,
        },
        sql::projection(q.fields, pg)?,
    ))
}
