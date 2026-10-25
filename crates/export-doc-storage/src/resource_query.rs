//! Extract only reference-bearing fields; template text never crosses this boundary.
use crate::{Error, QuerySql, Result, sql};

pub(crate) fn build(kind: &str, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = vec![];
    let mut filter = format!("r.kind={}", sql::bind(&mut values, kind, pg));
    let fields = match kind {
        "invoices" => &[
            "id",
            "companyScope",
            "departmentId",
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
    Ok((
        QuerySql {
            filter,
            order: "r.id DESC".into(),
            values,
        },
        projection,
    ))
}
