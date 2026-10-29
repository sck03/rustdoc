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
    company: &str,
    exclude: i64,
    kinds: &[&str],
    pg: bool,
) -> QuerySql {
    if kinds.is_empty() {
        return QuerySql {
            filter: "1=0".into(),
            order: "r.id ASC".into(),
            values: vec![],
        };
    }
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
            "({}={code} OR {}={code} OR {}={code} OR (r.kind='people' AND EXISTS(SELECT 1 FROM personnel_memberships m WHERE m.person_id=r.id AND m.company={code})))",
            sql::field("companyScope", pg),
            sql::field("companyCode", pg),
            sql::field("parentCompanyCode", pg)
        ),
        OrganizationReference::Department => {
            let company = sql::bind(&mut values, company, pg);
            format!(
                "(((r.company={company} OR {}={company}) AND ({}={code} OR {}={code})) OR (r.kind='people' AND EXISTS(SELECT 1 FROM personnel_memberships m WHERE m.person_id=r.id AND m.company={company} AND m.department={code})))",
                sql::field("companyCode", pg),
                sql::field("departmentId", pg),
                sql::field("parentCode", pg)
            )
        }
    };
    QuerySql {
        filter: format!("r.kind IN ({kinds}) AND r.id<>CAST({excluded} AS BIGINT) AND {fields}"),
        order: "r.id ASC".into(),
        values,
    }
}
