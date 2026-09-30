//! Shared filtering before counting/pagination; both adapters bind every value.
use super::QuerySql;
pub struct NotificationScope {
    pub kind: String,
    pub rank: u8,
    /// Empty accepts every parent state; otherwise filter before pagination.
    pub statuses: Vec<String>,
}
pub enum CommunicationView<'a> {
    Announcements { manage: bool, now: &'a str },
    Notifications { scopes: &'a [NotificationScope] },
}
pub struct CommunicationQuery<'a> {
    pub company: &'a str,
    pub department: &'a str,
    pub reader: i64,
    pub unread_only: bool,
    pub view: CommunicationView<'a>,
    pub offset: i64,
    pub limit: i64,
}
pub(super) fn sql(q: &CommunicationQuery<'_>, postgres: bool) -> QuerySql {
    let mut values = vec![];
    let mut bind = |value: String| {
        values.push(value);
        format!(
            "CAST({}{} AS TEXT)",
            if postgres { "$" } else { "?" },
            values.len()
        )
    };
    let company = bind(q.company.into());
    let mut filter = format!("r.company={company}");
    let order = match &q.view {
        CommunicationView::Announcements { manage, now } => {
            filter += " AND r.kind='announcement'";
            if !manage {
                let department = bind(q.department.into());
                let now = bind((*now).into());
                filter += &format!(
                    " AND r.body->>'status'='Published' AND r.body->>'startsAt'<={now} AND r.body->>'expiresAt'>{now} AND (r.body->>'audienceDepartment'='' OR r.body->>'audienceDepartment'={department})"
                );
            }
            if q.unread_only {
                let reader = format!("CAST({} AS BIGINT)", bind(q.reader.to_string()));
                filter += &format!(
                    " AND NOT EXISTS (SELECT 1 FROM records receipt WHERE receipt.kind='announcement-receipt' AND receipt.company=r.company AND receipt.owner_id={reader} AND CAST(receipt.body->>'requestId' AS BIGINT)=r.id AND CAST(receipt.body->>'publishVersion' AS BIGINT)=CAST(r.body->>'publishVersion' AS BIGINT))"
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
                let mut condition = match scope.rank {
                    1 => format!(" AND parent.owner_id={reader}"),
                    2 => format!(" AND parent.department={}", bind(q.department.into())),
                    3..=4 => String::new(),
                    _ => continue,
                };
                if !scope.statuses.is_empty() {
                    let states = scope
                        .statuses
                        .iter()
                        .map(|s| bind(s.clone()))
                        .collect::<Vec<_>>();
                    condition += &format!(" AND parent.body->>'status' IN ({})", states.join(","));
                }
                let kind = bind(scope.kind.clone());
                allowed.push(format!("(parent.kind={kind}{condition})"));
            }
            let allowed = if allowed.is_empty() {
                "FALSE".into()
            } else {
                allowed.join(" OR ")
            };
            filter += &format!(
                " AND EXISTS (SELECT 1 FROM records parent WHERE parent.id=CAST(r.body->>'requestId' AS BIGINT) AND parent.kind=r.body->>'requestKind' AND parent.company=r.company AND ({allowed}))"
            );
            "r.id DESC"
        }
    };
    QuerySql {
        filter,
        order,
        values,
    }
}
