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
            source: crate::query_source::Source::Projected,
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
    // Directory and authorization records retain creator scope metadata. Their
    // references come from hierarchy/ownership, not that incidental scope.
    let scoped = "r.kind NOT IN ('companies','departments','permission-templates','unit-access')";
    let fields = match reference {
        OrganizationReference::Company => format!(
            "(({scoped} AND {}={code}) OR (r.kind IN ('departments','unit-access') AND {}={code}) OR (r.kind='companies' AND {}={code}) OR (r.kind='permission-templates' AND EXISTS(SELECT 1 FROM permission_schemes s JOIN organization_units u ON u.id=s.owner_unit_id WHERE s.id=r.id AND u.code={code})) OR (r.kind='people' AND EXISTS(SELECT 1 FROM personnel_memberships m WHERE m.person_id=r.id AND m.company={code})))",
            sql::field("companyScope", pg),
            sql::field("companyCode", pg),
            sql::field("parentCompanyCode", pg)
        ),
        OrganizationReference::Department => {
            let company = sql::bind(&mut values, company, pg);
            format!(
                "(({scoped} AND r.company={company} AND {}={code}) OR (r.kind='departments' AND {}={company} AND {}={code}) OR (r.kind='people' AND EXISTS(SELECT 1 FROM personnel_memberships m WHERE m.person_id=r.id AND m.company={company} AND m.department={code})))",
                sql::field("departmentId", pg),
                sql::field("companyCode", pg),
                sql::field("parentCode", pg)
            )
        }
    };
    QuerySql {
        source: crate::query_source::Source::Projected,
        filter: format!("r.kind IN ({kinds}) AND r.id<>CAST({excluded} AS BIGINT) AND {fields}"),
        order: "r.id ASC".into(),
        values,
    }
}
