use crate::{Error, QuerySql, Result, sql};
pub(crate) fn build(kind: &str, parent: i64, states: &[&str], pg: bool) -> Result<QuerySql> {
    let field = match kind {
        "bookings" => "meetingRoomId",
        "supply-requests" => "officeSupplyId",
        _ => return Err(Error::unavailable("未知资源申请类型。")),
    };
    let mut values = vec![];
    let kind = sql::bind(&mut values, kind, pg);
    let id = sql::bind(&mut values, parent, pg);
    let state = sql::field("status", pg);
    let states = states
        .iter()
        .map(|state| sql::bind(&mut values, state, pg))
        .collect::<Vec<_>>()
        .join(",");
    let returnable = sql::field("isReturnable", pg);
    let issued = if pg {
        format!("COALESCE(({returnable})::boolean,false)")
    } else {
        format!("COALESCE({returnable},0)=1")
    };
    Ok(QuerySql {
        source: crate::query_source::Source::Projected,
        filter: format!(
            "r.kind={kind} AND CAST({} AS BIGINT)=CAST({id} AS BIGINT) AND ({state} IN ({states}) OR ({state}='Issued' AND {issued}))",
            sql::field(field, pg)
        ),
        order: "r.id DESC".into(),
        values,
    })
}
