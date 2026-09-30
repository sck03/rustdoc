use crate::{
    Error, QuerySql, Result,
    sql::{self, bind, field},
};
pub struct JobRetention<'a> {
    pub cutoff: &'a str,
    pub per_user: usize,
    pub global: usize,
}
pub struct JobQuery<'a> {
    pub owner: Option<i64>,
    pub active: Option<bool>,
    pub status: &'a str,
    pub keyword: &'a str,
    pub retention: Option<JobRetention<'a>>,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}
fn active(pg: bool) -> String {
    format!("{} IN ('Pending','Queued','Running')", field("status", pg))
}
fn time(pg: bool) -> String {
    let value = format!(
        "COALESCE({}, {})",
        field("completedAt", pg),
        field("createdAt", pg)
    );
    if pg {
        format!(
            "CASE WHEN {value} ~ '^[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}[Tt][0-9]{{2}}:[0-9]{{2}}:[0-9]{{2}}(\\.[0-9]+)?([Zz]|[+-][0-9]{{2}}:[0-9]{{2}})$' THEN (EXTRACT(EPOCH FROM ({value})::timestamptz)*1000)::bigint ELSE ('invalid job timestamp: '||COALESCE({value},''))::bigint END"
        )
    } else {
        format!("job_time({value})")
    }
}
pub(crate) fn invalid_times(pg: bool) -> QuerySql {
    QuerySql {
        filter: format!(
            "r.kind='background-jobs' AND NOT ({}) AND ({} IS NULL OR {} IS NULL)",
            active(pg),
            time(pg),
            field("requestedByUserId", pg)
        ),
        order: "r.id",
        values: vec![],
    }
}
pub(crate) fn build(q: &JobQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = Vec::new();
    let mut clauses = vec!["r.kind='background-jobs'".to_owned()];
    if let Some(owner) = q.owner {
        clauses.push(format!(
            "r.owner_id=CAST({} AS BIGINT)",
            bind(&mut values, owner, pg)
        ));
    }
    if let Some(value) = q.active {
        clauses.push(if value {
            active(pg)
        } else {
            format!("NOT ({})", active(pg))
        });
    }
    if !q.status.is_empty() {
        let status = bind(&mut values, q.status, pg);
        clauses.push(if q.status == "Canceling" {
            let cancel = if pg {
                format!("{}='true'", field("cancelRequested", true))
            } else {
                format!("{}=1", field("cancelRequested", false))
            };
            format!(
                "({}={status} OR ({} AND {cancel}))",
                field("status", pg),
                active(pg)
            )
        } else {
            format!("{}={status}", field("status", pg))
        });
    }
    if !q.keyword.is_empty() {
        let keyword = bind(&mut values, q.keyword, pg);
        clauses.push(format!(
            "({})",
            [
                "title",
                "kind",
                "statusText",
                "detailText",
                "requestedBy",
                "errorMessage"
            ]
            .iter()
            .map(|key| sql::contains(&field(key, pg), &keyword, pg))
            .collect::<Vec<_>>()
            .join(" OR ")
        ));
    }
    if let Some(policy) = &q.retention {
        if !(1..=100_000).contains(&policy.global) || !(1..=10_000).contains(&policy.per_user) {
            return Err(Error::unavailable("任务保留查询边界无效。"));
        }
        let cutoff = bind(&mut values, policy.cutoff, pg);
        let cutoff = if pg {
            format!("(EXTRACT(EPOCH FROM ({cutoff})::timestamptz)*1000)::bigint")
        } else {
            format!("job_time({cutoff})")
        };
        let timestamp = time(pg);
        let owner = field("requestedByUserId", pg);
        let job_id = field("jobId", pg);
        clauses.push(format!("r.id NOT IN (SELECT id FROM (SELECT r.id, {timestamp} AS completed, {job_id} AS job_id,
            ROW_NUMBER() OVER(PARTITION BY {owner} ORDER BY {timestamp} DESC, {job_id} DESC) AS user_rank
            FROM records r WHERE r.kind='background-jobs' AND NOT ({active})) ranked
            WHERE completed>={cutoff} AND user_rank<={per_user} ORDER BY completed DESC, job_id DESC LIMIT {global})",
            active=active(pg),per_user=policy.per_user,global=policy.global));
    }
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order: if pg {
                "r.body->>'createdAt' DESC, r.id DESC"
            } else {
                "json_extract(r.body,'$.createdAt') DESC, r.id DESC"
            },
            values,
        },
        sql::projection(q.fields, pg)?,
    ))
}
