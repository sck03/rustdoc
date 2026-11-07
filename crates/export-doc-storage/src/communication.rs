//! Shared filtering before counting/pagination; both adapters bind every value.
use super::QuerySql;
pub struct NotificationScope {
    pub workflow_date: Option<chrono::NaiveDate>,
    pub restrictions: Vec<crate::ScopeFilter>,
    pub kind: String,
    pub rank: u8,
    /// Empty accepts every parent state; otherwise filter before pagination.
    pub statuses: Vec<String>,
    pub handling_keys: Option<Vec<String>>,
    pub approval_principals: Vec<crate::RecordApprover>,
    pub node_access: Vec<crate::NodeAccess>,
}
pub enum CommunicationView<'a> {
    Announcements { manage: bool, now: &'a str },
    Notifications { scopes: &'a [NotificationScope] },
}
pub struct CommunicationQuery<'a> {
    pub context: &'a str,
    pub company: &'a str,
    pub department: &'a str,
    pub departments: &'a [String],
    pub assigned_only: bool,
    pub reader: i64,
    pub unread_only: bool,
    pub view: CommunicationView<'a>,
    pub offset: i64,
    pub limit: i64,
}
pub(super) fn sql(q: &CommunicationQuery<'_>, postgres: bool) -> QuerySql {
    let mut values = vec![];
    let mut bind = |value: String| crate::sql::bind(&mut values, value, postgres);
    let company = bind(q.company.into());
    let mut filter = format!("r.company={company}");
    let order = match &q.view {
        CommunicationView::Announcements { manage, now } => {
            filter += " AND r.kind='announcement'";
            if !manage {
                let department = std::iter::once(q.department)
                    .chain(q.departments.iter().map(String::as_str))
                    .map(|department| bind(department.into()))
                    .collect::<Vec<_>>()
                    .join(",");
                let now = bind((*now).into());
                let (now_seconds, now_nanos) = crate::sql::instant_order(&now, postgres);
                let (start_seconds, start_nanos) =
                    crate::sql::instant_order("r.body->>'startsAt'", postgres);
                let (end_seconds, end_nanos) =
                    crate::sql::instant_order("r.body->>'expiresAt'", postgres);
                filter += &format!(
                    " AND r.body->>'status'='Published' AND ({start_seconds},{start_nanos})<=({now_seconds},{now_nanos}) AND ({end_seconds},{end_nanos})>({now_seconds},{now_nanos}) AND (r.body->>'audienceDepartment'='' OR r.body->>'audienceDepartment' IN ({department}))"
                );
            }
            if q.unread_only {
                let reader = format!("CAST({} AS BIGINT)", bind(q.reader.to_string()));
                filter += &format!(
                    " AND NOT EXISTS (SELECT 1 FROM record_rows receipt WHERE receipt.kind='announcement-receipt' AND receipt.company=r.company AND receipt.owner_id={reader} AND CAST(receipt.body->>'requestId' AS BIGINT)=r.id AND CAST(receipt.body->>'publishVersion' AS BIGINT)=CAST(r.body->>'publishVersion' AS BIGINT))"
                );
            }
            "CASE WHEN CAST(r.body->>'isPinned' AS TEXT) IN ('true','1') THEN 1 ELSE 0 END DESC, r.id DESC"
        }
        CommunicationView::Notifications { scopes } => {
            let reader = format!("CAST({} AS BIGINT)", bind(q.reader.to_string()));
            filter += &format!(" AND r.kind='site-notification' AND r.owner_id={reader}");
            if q.unread_only {
                filter += " AND r.body->>'status'='Unread'";
            }
            let mut allowed = vec![];
            for scope in *scopes {
                if scope.handling_keys.as_ref().is_some_and(Vec::is_empty) {
                    continue;
                }
                let mut condition = match scope.rank {
                    1..=4 if q.assigned_only && scope.handling_keys.is_none() => String::new(),
                    1 => format!(" AND parent.owner_id={reader}"),
                    2 => format!(
                        " AND parent.department IN ({})",
                        std::iter::once(q.department)
                            .chain(q.departments.iter().map(String::as_str))
                            .map(|department| bind(department.into()))
                            .collect::<Vec<_>>()
                            .join(",")
                    ),
                    3..=4 => String::new(),
                    _ => continue,
                };
                let status = if scope.statuses.is_empty() {
                    String::new()
                } else {
                    let states = scope
                        .statuses
                        .iter()
                        .map(|s| bind(s.clone()))
                        .collect::<Vec<_>>();
                    format!(" AND parent.body->>'status' IN ({})", states.join(","))
                };
                condition += &status;
                if let Some(keys) = &scope.handling_keys {
                    let keys = keys.iter().map(|key| bind(key.clone())).collect::<Vec<_>>();
                    condition +=
                        &format!(" AND parent.body->>'handlingKey' IN ({})", keys.join(","));
                }
                if scope.handling_keys.is_none() {
                    let principals = scope
                        .approval_principals
                        .iter()
                        .map(|p| {
                            (
                                format!("CAST({} AS BIGINT)", bind(p.user_id.to_string())),
                                bind(p.context.clone()),
                                crate::generic_query::restriction_condition(
                                    &p.restrictions,
                                    "parent",
                                    postgres,
                                    &mut bind,
                                ),
                            )
                        })
                        .collect::<Vec<_>>();
                    let node_access = crate::record_query::node_access_condition(
                        postgres,
                        "parent",
                        &scope.node_access,
                        &mut bind,
                    );
                    let participant = crate::record_query::participant_condition(
                        postgres,
                        "parent",
                        &reader,
                        Some(&bind(q.context.into())),
                        &principals,
                        &node_access,
                        scope.workflow_date,
                    );
                    condition = if q.assigned_only {
                        format!(" AND {participant}{status}")
                    } else {
                        format!(" AND ((TRUE {condition}) OR {participant})")
                    };
                }
                let kind = bind(scope.kind.clone());
                let restriction = crate::generic_query::restriction_condition(
                    &scope.restrictions,
                    "parent",
                    postgres,
                    &mut bind,
                );
                allowed.push(format!(
                    "(parent.kind={kind}{condition} AND ({restriction}))"
                ));
            }
            let allowed = if allowed.is_empty() {
                "FALSE".into()
            } else {
                allowed.join(" OR ")
            };
            filter += &format!(
                " AND EXISTS (SELECT 1 FROM record_rows parent WHERE parent.id=CAST(r.body->>'requestId' AS BIGINT) AND parent.kind=r.body->>'requestKind' AND parent.company=r.company AND ({allowed}))"
            );
            "r.id DESC"
        }
    };
    QuerySql {
        source: crate::query_source::Source::Projected,
        filter,
        order: order.into(),
        values,
    }
}
