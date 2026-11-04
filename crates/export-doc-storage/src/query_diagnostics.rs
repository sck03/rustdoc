//! Read-only plans use the production builders, including scope, count and projection.
use crate::{
    GenericQuery, HsQuery, JobQuery, RecordQuery, TemplateVersionQuery, WorkflowSummaryQuery,
};

pub enum QueryPlan<'a> {
    Identity { kind: &'a str, identity: &'a str },
    Generic(&'a GenericQuery<'a>),
    Jobs(&'a JobQuery<'a>),
    Hs(&'a HsQuery<'a>),
    TemplateVersions(&'a TemplateVersionQuery<'a>),
    Records(&'a RecordQuery<'a>),
    WorkflowSummary(&'a WorkflowSummaryQuery<'a>),
}
impl QueryPlan<'_> {
    pub(crate) fn statement(&self, pg: bool) -> crate::Result<(String, Vec<String>)> {
        let page = |sql: crate::QuerySql, projection: String, offset, limit| {
            (
                crate::query_source::page(&sql, &projection, offset, limit, pg),
                sql.values,
            )
        };
        Ok(match self {
            Self::Identity { kind, identity } => (
                crate::query_source::identity(pg),
                vec![kind.to_string(), identity.to_string()],
            ),
            Self::Generic(q) => crate::query_source::generic(q, pg)?,
            Self::Jobs(q) => {
                let (sql, projection) = crate::job_query::build(q, pg)?;
                page(sql, projection, q.offset, q.limit)
            }
            Self::Hs(q) => {
                let (sql, projection) = crate::hs_query::build(q, pg)?;
                (
                    crate::query_source::slice(&sql, &projection, q.offset, q.limit),
                    sql.values,
                )
            }
            Self::TemplateVersions(q) => page(
                crate::template_query::versions(q, pg),
                crate::template_query::metadata(pg, true),
                q.offset,
                q.limit,
            ),
            Self::Records(q) => crate::record_query::statement(q, pg)?,
            Self::WorkflowSummary(q) => crate::workflow::summary(q, pg),
        })
    }
}
pub(crate) fn measure(mut run: impl FnMut() -> crate::Result<()>) -> crate::Result<Vec<f64>> {
    run()?;
    (0..5)
        .map(|_| {
            let start = std::time::Instant::now();
            run()?;
            Ok(start.elapsed().as_secs_f64() * 1000.0)
        })
        .collect()
}
