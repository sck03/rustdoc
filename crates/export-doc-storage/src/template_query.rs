//! Template reads project metadata before crossing the storage boundary.
use super::QuerySql;
pub(super) use crate::sql::field;
use crate::sql::{bind, normalized};

#[derive(Default)]
pub struct TemplateAudience<'a> {
    pub user_id: i64,
    pub company: &'a str,
    pub department: &'a str,
    pub departments: &'a [String],
    pub administrator: bool,
    pub can_view: bool,
    pub shared: bool,
}

pub struct ReportTemplateQuery<'a> {
    pub audience: TemplateAudience<'a>,
    pub report_type: &'a str,
    pub include_archived: bool,
    pub keyword: &'a str,
    pub exact_name: bool,
    pub status: &'a str,
    pub usable_only: bool,
    pub offset: i64,
    pub limit: i64,
}

pub struct TemplateVersionQuery<'a> {
    pub kind: &'a str,
    pub template_id: i64,
    pub offset: i64,
    pub limit: i64,
}

pub struct EmailTemplateQuery<'a> {
    pub audience: TemplateAudience<'a>,
    pub edit_scope: crate::DataScope<'a>,
    pub include_archived: bool,
    pub keyword: &'a str,
    pub category: &'a str,
    pub scope: &'a str,
    pub offset: i64,
    pub limit: i64,
}

pub(super) fn email_catalog(q: &EmailTemplateQuery<'_>, pg: bool) -> QuerySql {
    let mut values = Vec::new();
    let mut clauses = vec!["r.kind='email-templates'".into()];
    if !q.include_archived {
        clauses.push(format!("{}<>'Archived'", field("status", pg)));
    }
    if !q.category.is_empty() {
        clauses.push(format!(
            "{}={}",
            field("category", pg),
            bind(&mut values, q.category, pg)
        ));
    }
    if !q.keyword.is_empty() {
        let keyword = bind(&mut values, q.keyword, pg);
        clauses.push(format!(
            "({})",
            ["name", "subject", "bodyHtml"]
                .map(|key| crate::sql::contains(&field(key, pg), &keyword, pg))
                .join(" OR ")
        ));
    }
    if q.scope == "editable" {
        clauses.push(format!("({})", q.edit_scope.filter(&mut values, pg)));
        clauses.push(format!(
            "r.owner_id=CAST({} AS BIGINT) AND {}<>'Archived'",
            bind(&mut values, q.audience.user_id, pg),
            field("status", pg)
        ));
    } else if q.scope == "shared" {
        clauses.push(format!("{}<>'Private'", field("shareScope", pg)));
    }
    audience_filter(&q.audience, pg, &mut values, &mut clauses);
    let collation = if pg { "\"C\"" } else { "BINARY" };
    QuerySql {
        source: crate::query_source::Source::Records,
        filter: clauses.join(" AND "),
        values,
        order: format!(
            "{} COLLATE {collation},{} COLLATE {collation},r.id",
            field("category", pg),
            field("name", pg)
        ),
    }
}

pub(crate) fn email_name_exists(
    db: &(impl crate::Adapter + ?Sized),
    owner: i64,
    id: i64,
    name: &str,
    category: &str,
) -> crate::Result<bool> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = Vec::new();
    let owner = bind(&mut values, owner, pg);
    let id = bind(&mut values, id, pg);
    let name = bind(&mut values, name, pg);
    let category = bind(&mut values, category, pg);
    let sql = QuerySql {
        source: crate::query_source::Source::Records,
        order: "r.id".into(),
        filter: format!(
            "r.kind='email-templates' AND r.owner_id=CAST({owner} AS BIGINT) AND r.id<>CAST({id} AS BIGINT) AND {}<>'Archived' AND {}={} AND {}={}",
            field("status", pg),
            normalized(&field("name", pg), pg),
            normalized(&name, pg),
            normalized(&field("category", pg), pg),
            normalized(&category, pg)
        ),
        values,
    };
    Ok(!db
        .query_slice(&sql, &crate::sql::projection(&[("id", "id")], pg)?, 0, 1)?
        .is_empty())
}

pub(super) fn catalog(q: &ReportTemplateQuery<'_>, postgres: bool) -> QuerySql {
    let mut values = Vec::new();
    let mut clauses = vec!["r.kind='report-templates'".to_owned()];
    let f = |path| field(path, postgres);
    clauses.push(format!(
        "{}={}",
        f("reportType"),
        bind(&mut values, q.report_type, postgres)
    ));
    if !q.include_archived {
        clauses.push(format!("{}<>'Archived'", f("status")));
    }
    if !q.status.is_empty() {
        clauses.push(format!(
            "{}={}",
            f("status"),
            bind(&mut values, q.status, postgres)
        ));
    }
    if q.exact_name || !q.keyword.is_empty() {
        let keyword = bind(&mut values, q.keyword, postgres);
        let name = f("name");
        let (name, keyword) = (normalized(&name, postgres), normalized(&keyword, postgres));
        clauses.push(if q.exact_name {
            format!("{name}={keyword}")
        } else if postgres {
            format!("strpos({name},{keyword})>0")
        } else {
            format!("instr({name},{keyword})>0")
        });
    }
    let a = &q.audience;
    if q.usable_only {
        let owner = bind(&mut values, a.user_id, postgres);
        clauses.push(format!("((r.owner_id=CAST({owner} AS BIGINT) AND {} IN ('Draft','Published')) OR ({}='Published' AND {}<>'Private'))", f("status"), f("status"), f("shareScope")));
    }
    audience_filter(a, postgres, &mut values, &mut clauses);
    QuerySql {
        source: crate::query_source::Source::Records,
        filter: clauses.join(" AND "),
        values,
        order: if postgres {
            "(r.body->>'status'='Published') DESC, (r.body->>'name') COLLATE \"C\", r.id"
        } else {
            "(json_extract(r.body,'$.status')='Published') DESC, json_extract(r.body,'$.name') COLLATE BINARY, r.id"
        }.into(),
    }
}
pub(crate) fn audience_filter(
    a: &TemplateAudience<'_>,
    postgres: bool,
    values: &mut Vec<String>,
    clauses: &mut Vec<String>,
) {
    let f = |path| field(path, postgres);
    if !a.administrator {
        if !a.can_view {
            clauses.push("1=0".into());
        }
        let owner = bind(values, a.user_id, postgres);
        let owner = format!("r.owner_id=CAST({owner} AS BIGINT)");
        let mut shared = vec![format!("{}='All'", f("shareScope"))];
        if a.shared && !a.company.is_empty() {
            let company = bind(values, a.company, postgres);
            shared.push(format!(
                "({}='Company' AND r.company={company})",
                f("shareScope")
            ));
            if !a.department.is_empty() {
                let department = std::iter::once(a.department)
                    .chain(a.departments.iter().map(String::as_str))
                    .map(|department| bind(values, department, postgres))
                    .collect::<Vec<_>>()
                    .join(",");
                shared.push(format!(
                    "({}='Department' AND r.company={company} AND r.department IN ({department}))",
                    f("shareScope")
                ));
            }
        }
        clauses.push(if a.shared {
            format!(
                "({owner} OR ({}='Published' AND ({})))",
                f("status"),
                shared.join(" OR ")
            )
        } else {
            owner
        });
    }
}

pub(super) fn versions(q: &TemplateVersionQuery<'_>, postgres: bool) -> QuerySql {
    let mut values = Vec::new();
    let kind = bind(&mut values, q.kind, postgres);
    let id = bind(&mut values, q.template_id, postgres);
    QuerySql {
        source: crate::query_source::Source::Records,
        filter: format!(
            "r.kind='template-versions' AND {}={kind} AND {}=CAST({id} AS BIGINT)",
            field("templateKind", postgres),
            if postgres {
                format!("CAST({} AS BIGINT)", field("templateId", true))
            } else {
                field("templateId", false)
            }
        ),
        values,
        order: if postgres {
            "CAST(r.body #>> '{content,versionNumber}' AS BIGINT) DESC, r.id"
        } else {
            "json_extract(r.body,'$.content.versionNumber') DESC, r.id"
        }
        .into(),
    }
}

pub(super) fn metadata(postgres: bool, history: bool) -> String {
    let fields = if history {
        &[
            "id",
            "templateId",
            "changeType",
            "changedBy",
            "createdAt",
            "content.versionNumber",
            "content.name",
            "content.status",
            "content.shareScope",
        ][..]
    } else {
        &[
            "id",
            "reportType",
            "name",
            "status",
            "shareScope",
            "versionNumber",
            "ownerUserId",
            "companyScope",
            "departmentId",
        ][..]
    };
    let pairs = fields
        .iter()
        .map(|path| {
            let key = path.rsplit('.').next().unwrap();
            let expression = if postgres {
                format!("r.body #> '{{{}}}'", path.replace('.', ","))
            } else {
                field(path, false)
            };
            format!("'{key}',{expression}")
        })
        .collect::<Vec<_>>()
        .join(",");
    if postgres {
        format!("jsonb_build_object({pairs})::text")
    } else {
        format!("json_object({pairs})")
    }
}
