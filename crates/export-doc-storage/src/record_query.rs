//! Shared record filters; values are bound by both database adapters.
use super::{QuerySql, RecordQuery};
pub(super) fn sql(q: &RecordQuery<'_>, postgres: bool) -> QuerySql {
    // Preserve each adapter's indexed JSON expression.
    let property = |name: &str, number: bool| {
        if postgres {
            let expression = format!("r.body->>'{name}'");
            if number {
                format!("CAST({expression} AS BIGINT)")
            } else {
                expression
            }
        } else {
            format!("json_extract(r.body,'$.{name}')")
        }
    };
    let mut values = vec![];
    let mut bind = |value: String, ty: &str| {
        values.push(value);
        let parameter = format!(
            "CAST({}{} AS TEXT)",
            if postgres { "$" } else { "?" },
            values.len()
        );
        if ty == "TEXT" {
            parameter
        } else {
            format!("CAST({parameter} AS {ty})")
        }
    };
    let mut filter = format!(
        "r.kind={} AND r.company={}",
        bind(q.kind.into(), "TEXT"),
        bind(q.company.into(), "TEXT")
    );
    let mut scope = vec![];
    if let Some(department) = q.department {
        scope.push(format!("r.department={}", bind(department.into(), "TEXT")));
    }
    if let Some(departments) = q.departments {
        let parameters = departments
            .iter()
            .map(|department| bind(department.clone(), "TEXT"))
            .collect::<Vec<_>>();
        scope.push(if parameters.is_empty() {
            "FALSE".into()
        } else {
            format!("r.department IN ({})", parameters.join(","))
        });
    }
    if let Some(owner) = q.owner {
        scope.push(format!("r.owner_id={}", bind(owner.to_string(), "BIGINT")));
    }
    let ordinary = if scope.is_empty() {
        "TRUE".into()
    } else {
        scope.join(" AND ")
    };
    let assigned = if let Some(keys) = q.handling_keys.filter(|keys| !keys.is_empty()) {
        let keys = keys
            .iter()
            .map(|k| bind(k.clone(), "TEXT"))
            .collect::<Vec<_>>()
            .join(",");
        let states = q
            .handling_states
            .iter()
            .map(|s| bind((*s).into(), "TEXT"))
            .collect::<Vec<_>>()
            .join(",");
        format!(
            "{} IN ({keys}) AND {} IN ({states})",
            property("handlingKey", false),
            property("status", false)
        )
    } else {
        "FALSE".into()
    };
    filter += &format!(
        " AND ({})",
        if q.handling_only {
            format!(
                "({ordinary} AND COALESCE({},'')='') OR ({assigned})",
                property("handlingKey", false)
            )
        } else if q.handling_keys.is_some() {
            format!("({ordinary}) OR ({assigned})")
        } else {
            ordinary
        }
    );
    if let Some(status) = q.status {
        filter += &format!(
            " AND {}={}",
            property("status", false),
            bind(status.into(), "TEXT")
        );
    }
    if let Some((start, end)) = q.period {
        let nested = |kind: &str, field: &str| {
            if postgres {
                format!("r.body->'{kind}'->>'{field}'")
            } else {
                format!("json_extract(r.body,'$.{kind}.{field}')")
            }
        };
        let (first, last) = (bind(start.into(), "TEXT"), bind(end.into(), "TEXT"));
        match q.kind {
            "oa-leave" | "oa-travel" => {
                let kind = q.kind.trim_start_matches("oa-");
                filter += &format!(
                    " AND {}<={last} AND {}>={first}",
                    nested(kind, "startsOn"),
                    nested(kind, "endsOn")
                );
            }
            "oa-overtime" => {
                let instant = |value: String| {
                    if postgres {
                        format!("CAST({value} AS TIMESTAMPTZ)")
                    } else {
                        format!("julianday({value})")
                    }
                };
                filter += &format!(
                    " AND {}<{} AND {}>{}",
                    instant(nested("overtime", "startsAt")),
                    instant(last),
                    instant(nested("overtime", "endsAt")),
                    instant(first)
                );
            }
            _ => filter += " AND FALSE",
        }
    }
    if let Some(key) = q.handling_key {
        filter += &format!(
            " AND {}={}",
            property("handlingKey", false),
            bind(key.into(), "TEXT")
        );
    }
    for (field, value) in [
        (property("employeeId", true), q.employee),
        (property("requestId", true), q.parent),
    ] {
        if let Some(value) = value {
            filter += &format!(" AND {field}={}", bind(value.to_string(), "BIGINT"));
        }
    }
    if let Some(approvers) = q.approvers {
        let mut choices = vec!["FALSE".to_owned()];
        for approver in approvers {
            let user = bind(approver.user_id.to_string(), "BIGINT");
            let mut choice = format!(
                "EXISTS (SELECT 1 FROM workflow_tasks task WHERE task.request_id=r.id AND task.active=1 AND task.status='Waiting' AND task.assignee_id={user})"
            );
            if let Some(departments) = &approver.departments {
                let ids = departments
                    .iter()
                    .map(|d| bind(d.clone(), "TEXT"))
                    .collect::<Vec<_>>();
                choice += &if ids.is_empty() {
                    " AND FALSE".into()
                } else {
                    format!(" AND r.department IN ({})", ids.join(","))
                };
            }
            choices.push(choice);
        }
        filter += &format!(" AND ({})", choices.join(" OR "));
    }
    if let Some(user) = q.visible_user {
        let user = bind(user.to_string(), "BIGINT");
        let principals = q
            .visible_principals
            .iter()
            .map(|p| {
                (
                    bind(p.user_id.to_string(), "BIGINT"),
                    p.departments
                        .as_ref()
                        .map(|ds| ds.iter().map(|d| bind(d.clone(), "TEXT")).collect()),
                )
            })
            .collect::<Vec<_>>();
        filter += &format!(
            " AND ({} OR ({assigned}))",
            participant_condition(postgres, "r", &user, &principals)
        );
    }
    if let Some(actor) = q.approval_actor {
        let actor = bind(actor.to_string(), "BIGINT");
        filter += &format!(
            " AND r.owner_id<>{actor} AND COALESCE({},0)<>{actor}",
            property("applicantAccountId", true)
        );
    }
    if let Some(owner) = q.exclude_owner {
        filter += &format!(" AND r.owner_id<>{}", bind(owner.to_string(), "BIGINT"));
    }
    if let Some(applicant) = q.exclude_applicant {
        filter += &format!(
            " AND COALESCE({},0)<>{}",
            property("applicantAccountId", true),
            bind(applicant.to_string(), "BIGINT")
        );
    }
    QuerySql {
        filter,
        order: "r.id DESC".into(),
        values,
    }
}
pub(crate) fn participant_condition(
    postgres: bool,
    record: &str,
    user: &str,
    principals: &[(String, Option<Vec<String>>)],
) -> String {
    let mut choices = vec![
        format!("viewer.acted_by_id={user}"),
        format!("viewer.assignee_id={user}"),
    ];
    for (principal, departments) in principals {
        let scope = match departments {
            None => String::new(),
            Some(ds) if ds.is_empty() => " AND FALSE".into(),
            Some(ds) => format!(" AND {record}.department IN ({})", ds.join(",")),
        };
        choices.push(format!("(viewer.assignee_id={principal}{scope})"));
    }
    let cc = if postgres {
        format!(
            "jsonb_array_elements_text(COALESCE({record}.body->'approvalPlan'->'notifyUserIds','[]'::jsonb)) AS cc(value)"
        )
    } else {
        format!("json_each({record}.body,'$.approvalPlan.notifyUserIds') AS cc")
    };
    format!(
        "({record}.owner_id={user} OR EXISTS (SELECT 1 FROM workflow_tasks viewer WHERE viewer.request_id={record}.id AND viewer.submission=CAST({record}.body->>'submissionNumber' AS BIGINT) AND ({})) OR EXISTS (SELECT 1 FROM {cc} WHERE CAST(cc.value AS BIGINT)={user}))",
        choices.join(" OR ")
    )
}
