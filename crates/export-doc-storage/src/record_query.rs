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
    let mut ordinary = if scope.is_empty() {
        "TRUE".into()
    } else {
        scope.join(" AND ")
    };
    let participant = q.visible_user.map(|user| {
        let user = bind(user.to_string(), "BIGINT");
        let context = q.visible_context.map(|c| bind(c.into(), "TEXT"));
        let principals = q
            .visible_principals
            .iter()
            .map(|p| {
                (
                    bind(p.user_id.to_string(), "BIGINT"),
                    bind(p.context.clone(), "TEXT"),
                )
            })
            .collect::<Vec<_>>();
        participant_condition(postgres, "r", &user, context.as_deref(), &principals)
    });
    if q.include_participants {
        if let Some(participant) = &participant {
            ordinary = format!("({ordinary}) OR ({participant})");
        }
    }
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
    if !q.statuses.is_empty() {
        let states = q
            .statuses
            .iter()
            .map(|s| bind((*s).into(), "TEXT"))
            .collect::<Vec<_>>();
        filter += &format!(
            " AND {} IN ({})",
            property("status", false),
            states.join(",")
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
        let principals = approvers
            .iter()
            .map(|p| {
                (
                    bind(p.user_id.to_string(), "BIGINT"),
                    bind(p.context.clone(), "TEXT"),
                )
            })
            .collect::<Vec<_>>();
        let actor = q.approval_actor.map(|a| bind(a.to_string(), "BIGINT"));
        filter += &format!(
            " AND ({})",
            approval_condition("r", actor.as_deref(), &principals)
        );
    }
    if let Some(participant) = participant.filter(|_| !q.include_participants) {
        filter += &format!(" AND ({} OR ({assigned}))", participant);
    }
    if let Some(actor) = q.approval_actor {
        let actor = bind(actor.to_string(), "BIGINT");
        filter += &format!(
            " AND r.owner_id<>{actor} AND COALESCE({},0)<>{actor}",
            property("applicantAccountId", true)
        );
    }
    let restricted = crate::DataScope {
        all: true,
        restrictions: q.restrictions.clone(),
        ..Default::default()
    }
    .filter(&mut values, postgres);
    filter += &format!(" AND ({restricted})");
    QuerySql {
        filter,
        order: "r.id DESC".into(),
        values,
    }
}
/// The inbox uses the same submission, context and independent-signature rule
/// for ordinary OA and resource approvals, before counting or paging.
pub(crate) fn approval_condition(
    record: &str,
    actor: Option<&str>,
    principals: &[(String, String)],
) -> String {
    let choices = principals.iter().map(|(user, context)| {
        let independence = actor.map(|actor| format!(" AND NOT EXISTS (SELECT 1 FROM workflow_current_tasks signed WHERE signed.request_id=task.request_id AND signed.submission=task.submission AND signed.acted_by_id={actor} AND (signed.node_key=task.node_key OR task.independent=1 OR signed.independent=1))")).unwrap_or_default();
        format!("EXISTS (SELECT 1 FROM workflow_current_tasks task WHERE task.request_id={record}.id AND task.submission=CAST({record}.body->>'submissionNumber' AS BIGINT) AND task.active=1 AND task.status='Waiting' AND task.assignee_id={user} AND task.context_id={context}{independence})")
    }).collect::<Vec<_>>();
    if choices.is_empty() {
        "FALSE".into()
    } else {
        choices.join(" OR ")
    }
}
pub(crate) fn participant_condition(
    postgres: bool,
    record: &str,
    user: &str,
    context: Option<&str>,
    principals: &[(String, String)],
) -> String {
    let signed_context = context
        .map(|c| format!(" AND viewer.acted_by_context={c}"))
        .unwrap_or_default();
    let assigned_context = context
        .map(|c| format!(" AND viewer.context_id={c}"))
        .unwrap_or_default();
    let mut choices = vec![
        format!("(viewer.acted_by_id={user}{signed_context})"),
        format!("(viewer.assignee_id={user}{assigned_context})"),
    ];
    for (principal, context) in principals {
        choices.push(format!(
            "(viewer.assignee_id={principal} AND viewer.context_id={context})"
        ));
    }
    let cc = if postgres {
        format!(
            "jsonb_array_elements_text(COALESCE({record}.body->'approvalPlan'->'notifyUserIds','[]'::jsonb)) AS cc(value)"
        )
    } else {
        format!("json_each({record}.body,'$.approvalPlan.notifyUserIds') AS cc")
    };
    format!(
        "({record}.owner_id={user} OR EXISTS (SELECT 1 FROM workflow_current_tasks viewer WHERE viewer.request_id={record}.id AND viewer.submission=CAST({record}.body->>'submissionNumber' AS BIGINT) AND ({})) OR EXISTS (SELECT 1 FROM {cc} WHERE CAST(cc.value AS BIGINT)={user}))",
        choices.join(" OR ")
    )
}
