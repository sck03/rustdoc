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

pub(crate) fn build(q: &RelatedQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
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
                let field = sql::json_field(path, pg).replace("r.body", "product.body");
                let value = format!("(SELECT {field} FROM record_rows product WHERE product.kind='products' AND product.id=CAST({} AS BIGINT))", sql::field("productId", pg));
                if *name == "__linkedProductId" { value } else { format!("COALESCE({value},{})", if pg { "'\"\"'::jsonb" } else { "'\"\"'" }) }
            } else { sql::json_field(path, pg) }
        } else { sql::json_field(path, pg) };
        Ok((*name, value))
    }), pg)?;
    Ok((
        QuerySql {
            filter,
            order,
            values,
        },
        projection,
    ))
}
