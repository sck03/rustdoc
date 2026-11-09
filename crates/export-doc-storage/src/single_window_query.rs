//! Submission ordering is queried without materializing batch histories.
use crate::{Adapter, Error, Result, sql};

pub(crate) fn latest_version(
    db: &(impl Adapter + ?Sized),
    source: i64,
    business: &str,
) -> Result<i64> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let source = sql::bind(&mut values, source, pg);
    let business = sql::bind(&mut values, business, pg);
    let local = sql::field("_sourceLocal", pg);
    let local = if pg {
        format!("CAST({local} AS BOOLEAN)=TRUE")
    } else {
        format!("{local}=1")
    };
    let statement = format!(
        "SELECT {}('version',COALESCE(MAX(CAST({} AS BIGINT)),0)){} FROM record_rows r WHERE r.kind='sw-batches' AND {local} AND CAST({} AS BIGINT)=CAST({source} AS BIGINT) AND {}={business}",
        sql::object(pg),
        sql::field("submissionVersion", pg),
        if pg { "::text" } else { "" },
        sql::field("sourceId", pg),
        sql::field("businessType", pg),
    );
    db.query_json(&statement, &values)?["version"]
        .as_i64()
        .filter(|version| *version >= 0)
        .ok_or_else(|| Error::unavailable("申报版本损坏。"))
}
