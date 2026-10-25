//! Temporary import data is accounted from BLOB metadata without loading payloads.
use crate::{Error, QuerySql, Result, sql};

#[derive(Debug, Default, PartialEq, Eq)]
pub struct ImportPreviewUsage {
    pub count: u64,
    pub user_count: u64,
    pub bytes: u64,
    pub user_bytes: u64,
}
impl ImportPreviewUsage {
    pub(crate) fn from_counts(values: [i64; 4]) -> Result<Self> {
        let [count, user_count, bytes, user_bytes] = values.map(u64::try_from);
        let invalid = |_| Error::unavailable("导入预检容量元数据无效。");
        Ok(Self {
            count: count.map_err(invalid)?,
            user_count: user_count.map_err(invalid)?,
            bytes: bytes.map_err(invalid)?,
            user_bytes: user_bytes.map_err(invalid)?,
        })
    }
}

pub(crate) fn usage_sql(pg: bool) -> String {
    let owner = if pg { "$1" } else { "?1" };
    let length = if pg {
        "octet_length(f.content)"
    } else {
        "length(f.content)"
    };
    let active = if pg {
        "CASE WHEN r.body->'consumed'='false'::jsonb THEN 1 WHEN r.body->'consumed'='true'::jsonb THEN 0 ELSE ('invalid preview state: '||r.id::text)::bigint END"
    } else {
        "CASE json_type(r.body,'$.consumed') WHEN 'false' THEN 1 WHEN 'true' THEN 0 ELSE job_time('invalid preview state') END"
    };
    format!("SELECT CAST(COALESCE(SUM(active),0) AS BIGINT), CAST(COALESCE(SUM(CASE WHEN owner_id={owner} THEN active ELSE 0 END),0) AS BIGINT),
        CAST(COALESCE(SUM(bytes),0) AS BIGINT), CAST(COALESCE(SUM(CASE WHEN owner_id={owner} THEN bytes ELSE 0 END),0) AS BIGINT)
        FROM (SELECT r.owner_id, {active} AS active, COALESCE((SELECT {length} FROM files f
            WHERE f.record_id=r.id AND f.kind='import-previews'),0) AS bytes
            FROM records r WHERE r.kind='import-previews') previews")
}

pub(crate) fn expired(before: &str, pg: bool) -> QuerySql {
    let mut values = vec![];
    let before = sql::bind(&mut values, before, pg);
    let (expires_seconds, expires_nanos) = sql::instant_order(&sql::field("expiresAt", pg), pg);
    let (before_seconds, before_nanos) = sql::instant_order(&before, pg);
    QuerySql {
        filter: format!(
            "r.kind='import-previews' AND ({expires_seconds},{expires_nanos}) <= ({before_seconds},{before_nanos})"
        ),
        order: "r.id".into(),
        values,
    }
}
