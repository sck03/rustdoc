//! Extract only reference-bearing fields; template text never crosses this boundary.
use crate::{Error, QuerySql, Result, sql};

pub(crate) fn build(kind: &str, ids: Option<&[&str]>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = vec![];
    let mut filter = format!("r.kind={}", sql::bind(&mut values, kind, pg));
    let fields = match kind {
        "invoices" => &[
            "id",
            "companyScope",
            "departmentId",
            "projectId",
            "ownerUserId",
            "shippingMarksType",
            "shippingMarksImage",
            "docSealPath",
            "customsSealPath",
        ][..],
        "exporters" => &[
            "id",
            "companyScope",
            "departmentId",
            "projectId",
            "ownerUserId",
            "docSealPath",
            "customsSealPath",
        ][..],
        "report-templates" => &[
            "id",
            "reportType",
            "status",
            "shareScope",
            "companyScope",
            "departmentId",
            "ownerUserId",
        ][..],
        "template-versions" => &["id", "templateId"][..],
        _ => return Err(Error::unavailable("未知图片引用查询类型。")),
    };
    let fields: Vec<_> = fields.iter().map(|field| (*field, *field)).collect();
    let projection = sql::projection(&fields, pg)?;
    let projection = if matches!(kind, "report-templates" | "template-versions") {
        if kind == "template-versions" {
            filter += &format!(" AND {}='report-templates'", sql::field("templateKind", pg));
        }
        let content = sql::field(
            if kind == "template-versions" {
                "content.contentHtml"
            } else {
                "contentHtml"
            },
            pg,
        );
        if pg {
            format!(
                "(CAST(({projection}) AS JSONB) || jsonb_build_object('resources',CAST({content} AS JSONB)->'resources','validDocument',jsonb_typeof(CAST({content} AS JSONB))='object' AND CAST({content} AS JSONB)->'version'='3'::jsonb))::text"
            )
        } else {
            format!(
                "json_set(({projection}),'$.resources',json_extract({content},'$.resources'),'$.validDocument',json(CASE WHEN json_type({content})='object' AND json_extract({content},'$.version')=3 THEN 'true' ELSE 'false' END))"
            )
        }
    } else {
        projection
    };
    if let Some(ids) = ids {
        let parameter = sql::bind(&mut values, serde_json::to_string(ids)?, pg);
        let targets = if pg {
            format!("SELECT jsonb_array_elements_text(CAST({parameter} AS jsonb))")
        } else {
            format!("SELECT value FROM json_each({parameter})")
        };
        filter += &format!(
            " AND EXISTS(SELECT 1 FROM report_resource_links l WHERE l.source_id=r.id AND l.resource_id IN ({targets}))"
        );
    }
    Ok((
        QuerySql {
            source: crate::query_source::Source::Projected,
            filter,
            order: "r.id DESC".into(),
            values,
        },
        projection,
    ))
}
