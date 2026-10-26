use crate::{GenericQuery, QuerySql, Result, generic_query, sql};
pub enum RecordOrder {
    CreatedDescending,
    UpdatedDescending,
    IdAscending,
}
pub(crate) fn build(
    query: &GenericQuery<'_>,
    order: RecordOrder,
    pg: bool,
) -> Result<(QuerySql, String)> {
    let (mut sql, projection) = generic_query::build(query, pg)?;
    sql.order = match order {
        RecordOrder::IdAscending => "r.id".into(),
        RecordOrder::CreatedDescending | RecordOrder::UpdatedDescending => {
            let path = if matches!(order, RecordOrder::CreatedDescending) {
                "createdAt"
            } else {
                "updatedAt"
            };
            let (seconds, nanos) = sql::instant_order(&sql::field(path, pg), pg);
            format!("{seconds} DESC,{nanos} DESC,r.id DESC")
        }
    };
    Ok((sql, projection))
}
