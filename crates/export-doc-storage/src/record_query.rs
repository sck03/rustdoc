//! Shared record filters; values are bound by both database adapters.
use super::{QuerySql, RecordQuery};
pub(super) fn sql(q: &RecordQuery<'_>, postgres: bool) -> QuerySql {
    // Preserve each adapter's indexed JSON expression, including SQLite's
    // json_extract expression indexes from the version-5 schema.
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
        let assigned = format!("COALESCE({},0)", property("currentApproverId", true));
        let mut choices = vec![format!("{assigned}=0")];
        for approver in approvers {
            let mut choice = format!(
                "EXISTS (SELECT 1 FROM {} WHERE step.value->>'status'='Waiting' AND {}=(SELECT MIN({}) FROM {} WHERE next.value->>'status'='Waiting') AND CAST(step.value->>'approverUserId' AS BIGINT)={})",
                steps(postgres, "step"),
                stage(postgres, "step"),
                stage(postgres, "next"),
                steps(postgres, "next"),
                bind(approver.user_id.to_string(), "BIGINT")
            );
            if let Some(departments) = &approver.departments {
                let parameters = departments
                    .iter()
                    .map(|department| bind(department.clone(), "TEXT"))
                    .collect::<Vec<_>>();
                choice += &if parameters.is_empty() {
                    " AND FALSE".into()
                } else {
                    format!(" AND r.department IN ({})", parameters.join(","))
                };
            }
            choices.push(format!("({choice})"));
        }
        filter += &format!(" AND ({})", choices.join(" OR "));
    }
    if let Some(user) = q.visible_user {
        let user = bind(user.to_string(), "BIGINT");
        let principals = q
            .visible_principals
            .iter()
            .map(|principal| {
                (
                    bind(principal.user_id.to_string(), "BIGINT"),
                    principal.departments.as_ref().map(|departments| {
                        departments
                            .iter()
                            .map(|department| bind(department.clone(), "TEXT"))
                            .collect()
                    }),
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
        let used = if postgres {
            "SELECT 1 FROM jsonb_array_elements(COALESCE(r.body->'approvalPlan'->'steps','[]'::jsonb)) step WHERE CAST(step->>'actedByUserId' AS BIGINT)"
        } else {
            "SELECT 1 FROM json_each(r.body,'$.approvalPlan.steps') step WHERE CAST(step.value->>'actedByUserId' AS BIGINT)"
        };
        filter += &format!(
            " AND (COALESCE({},0)=0 OR (r.owner_id<>{actor} AND COALESCE({},0)<>{actor} AND NOT EXISTS ({used}={actor})))",
            property("currentApproverId", true),
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
        order: "r.id DESC",
        values,
    }
}

fn steps(postgres: bool, alias: &str) -> String {
    if postgres {
        format!(
            "jsonb_array_elements(COALESCE(r.body->'approvalPlan'->'steps','[]'::jsonb)) WITH ORDINALITY AS {alias}(value,ordinality)"
        )
    } else {
        format!("json_each(r.body,'$.approvalPlan.steps') AS {alias}")
    }
}
fn stage(postgres: bool, alias: &str) -> String {
    let ordinal = if postgres {
        format!("{alias}.ordinality-1")
    } else {
        format!("CAST({alias}.key AS BIGINT)")
    };
    format!("COALESCE(CAST({alias}.value->>'stage' AS BIGINT),{ordinal})")
}

pub(crate) fn participant_condition(
    postgres: bool,
    record: &str,
    user: &str,
    principals: &[(String, Option<Vec<String>>)],
) -> String {
    let mut choices = vec![
        format!("CAST(viewer.value->>'actedByUserId' AS BIGINT)={user}"),
        format!("CAST(viewer.value->>'approverUserId' AS BIGINT)={user}"),
    ];
    for (principal, departments) in principals {
        let scope = match departments {
            None => String::new(),
            Some(departments) if departments.is_empty() => " AND FALSE".into(),
            Some(departments) => format!(" AND {record}.department IN ({})", departments.join(",")),
        };
        choices.push(format!(
            "(CAST(viewer.value->>'approverUserId' AS BIGINT)={principal}{scope})"
        ));
    }
    let source = if postgres {
        format!(
            "jsonb_array_elements(COALESCE({record}.body->'approvalPlan'->'steps','[]'::jsonb)) AS viewer(value)"
        )
    } else {
        format!("json_each({record}.body,'$.approvalPlan.steps') AS viewer")
    };
    format!(
        "({record}.owner_id={user} OR EXISTS (SELECT 1 FROM {source} WHERE {}))",
        choices.join(" OR ")
    )
}
