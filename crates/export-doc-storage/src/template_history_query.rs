//! Payload sizes and cleanup candidates are projected without loading snapshot content.
use crate::{QuerySql, TemplateVersionQuery, sql, template_query};
#[derive(Debug)]
pub struct TemplateHistoryUsage {
    pub count: u64,
    pub bytes: u64,
    pub eligible_count: u64,
    pub eligible_bytes: u64,
}
impl TemplateHistoryUsage {
    pub(crate) fn from_value(value: &serde_json::Value) -> crate::Result<Self> {
        let read = |key: &str| {
            value[key]
                .as_u64()
                .ok_or_else(|| crate::Error::unavailable("模板历史容量统计无效。"))
        };
        Ok(Self {
            count: read("count")?,
            bytes: read("bytes")?,
            eligible_count: read("eligibleCount")?,
            eligible_bytes: read("eligibleBytes")?,
        })
    }
}

fn bytes(pg: bool) -> &'static str {
    if pg {
        "octet_length(r.body::text)::bigint"
    } else {
        "length(CAST(r.body AS BLOB))"
    }
}

pub(crate) fn usage(query: &TemplateVersionQuery<'_>, keep: i64, pg: bool) -> (QuerySql, String) {
    let mut sql = template_query::versions(query, pg);
    let eligible = format!(
        "r.id NOT IN (SELECT r.id FROM record_rows r WHERE {} ORDER BY {} LIMIT {})",
        sql.filter,
        sql.order,
        keep.max(1)
    );
    let bytes = bytes(pg);
    let pairs = format!(
        "'count',COUNT(*),'bytes',COALESCE(SUM({bytes}),0),'eligibleCount',COALESCE(SUM(CASE WHEN {eligible} THEN 1 ELSE 0 END),0),'eligibleBytes',COALESCE(SUM(CASE WHEN {eligible} THEN {bytes} ELSE 0 END),0)"
    );
    let projection = if pg {
        format!("jsonb_build_object({pairs})::text")
    } else {
        format!("json_object({pairs})")
    };
    sql.order = "1".into();
    (sql, projection)
}

pub(crate) fn candidates(query: &TemplateVersionQuery<'_>, pg: bool) -> (QuerySql, String) {
    let version = sql::field("content.versionNumber", pg);
    let pairs = format!(
        "'id',r.id,'recordVersion',r.version,'versionNumber',CAST({version} AS BIGINT),'byteLength',{}",
        bytes(pg)
    );
    (
        template_query::versions(query, pg),
        if pg {
            format!("jsonb_build_object({pairs})::text")
        } else {
            format!("json_object({pairs})")
        },
    )
}
