//! Resource requests share authorization, filtering and one-snapshot pagination.
use crate::{DataScope, QuerySql, Result, sql};

pub struct OfficeQuery<'a> {
    pub viewer: i64,
    pub context: &'a str,
    pub assigned_only: bool,
    pub principals: &'a [crate::RecordApprover],
    pub kind: &'a str,
    pub company: &'a str,
    pub view: DataScope<'a>,
    pub issue: DataScope<'a>,
    pub return_scope: DataScope<'a>,
    pub handling_keys: &'a [String],
    pub keyword_handling_keys: &'a [String],
    pub keyword: &'a str,
    pub status: &'a str,
    pub identifiers: &'a [(&'a str, i64)],
    pub owner: Option<i64>,
    pub include_inactive: bool,
    pub low_stock: bool,
    pub handling_only: bool,
    pub can_issue: bool,
    pub can_return: bool,
    pub from: Option<&'a str>,
    pub to: Option<&'a str>,
    pub offset: i64,
    pub limit: i64,
}

fn keys(path: &str, keys: &[String], values: &mut Vec<String>, pg: bool) -> String {
    if keys.is_empty() {
        return "FALSE".into();
    }
    let parameters = keys
        .iter()
        .map(|key| sql::bind(values, key, pg))
        .collect::<Vec<_>>()
        .join(",");
    format!("{} IN ({parameters})", sql::field(path, pg))
}

pub(crate) fn build(q: &OfficeQuery<'_>, pg: bool) -> Result<QuerySql> {
    if !["rooms", "supplies", "bookings", "supply-requests"].contains(&q.kind) {
        return Err(crate::Error::unavailable("行政查询类型无效。"));
    }
    let directory = matches!(q.kind, "rooms" | "supplies");
    let mut values = vec![];
    let mut clauses = vec![format!(
        "r.kind={} AND r.company={}",
        sql::bind(&mut values, q.kind, pg),
        sql::bind(&mut values, q.company, pg)
    )];
    let state = sql::field("status", pg);
    let handling = sql::field("handlingKey", pg);
    let assigned = if !directory || q.handling_only {
        keys("handlingKey", q.handling_keys, &mut values, pg)
    } else {
        "FALSE".into()
    };
    let issue = if !directory || q.handling_only {
        q.issue.filter(&mut values, pg)
    } else {
        "FALSE".into()
    };
    let can_issue = format!("((COALESCE({handling},'')='' AND {issue}) OR {assigned})");
    if !directory {
        let view = q.view.filter(&mut values, pg);
        let viewer = format!("CAST({} AS BIGINT)", sql::bind(&mut values, q.viewer, pg));
        let principals = q
            .principals
            .iter()
            .map(|p| {
                (
                    format!("CAST({} AS BIGINT)", sql::bind(&mut values, p.user_id, pg)),
                    sql::bind(&mut values, &p.context, pg),
                )
            })
            .collect::<Vec<_>>();
        let context = sql::bind(&mut values, q.context, pg);
        let participant = crate::record_query::participant_condition(
            pg,
            "r",
            &viewer,
            Some(&context),
            &principals,
        );
        let view = if q.assigned_only {
            participant.clone()
        } else {
            format!("({view} OR {participant})")
        };
        let states = if q.kind == "bookings" {
            "'Approved','InUse','Completed'"
        } else {
            "'Approved','Issued','Returned'"
        };
        clauses.push(format!(
            "({view} OR ({state} IN ({states}) AND {can_issue}))"
        ));
    }
    if q.handling_only {
        let return_scope = q.return_scope.filter(&mut values, pg);
        let can_return = format!("((COALESCE({handling},'')='' AND {return_scope}) OR {assigned})");
        clauses.push(format!("(({state}='Approved' AND {} AND {can_issue}) OR (({state}='InUse' OR ({state}='Issued' AND {})) AND {} AND {can_return}))", q.can_issue, truth("isReturnable", pg), q.can_return));
    }
    if directory && !q.include_inactive {
        clauses.push(truth("isActive", pg));
    }
    if directory && q.low_stock {
        clauses.push(truth("lowStock", pg));
    }
    if !q.status.is_empty() {
        clauses.push(format!("{state}={}", sql::bind(&mut values, q.status, pg)));
    }
    for (path, id) in q.identifiers {
        sql::valid_path(path)?;
        clauses.push(format!(
            "CAST({} AS BIGINT)=CAST({} AS BIGINT)",
            sql::field(path, pg),
            sql::bind(&mut values, id, pg)
        ));
    }
    if let Some(owner) = q.owner {
        clauses.push(format!(
            "r.owner_id=CAST({} AS BIGINT)",
            sql::bind(&mut values, owner, pg)
        ));
    }
    if !q.keyword.is_empty() {
        let parameter = sql::bind(&mut values, q.keyword, pg);
        let fields = if directory {
            &["name", "location", "handlingName"][..]
        } else {
            &[
                "title",
                "purpose",
                "applicantName",
                "employeeNumber",
                "roomName",
                "supplyName",
            ][..]
        };
        let mut choices = fields
            .iter()
            .map(|path| {
                sql::contains(
                    &format!("COALESCE({},'')", sql::field(path, pg)),
                    &parameter,
                    pg,
                )
            })
            .collect::<Vec<_>>();
        choices.push(keys(
            "handlingKey",
            q.keyword_handling_keys,
            &mut values,
            pg,
        ));
        clauses.push(format!("({})", choices.join(" OR ")));
    }
    for (value, path, operator) in [
        (
            q.from,
            if q.kind == "bookings" {
                "endsAt"
            } else {
                "createdAt"
            },
            ">",
        ),
        (
            q.to,
            if q.kind == "bookings" {
                "startsAt"
            } else {
                "createdAt"
            },
            "<",
        ),
    ] {
        if let Some(value) = value {
            let field = sql::field(path, pg);
            let parameter = sql::bind(&mut values, value, pg);
            clauses.push(if pg {
                format!("CAST({field} AS TIMESTAMPTZ){operator}CAST({parameter} AS TIMESTAMPTZ)")
            } else {
                format!("job_time({field}){operator}job_time({parameter})")
            });
        }
    }
    let order = if directory {
        if pg {
            "(r.body->>'isActive')::boolean DESC, r.body->>'name' COLLATE \"C\", r.id"
        } else {
            "json_extract(r.body,'$.isActive') DESC, json_extract(r.body,'$.name') COLLATE BINARY, r.id"
        }
    } else if pg {
        "r.body->>'createdAt' DESC, r.id DESC"
    } else {
        "json_extract(r.body,'$.createdAt') DESC, r.id DESC"
    };
    Ok(QuerySql {
        filter: clauses.join(" AND "),
        order: order.into(),
        values,
    })
}

fn truth(path: &str, pg: bool) -> String {
    let field = sql::field(path, pg);
    if pg {
        format!("COALESCE(({field})::boolean,false)")
    } else {
        format!("COALESCE({field},0)=1")
    }
}
