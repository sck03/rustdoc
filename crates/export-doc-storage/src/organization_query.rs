//! Organization deletion protects references across the reviewed resource set.
use crate::{QuerySql, sql};

#[derive(Clone, Copy)]
pub enum OrganizationReference {
    Company,
    Department,
}

pub(crate) fn build(
    reference: OrganizationReference,
    code: &str,
    exclude: i64,
    kinds: &[&str],
    pg: bool,
) -> QuerySql {
    let mut values = vec![];
    let kinds = kinds
        .iter()
        .map(|kind| sql::bind(&mut values, kind, pg))
        .collect::<Vec<_>>()
        .join(",");
    let code = sql::bind(&mut values, code, pg);
    let excluded = sql::bind(&mut values, exclude, pg);
    let fields = match reference {
        OrganizationReference::Company => format!(
            "({}={code} OR {}={code})",
            sql::field("companyScope", pg),
            sql::field("companyCode", pg)
        ),
        OrganizationReference::Department => format!(
            "({}={code} OR {}={code} OR (r.kind='people' AND {}))",
            sql::field("departmentId", pg),
            sql::field("parentCode", pg),
            crate::generic_query::department_filter(&code, true, pg)
        ),
    };
    QuerySql {
        filter: if kinds.is_empty() {
            "1=0".into()
        } else {
            format!("r.kind IN ({kinds}) AND r.id<>CAST({excluded} AS BIGINT) AND {fields}")
        },
        order: "r.id ASC".into(),
        values,
    }
}
