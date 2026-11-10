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
    if let Some(key) = q.definition_key {
        filter += &format!(
            " AND {}={}",
            property("definitionKey", false),
            bind(key.into(), "TEXT")
        );
    }
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
                    crate::generic_query::restriction_condition(
                        &p.restrictions,
                        "r",
                        postgres,
                        &mut |v| bind(v, "TEXT"),
                    ),
                )
            })
            .collect::<Vec<_>>();
        let node_access = node_access_condition(postgres, "r", &q.node_access, &mut |value| {
            bind(value, "TEXT")
        });
        participant_condition(
            postgres,
            "r",
            &user,
            context.as_deref(),
            &principals,
            &node_access,
            q.workflow_date,
        )
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
    if let Some(user) = q.handling_user {
        let user = bind(user.to_string(), "BIGINT");
        let context = q
            .handling_context
            .map(|c| bind(c.into(), "TEXT"))
            .unwrap_or_else(|| "''".into());
        let person = format!("(SELECT person_id FROM personnel_accounts WHERE user_id={user})");
        let handler_origin = handler_origin_condition(postgres, "r", &user);
        let current_origin = crate::workflow_origin::condition(
            postgres,
            "r",
            "assignment.origin",
            "assignment.person_id",
            q.workflow_date,
        );
        let origin_available = format!("({handler_origin}) AND ({current_origin})");
        let completed = q
            .status
            .is_some_and(|state| matches!(state, "Completed" | "HandedOff"));
        let assigned = format!(
            "EXISTS(SELECT 1 FROM workflow_instances instance JOIN node_instances node ON node.instance_id=instance.id AND node.generation=instance.generation JOIN workflow_node_actors assignment ON assignment.instance_id=node.instance_id AND assignment.generation=node.generation AND assignment.node_key=node.node_key WHERE instance.request_id=r.id AND instance.revision=CAST(r.body->>'submissionNumber' AS BIGINT) AND instance.suspended_at='' AND node.phase='Fulfillment' AND (node.execution_state='Active' OR ({completed} AND node.execution_state='Completed') OR node.execution_state='Pending' AND r.body->>'reservationState'='Insufficient' AND node.definition->>'handlingAction'='Issue') AND assignment.activation_level=0 AND assignment.actor_id={user} AND assignment.context_id={context} AND ({origin_available}) AND (assignment.person_id={person} OR assignment.person_id IS NULL AND assignment.administration=1 AND EXISTS(SELECT 1 FROM accounts a WHERE a.id={user} AND a.role_code='Admin' AND a.is_active=1)))"
        );
        let principals = q
            .handling_principals
            .unwrap_or(&[])
            .iter()
            .map(|p| {
                (
                    bind(p.user_id.to_string(), "BIGINT"),
                    bind(p.context.clone(), "TEXT"),
                    crate::generic_query::restriction_condition(
                        &p.restrictions,
                        "r",
                        postgres,
                        &mut |v| bind(v, "TEXT"),
                    ),
                )
            })
            .collect::<Vec<_>>();
        filter += &format!(
            " AND ({assigned} OR ({}))",
            approval_condition(postgres, "r", Some(&user), &principals, q.workflow_date)
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
            "bookings" => {
                let instant = |value: String| {
                    if postgres {
                        format!("CAST({value} AS TIMESTAMPTZ)")
                    } else {
                        format!("julianday({value})")
                    }
                };
                let starts = instant(property("startsAt", false));
                let ends = instant(property("endsAt", false));
                let damaged = if postgres {
                    "FALSE".into()
                } else {
                    format!("{starts} IS NULL OR {ends} IS NULL")
                };
                filter += &format!(
                    " AND ({damaged} OR ({starts}<{} AND ({ends}>{} OR {}='InUse')))",
                    instant(last),
                    instant(first),
                    property("status", false)
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
    if let Some(id) = q.resource_id {
        let field = match q.kind {
            "bookings" => Some("meetingRoomId"),
            "supply-requests" => Some("officeSupplyId"),
            _ => None,
        };
        filter += &field
            .map(|field| {
                format!(
                    " AND {}={}",
                    property(field, true),
                    bind(id.to_string(), "BIGINT")
                )
            })
            .unwrap_or_else(|| " AND FALSE".into());
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
                    crate::generic_query::restriction_condition(
                        &p.restrictions,
                        "r",
                        postgres,
                        &mut |v| bind(v, "TEXT"),
                    ),
                )
            })
            .collect::<Vec<_>>();
        let actor = q.approval_actor.map(|a| bind(a.to_string(), "BIGINT"));
        filter += &format!(
            " AND ({})",
            approval_condition(
                postgres,
                "r",
                actor.as_deref(),
                &principals,
                q.workflow_date
            )
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
        source: crate::query_source::Source::Projected,
        filter,
        order: "r.id DESC".into(),
        values,
    }
}
pub(crate) fn page(
    db: &(impl crate::Adapter + ?Sized),
    q: &RecordQuery<'_>,
) -> crate::Result<(i64, Vec<serde_json::Value>)> {
    let pg = db.provider() == "PostgreSQL";
    if !crate::workflow_runtime::request_kind(q.kind) {
        let sql = sql(q, pg);
        let projection = crate::sql::projection(q.fields, pg)?;
        return db.query_page(&sql, &projection, q.offset, q.limit.clamp(1, 100));
    }
    let (statement, values) = statement(q, pg)?;
    let value = db.query_json(&statement, &values)?;
    let count = value["count"]
        .as_i64()
        .ok_or_else(|| crate::Error::unavailable("申请统计结果无效。"))?;
    Ok((count, serde_json::from_value(value["items"].clone())?))
}
pub(crate) fn statement(q: &RecordQuery<'_>, pg: bool) -> crate::Result<(String, Vec<String>)> {
    let sql = sql(q, pg);
    let projection = crate::sql::projection(q.fields, pg)?;
    if !crate::workflow_runtime::request_kind(q.kind) {
        return Ok((
            crate::query_source::page(&sql, &projection, q.offset, q.limit.clamp(1, 100), pg),
            sql.values,
        ));
    }
    let filter = sql.filter;
    let order = sql.order;
    let limit = q.limit.clamp(1, 100);
    let offset = q.offset.max(0);
    let ids = format!(
        "SELECT r.id FROM workflow_record_filters r WHERE {filter} ORDER BY {order} LIMIT {limit} OFFSET {offset}"
    );
    let select = format!(
        "SELECT {projection} AS body FROM workflow_list_records r WHERE r.id IN ({ids}) ORDER BY {order}"
    );
    let aggregate = if pg {
        format!("COALESCE((SELECT jsonb_agg(body::jsonb) FROM ({select}) page),'[]'::jsonb)")
    } else {
        format!("json((SELECT json_group_array(json(body)) FROM ({select}) page))")
    };
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let cast = if pg { "::text" } else { "" };
    Ok((
        format!(
            "SELECT {object}('count',(SELECT COUNT(*) FROM workflow_record_filters r WHERE {filter}),'items',{aggregate}){cast}"
        ),
        sql.values,
    ))
}
/// The inbox uses the same submission, context and independent-signature rule
/// for ordinary OA and resource approvals, before counting or paging.
pub(crate) fn approval_condition(
    postgres: bool,
    record: &str,
    actor: Option<&str>,
    principals: &[(String, String, String)],
    date: Option<chrono::NaiveDate>,
) -> String {
    if principals.is_empty() {
        return "FALSE".into();
    }
    let choices = principals
        .iter()
        .map(|(user, context, restriction)| {
            format!("(task.assignee_id={user} AND task.context_id={context} AND ({restriction}))")
        })
        .collect::<Vec<_>>();
    let independence = actor.map(|actor| format!(" AND NOT EXISTS (SELECT 1 FROM workflow_current_tasks signed WHERE signed.request_id=task.request_id AND signed.submission=task.submission AND signed.acted_by_id IS NOT NULL AND (signed.acted_by_id={actor} OR signed.acted_by_person_id=(SELECT person_id FROM personnel_accounts WHERE user_id={actor}) OR signed.assignee_person_id=(SELECT person_id FROM personnel_accounts WHERE user_id={actor})) AND (signed.node_key=task.node_key OR task.independent=1 OR signed.independent=1))")).unwrap_or_default();
    let origin = crate::workflow_origin::condition(
        postgres,
        record,
        "task.origin",
        "task.assignee_person_id",
        date,
    );
    format!(
        "EXISTS (SELECT 1 FROM workflow_current_tasks task WHERE task.request_id={record}.id AND task.submission=CAST({record}.body->>'submissionNumber' AS BIGINT) AND task.active=1 AND task.status='Waiting' AND task.assignee_person_id=(SELECT person_id FROM personnel_accounts WHERE user_id=task.assignee_id){independence} AND ({origin}) AND ({}))",
        choices.join(" OR ")
    )
}
fn handler_origin_condition(postgres: bool, record: &str, user: &str) -> String {
    let key = format!("{record}.body->>'handlingKey'");
    let members = if postgres {
        "records policy CROSS JOIN LATERAL jsonb_array_elements(policy.body->'value') service(value) CROSS JOIN LATERAL jsonb_array_elements_text(service.value->'handlerUserIds') handler(value)"
    } else {
        "records policy JOIN json_each(policy.body,'$.value') service JOIN json_each(service.value,'$.handlerUserIds') handler"
    };
    // Keep historical actors as facts, while service handover revokes their
    // derived access in the same snapshot as list counts and pagination.
    format!(
        "(COALESCE(assignment.origin->>'source','')<>'Handlers' OR assignment.administration=1 OR COALESCE({key},'')='' OR EXISTS(SELECT 1 FROM {members} WHERE policy.kind='oa-setting-section' AND policy.body->>'key'='handlingServices' AND policy.company={record}.company AND service.value->>'key'={key} AND CAST(handler.value AS BIGINT)={user}))"
    )
}
pub(crate) fn node_access_condition(
    postgres: bool,
    record: &str,
    accesses: &[crate::NodeAccess],
    bind: &mut impl FnMut(String) -> String,
) -> String {
    let choices = accesses
        .iter()
        .filter_map(|access| {
            let actions = match access.action {
                "complete" => "'Complete','Receive','Acceptance'",
                "issue" => "'Issue'",
                "return" => "'Return'",
                _ => return None,
            };
            let restrictions = crate::generic_query::restriction_condition(
                &access.restrictions,
                record,
                postgres,
                bind,
            );
            Some(format!(
                "(node.definition->>'handlingAction' IN ({actions}) AND ({restrictions}))"
            ))
        })
        .collect::<Vec<_>>();
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
    principals: &[(String, String, String)],
    handling_access: &str,
    date: Option<chrono::NaiveDate>,
) -> String {
    let signed_context = context
        .map(|c| format!(" AND viewer.acted_by_context={c}"))
        .unwrap_or_default();
    let assigned_context = context
        .map(|c| format!(" AND viewer.context_id={c}"))
        .unwrap_or_default();
    let current_origin = crate::workflow_origin::condition(
        postgres,
        record,
        "viewer.origin",
        "viewer.assignee_person_id",
        date,
    );
    let mut choices = vec![
        format!(
            "(viewer.acted_by_id={user} AND viewer.acted_by_person_id=(SELECT person_id FROM personnel_accounts WHERE user_id={user}){signed_context})"
        ),
        format!(
            "(viewer.assignee_id={user} AND viewer.assignee_person_id=(SELECT person_id FROM personnel_accounts WHERE user_id={user}){assigned_context} AND ({current_origin}))"
        ),
    ];
    for (principal, context, restriction) in principals {
        choices.push(format!(
            "(viewer.assignee_id={principal} AND viewer.assignee_person_id=(SELECT person_id FROM personnel_accounts WHERE user_id={principal}) AND viewer.context_id={context} AND ({restriction}) AND ({current_origin}))"
        ));
    }
    let cc = if postgres {
        "workflow_instances notify CROSS JOIN LATERAL jsonb_array_elements_text(COALESCE(notify.definition->'notifyUserIds','[]'::jsonb)) cc(value)"
    } else {
        "workflow_instances notify JOIN json_each(notify.definition,'$.notifyUserIds') cc"
    };
    let person = format!("(SELECT person_id FROM personnel_accounts WHERE user_id={user})");
    let actor_context = context
        .map(|c| format!(" AND assignment.context_id={c}"))
        .unwrap_or_default();
    let handler_origin = handler_origin_condition(postgres, record, user);
    let current_origin = crate::workflow_origin::condition(
        postgres,
        record,
        "assignment.origin",
        "assignment.person_id",
        date,
    );
    let origin_available = format!("({handler_origin}) AND ({current_origin})");
    let supervision = if postgres {
        "node.definition->'escalations'->(CAST(assignment.activation_level AS INTEGER)-1)->>'action'"
    } else {
        "json_extract(node.definition,'$.escalations['||(assignment.activation_level-1)||'].action')"
    };
    let node_access = format!(
        "(node.definition->>'kind'<>'Handling' OR (assignment.activation_level>0 AND {supervision}='NotifySupervisor') OR ({handling_access}))"
    );
    let node_actors = format!(
        "EXISTS(SELECT 1 FROM workflow_instances assigned JOIN node_instances node ON node.instance_id=assigned.id AND node.generation=assigned.generation JOIN workflow_node_actors assignment ON assignment.instance_id=node.instance_id AND assignment.generation=node.generation AND assignment.node_key=node.node_key WHERE assigned.request_id={record}.id AND assigned.revision=CAST({record}.body->>'submissionNumber' AS BIGINT) AND assignment.actor_id={user}{actor_context} AND node.started_at<>'' AND assignment.activation_level<=node.escalation_cursor AND ({node_access}) AND ({origin_available}) AND (assignment.person_id={person} OR assignment.person_id IS NULL AND assignment.administration=1 AND EXISTS(SELECT 1 FROM accounts account WHERE account.id={user} AND account.role_code='Admin' AND account.is_active=1)))"
    );
    format!(
        "({record}.owner_id={user} OR EXISTS (SELECT 1 FROM workflow_current_tasks viewer WHERE viewer.request_id={record}.id AND viewer.submission=CAST({record}.body->>'submissionNumber' AS BIGINT) AND ({})) OR {node_actors} OR EXISTS (SELECT 1 FROM {cc} WHERE notify.request_id={record}.id AND notify.revision=CAST({record}.body->>'submissionNumber' AS BIGINT) AND CAST(cc.value AS BIGINT)={user}))",
        choices.join(" OR ")
    )
}
