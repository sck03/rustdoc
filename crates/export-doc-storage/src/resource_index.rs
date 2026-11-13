//! Indexed resource ownership and references; catalogues never deserialize all
//! business documents merely to decide which image metadata may be returned.
use crate::{Adapter, DataScope, Error, QuerySql, Result, TemplateAudience, sql};
use serde_json::Value;
use std::collections::BTreeSet;
pub(crate) fn supports(kind: &str) -> bool {
    matches!(
        kind,
        "report-images" | "invoices" | "exporters" | "report-templates" | "template-versions"
    )
}
pub(crate) fn persist(
    db: &(impl Adapter + ?Sized),
    id: i64,
    kind: &str,
    body: &Value,
) -> Result<()> {
    if !supports(kind) {
        return Ok(());
    }
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let ident = sql::bind(&mut values, id, pg);
    let table = if kind == "report-images" {
        "report_resource_uploaders"
    } else {
        "report_resource_links"
    };
    let column = if kind == "report-images" {
        "resource_record_id"
    } else {
        "source_id"
    };
    db.execute_statement(
        &format!("DELETE FROM {table} WHERE {column}=CAST({ident} AS BIGINT)"),
        &values,
    )?;
    if kind == "report-images" {
        for user in body["uploaders"].as_array().into_iter().flatten() {
            let user = user
                .as_i64()
                .ok_or_else(|| Error::unavailable("图片归属编号无效。"))?;
            let mut values = vec![];
            let id = sql::bind(&mut values, id, pg);
            let user = sql::bind(&mut values, user, pg);
            db.execute_statement(&format!("INSERT INTO report_resource_uploaders(resource_record_id,user_id) VALUES(CAST({id} AS BIGINT),CAST({user} AS BIGINT)) ON CONFLICT DO NOTHING"),&values)?;
        }
        return Ok(());
    }
    let mut resources = BTreeSet::new();
    let mut owner = id;
    if matches!(kind, "report-templates" | "template-versions") {
        if kind == "template-versions" && body["templateKind"] != "report-templates" {
            return Ok(());
        }
        let content = if kind == "template-versions" {
            owner = body["templateId"]
                .as_i64()
                .ok_or_else(|| Error::unavailable("模板历史归属无效。"))?;
            &body["content"]["contentHtml"]
        } else {
            &body["contentHtml"]
        };
        if let Some(text) = content.as_str().filter(|s| !s.is_empty()) {
            let content: Value = serde_json::from_str(text)?;
            for resource in content["resources"].as_array().into_iter().flatten() {
                resources.insert(
                    resource["id"]
                        .as_str()
                        .ok_or_else(|| Error::unavailable("模板资源编号无效。"))?
                        .to_owned(),
                );
            }
        }
    } else {
        for (field, prefix) in [
            ("docSealPath", "Files/Seals/"),
            ("customsSealPath", "Files/Seals/"),
            ("shippingMarksImage", "Files/ShippingMarks/"),
        ] {
            if field == "shippingMarksImage"
                && (kind != "invoices" || body["shippingMarksType"] != "Image")
            {
                continue;
            }
            if let Some(path) = body[field].as_str().filter(|s| !s.is_empty()) {
                let resource = path
                    .strip_prefix(prefix)
                    .ok_or_else(|| Error::unavailable("图片引用路径无效。"))?;
                resources.insert(resource.to_owned());
            }
        }
    }
    for resource in resources {
        let mut values = vec![];
        let source = sql::bind(&mut values, id, pg);
        let owner = sql::bind(&mut values, owner, pg);
        let kind = sql::bind(&mut values, kind, pg);
        let resource = sql::bind(&mut values, resource, pg);
        db.execute_statement(&format!("INSERT INTO report_resource_links(source_id,owner_record_id,source_kind,resource_id) VALUES(CAST({source} AS BIGINT),CAST({owner} AS BIGINT),{kind},{resource})"),&values)?;
    }
    Ok(())
}

pub struct ResourceQuery<'a> {
    pub actor: i64,
    pub administrator: bool,
    pub invoices: DataScope<'a>,
    pub exporters: DataScope<'a>,
    pub templates: TemplateAudience<'a>,
    pub output_templates: TemplateAudience<'a>,
    pub report_types: &'a [&'a str],
    pub managed_resources: &'a [&'a str],
    pub keyword: &'a str,
    pub before_id: Option<i64>,
    pub offset: i64,
    pub limit: i64,
}
pub(crate) fn catalogue(
    db: &(impl Adapter + ?Sized),
    q: &ResourceQuery<'_>,
) -> Result<(i64, Vec<Value>)> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let mut conditions = vec!["r.kind='report-images'".to_owned()];
    if !q.administrator {
        let actor = sql::bind(&mut values, q.actor, pg);
        let mut visible = vec![format!(
            "EXISTS(SELECT 1 FROM report_resource_uploaders u WHERE u.resource_record_id=r.id AND u.user_id=CAST({actor} AS BIGINT))"
        )];
        let resources = sql::field("resourceId", pg);
        for (kind, scope) in [("invoices", &q.invoices), ("exporters", &q.exporters)] {
            let allowed = scope.filter_for(&mut values, pg, "owner");
            visible.push(format!("EXISTS(SELECT 1 FROM report_resource_links l JOIN records owner ON owner.id=l.owner_record_id WHERE l.resource_id={resources} AND l.source_kind='{kind}' AND ({allowed}))"));
        }
        if !q.report_types.is_empty() {
            let types = q
                .report_types
                .iter()
                .map(|v| sql::bind(&mut values, v, pg))
                .collect::<Vec<_>>()
                .join(",");
            for (audience, output) in [(&q.templates, false), (&q.output_templates, true)] {
                let mut clauses = vec![];
                crate::template_query::audience_filter(audience, pg, &mut values, &mut clauses);
                let mut allowed = if clauses.is_empty() {
                    "TRUE".into()
                } else {
                    clauses.join(" AND ")
                }
                .replace("r.", "owner.");
                if output {
                    let actor = sql::bind(&mut values, q.actor, pg);
                    let status = sql::field("status", pg).replace("r.", "owner.");
                    let share = sql::field("shareScope", pg).replace("r.", "owner.");
                    allowed += &format!(
                        " AND l.source_kind='report-templates' AND ((owner.owner_id=CAST({actor} AS BIGINT) AND {status} IN ('Draft','Published')) OR ({status}='Published' AND {share}<>'Private'))"
                    );
                }
                let report_type = sql::field("reportType", pg).replace("r.", "owner.");
                visible.push(format!("EXISTS(SELECT 1 FROM report_resource_links l JOIN records owner ON owner.id=l.owner_record_id WHERE l.resource_id={resources} AND owner.kind='report-templates' AND {report_type} IN ({types}) AND ({allowed}))"));
            }
        }
        if !q.managed_resources.is_empty() {
            let resources_list =
                sql::bind(&mut values, serde_json::to_string(q.managed_resources)?, pg);
            let selected = if pg {
                format!("SELECT jsonb_array_elements_text(CAST({resources_list} AS jsonb))")
            } else {
                format!("SELECT value FROM json_each({resources_list})")
            };
            visible.push(format!("{resources} IN ({selected})"));
        }
        conditions.push(format!("({})", visible.join(" OR ")));
    }
    if !q.keyword.is_empty() {
        let keyword = sql::bind(&mut values, q.keyword, pg);
        conditions.push(format!(
            "({})",
            ["resourceId", "mediaType", "sha256"]
                .map(|f| sql::contains(&sql::field(f, pg), &keyword, pg))
                .join(" OR ")
        ));
    }
    if let Some(id) = q.before_id {
        conditions.push(format!(
            "r.id<CAST({} AS BIGINT)",
            sql::bind(&mut values, id, pg)
        ));
    }
    let sql = QuerySql {
        source: crate::query_source::Source::Records,
        filter: conditions.join(" AND "),
        order: "r.id DESC".into(),
        values,
    };
    let fields = [
        "id",
        "resourceId",
        "mediaType",
        "byteLength",
        "sha256",
        "uploaders",
    ]
    .map(|f| (f, f));
    db.query_page(&sql, &sql::projection(&fields, pg)?, q.offset, q.limit)
}
