//! Group ownership without materializing business documents.
use crate::{Adapter, Result, sql};

pub(crate) fn counts(
    db: &(impl Adapter + ?Sized),
    kinds: &[&str],
) -> Result<Vec<(Option<i64>, i64)>> {
    if kinds.is_empty() {
        return Ok(Vec::new());
    }
    let pg = db.provider() == "PostgreSQL";
    let mut values = Vec::new();
    let kinds = kinds
        .iter()
        .map(|kind| sql::bind(&mut values, kind, pg))
        .collect::<Vec<_>>()
        .join(",");
    let aggregate = if pg {
        "COALESCE(jsonb_agg(jsonb_build_array(owner_id,total)),'[]'::jsonb)::text"
    } else {
        "json_group_array(json_array(owner_id,total))"
    };
    let result = db.query_json(&format!("SELECT {aggregate} FROM (SELECT owner_id,COUNT(*) AS total FROM records WHERE kind IN ({kinds}) GROUP BY owner_id) owners"), &values)?;
    Ok(serde_json::from_value(result)?)
}
