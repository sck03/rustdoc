//! Parent-authorized contacts, supplier products and assessments share bounded SQL reads.
use crate::{Error, QuerySql, Result, sql};

pub struct RelatedQuery<'a> {
    pub kind: &'a str,
    pub parent_id: i64,
    pub include_other_drafts: bool,
    pub user_id: i64,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}

fn selection(q: &RelatedQuery<'_>, pg: bool) -> Result<QuerySql> {
    let relation = match q.kind {
        "crm-contacts" => "crmCustomerId",
        "supplier-contacts" | "supplier-products" | "supplier-assessments" => "supplierCompanyId",
        _ => return Err(Error::unavailable("关联目录类型无效。")),
    };
    let mut values = vec![];
    let mut filter = format!(
        "r.kind={} AND CAST({} AS BIGINT)=CAST({} AS BIGINT)",
        sql::bind(&mut values, q.kind, pg),
        sql::field(relation, pg),
        sql::bind(&mut values, q.parent_id, pg)
    );
    if q.kind == "supplier-assessments" && !q.include_other_drafts {
        filter.push_str(&format!(
            " AND ({}='Confirmed' OR r.owner_id=CAST({} AS BIGINT))",
            sql::field("status", pg),
            sql::bind(&mut values, q.user_id, pg)
        ));
    }
    let primary = if pg {
        format!("{}='true'", sql::field("isPrimary", pg))
    } else {
        format!("{}=1", sql::field("isPrimary", pg))
    };
    let order = format!(
        "CASE WHEN {primary} THEN 1 ELSE 0 END DESC, {} DESC NULLS LAST, r.id DESC",
        sql::field("assessmentDate", pg)
    );
    Ok(QuerySql {
        source: crate::query_source::Source::Records,
        filter,
        order,
        values,
    })
}
pub(crate) fn build(q: &RelatedQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let selection = selection(q, pg)?;
    let projection = sql::projected(q.fields.iter().map(|(name, path)| {
        sql::valid_path(path)?;
        let product_path = match *path {
            "productCode" => Some("productCode"),
            "productNameCN" => Some("nameCN"),
            "productNameEN" => Some("nameEN"),
            "__linkedProductId" => Some("id"),
            _ => None,
        };
        let value = if q.kind == "supplier-products" {
            if let Some(path) = product_path {
                let field = sql::json_field_from("product.body", path, pg);
                let value = format!("(SELECT {field} FROM record_rows product WHERE product.kind='products' AND product.id=CAST({} AS BIGINT))", sql::field("productId", pg));
                if *name == "__linkedProductId" { value } else { format!("COALESCE({value},{})", if pg { "'\"\"'::jsonb" } else { "'\"\"'" }) }
            } else { sql::json_field(path, pg) }
        } else { sql::json_field(path, pg) };
        Ok((*name, value))
    }), pg)?;
    Ok((selection, projection))
}

pub(crate) fn analytics(
    connection: &(impl crate::adapter::Adapter + ?Sized),
    q: &RelatedQuery<'_>,
) -> Result<serde_json::Value> {
    if q.kind != "supplier-assessments" {
        return Err(Error::unavailable("评价分析目录无效。"));
    }
    let pg = connection.provider() == "PostgreSQL";
    let (selection, projection) = build(q, pg)?;
    let mut totals = vec![("totalCount", "COUNT(*)".to_owned())];
    for (target, field) in [
        ("qualityTotal", "qualityScore"),
        ("deliveryTotal", "deliveryScore"),
        ("serviceTotal", "serviceScore"),
        ("priceTotal", "priceScore"),
    ] {
        totals.push((
            target,
            format!("COALESCE(SUM(CAST({} AS BIGINT)),0)", sql::field(field, pg)),
        ));
    }
    for (target, conclusion) in [
        ("preferredCount", "优先合作"),
        ("qualifiedCount", "合格"),
        ("watchCount", "观察"),
        ("pausedCount", "暂停合作"),
    ] {
        totals.push((
            target,
            format!(
                "COALESCE(SUM(CASE WHEN {}='{conclusion}' THEN 1 ELSE 0 END),0)",
                sql::field("conclusion", pg)
            ),
        ));
    }
    // Aggregates must stay in this SELECT scope, outside row-projection subqueries.
    let projection_totals = format!(
        "{}({}){}",
        sql::object(pg),
        totals
            .iter()
            .map(|(key, value)| format!("'{key}',{value}"))
            .collect::<Vec<_>>()
            .join(","),
        if pg { "::text" } else { "" }
    );
    let mut result = connection.query_json(
        &crate::query_source::aggregate(&selection, &projection_totals),
        &selection.values,
    )?;
    result["recent"] = serde_json::json!(connection.query_slice(&selection, &projection, 0, 12)?);
    Ok(result)
}
