use crate::{Adapter, Result, sql};
use serde_json::{Value, json};
pub(crate) fn administrators(db: &(impl Adapter + ?Sized)) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let role = sql::field("role", pg);
    let constructor = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let rows = crate::file_catalog::rows(
        db,
        &format!(
            "SELECT {constructor}('id',r.id) AS body FROM records r WHERE r.kind='users' AND {role}='Admin' ORDER BY r.id LIMIT 1001"
        ),
        &[],
    )?;
    if rows.len() > 1000 {
        return Err(crate::Error::unavailable(
            "容量通知管理员数量超过一千账号预算。",
        ));
    }
    Ok(json!(rows))
}
pub(crate) fn sample(
    db: &(impl Adapter + ?Sized),
    day: &str,
    location: &str,
    physical: u64,
    logical: u64,
    reclaimable: u64,
    oldest: &str,
) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let day = sql::bind(&mut args, day, pg);
    let location = sql::bind(&mut args, location, pg);
    let physical = sql::bind(&mut args, physical, pg);
    let logical = sql::bind(&mut args, logical, pg);
    let reclaimable = sql::bind(&mut args, reclaimable, pg);
    db.execute_statement(&format!("INSERT INTO capacity_daily(day,location,physical_bytes,logical_bytes,reclaimable_bytes) VALUES({day},{location},CAST({physical} AS BIGINT),CAST({logical} AS BIGINT),CAST({reclaimable} AS BIGINT)) ON CONFLICT(day,location) DO UPDATE SET physical_bytes=excluded.physical_bytes,logical_bytes=excluded.logical_bytes,reclaimable_bytes=excluded.reclaimable_bytes"),&args)?;
    let mut args = vec![];
    let oldest = sql::bind(&mut args, oldest, pg);
    db.execute_statement(
        &format!("DELETE FROM capacity_daily WHERE day<{oldest}"),
        &args,
    )?;
    Ok(())
}
pub(crate) fn history(db: &(impl Adapter + ?Sized), since: &str) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let since = sql::bind(&mut args, since, pg);
    let json = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    Ok(json!(crate::file_catalog::rows(
        db,
        &format!(
            "SELECT {json}('day',day,'location',location,'physicalBytes',physical_bytes,'logicalBytes',logical_bytes,'reclaimableBytes',reclaimable_bytes) AS body FROM capacity_daily WHERE day>={since} ORDER BY day DESC,location LIMIT 1100"
        ),
        &args
    )?))
}
