//! Template reads project metadata before crossing the storage boundary.
use super::QuerySql;
pub(super) use crate::sql::field;
use crate::sql::{bind, normalized};

#[derive(Default)]
pub struct TemplateAudience<'a> {
    pub user_id: i64,
    pub company: &'a str,
    pub department: &'a str,
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
    if !a.administrator {
        if !a.can_view {
            clauses.push("1=0".into());
        }
        let owner = bind(&mut values, a.user_id, postgres);
        let owner = format!("r.owner_id=CAST({owner} AS BIGINT)");
        let mut shared = vec![format!("{}='All'", f("shareScope"))];
        if a.shared && !a.company.is_empty() {
            let company = bind(&mut values, a.company, postgres);
            shared.push(format!(
                "({}='Company' AND r.company={company})",
                f("shareScope")
            ));
            if !a.department.is_empty() {
                let department = bind(&mut values, a.department, postgres);
                shared.push(format!(
                    "({}='Department' AND r.company={company} AND r.department={department})",
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
    QuerySql {
        filter: clauses.join(" AND "),
        values,
        order: if postgres {
            "(r.body->>'status'='Published') DESC, (r.body->>'name') COLLATE \"C\", r.id"
        } else {
            "(json_extract(r.body,'$.status')='Published') DESC, json_extract(r.body,'$.name') COLLATE BINARY, r.id"
        },
    }
}

pub(super) fn versions(q: &TemplateVersionQuery<'_>, postgres: bool) -> QuerySql {
    let mut values = Vec::new();
    let kind = bind(&mut values, q.kind, postgres);
    let id = bind(&mut values, q.template_id, postgres);
    QuerySql {
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
        },
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
