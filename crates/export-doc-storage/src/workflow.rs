//! Authorized read projections over canonical workflow and handling state.
pub struct WorkflowProgress<'a> {
    pub approval: &'a str,
    pub handling: Option<(&'a str, &'a str)>,
}

pub struct WorkflowTaskQuery<'a> {
    pub company: &'a str,
    pub assignee: Option<i64>,
    pub offset: i64,
    pub limit: i64,
}

pub struct WorkflowSummaryQuery<'a> {
    pub companies: &'a [String],
    pub as_of: &'a str,
    pub completed_since: &'a str,
    pub offset: i64,
    pub limit: i64,
}

/// Limit the unit set before joining tasks, aggregation, counting and paging.
pub(crate) fn summary(q: &WorkflowSummaryQuery<'_>, pg: bool) -> (String, Vec<String>) {
    let mut values = vec![];
    let units = q
        .companies
        .iter()
        .map(|c| crate::sql::bind(&mut values, c, pg))
        .collect::<Vec<_>>();
    let filter = if units.is_empty() {
        "1=0".to_owned()
    } else {
        format!("u.code IN ({})", units.join(","))
    };
    let now = crate::sql::timestamp(&crate::sql::bind(&mut values, q.as_of, pg), pg);
    let due = crate::sql::timestamp("t.due_at", pg);
    let since = crate::sql::timestamp(&crate::sql::bind(&mut values, q.completed_since, pg), pg);
    let acted = crate::sql::timestamp("t.acted_at", pg);
    let json = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let projection = format!(
        "{json}('unitId',u.id,'companyCode',u.code,'companyName',u.name,'requestCount',p.requests,'activeTaskCount',p.active,'queuedTaskCount',p.queued,'overdueTaskCount',p.overdue,'completedTaskCount',COALESCE(h.completed,0),'averageProcessingSeconds',h.elapsed)"
    );
    let rows = if pg {
        "COALESCE((SELECT jsonb_agg(body) FROM page),'[]'::jsonb)::text"
    } else {
        "(SELECT json_group_array(json(body)) FROM page)"
    };
    (
        format!(
            "WITH units AS (SELECT u.id,u.code,c.body->>'name' AS name FROM organization_units u JOIN record_rows c ON c.id=u.id WHERE {filter}),
            requests AS (SELECT r.id,r.company,r.body->>'status' AS status,CAST(r.body->>'submissionNumber' AS BIGINT) AS submission FROM record_rows r JOIN units u ON r.company=u.code WHERE r.kind IN ('oa-leave','oa-overtime','oa-expense','oa-travel','oa-purchase','oa-general','oa-personnel','bookings','supply-requests')),
            pending AS (SELECT u.id,COUNT(DISTINCT r.id) AS requests,SUM(CASE WHEN t.active=1 THEN 1 ELSE 0 END) AS active,SUM(CASE WHEN t.active=0 THEN 1 ELSE 0 END) AS queued,SUM(CASE WHEN t.active=1 AND t.due_at<>'' THEN CASE WHEN {due}<{now} THEN 1 ELSE 0 END ELSE 0 END) AS overdue FROM units u LEFT JOIN requests r ON r.company=u.code AND r.status='Pending' LEFT JOIN workflow_current_tasks t ON t.request_id=r.id AND t.submission=r.submission AND t.status='Waiting' GROUP BY u.id),
            handled AS (SELECT r.company,COUNT(*) AS completed,AVG(CAST(t.elapsed_seconds AS DOUBLE PRECISION)) AS elapsed FROM requests r JOIN workflow_tasks t ON t.request_id=r.id WHERE t.signature IS NOT NULL AND CASE WHEN t.status IN ('Approved','Rejected','Returned') THEN {acted}>={since} AND {acted}<={now} ELSE FALSE END GROUP BY r.company),
            summary AS (SELECT u.id,{projection} AS body FROM units u JOIN pending p ON p.id=u.id LEFT JOIN handled h ON h.company=u.code), page AS (SELECT body FROM summary ORDER BY id LIMIT {limit} OFFSET {offset}) SELECT (SELECT COUNT(*) FROM summary),{rows}",
            limit = q.limit.clamp(1, 100),
            offset = q.offset.max(0)
        ),
        values,
    )
}

/// Administrative task projection contains no request body or attachment fields.
pub(crate) fn page(q: &WorkflowTaskQuery<'_>, pg: bool) -> (String, Vec<String>) {
    let mut values = vec![];
    let company = crate::sql::bind(&mut values, q.company, pg);
    let mut filter = format!(
        "r.company={company} AND r.body->>'status'='Pending' AND t.status='Waiting' AND t.submission=CAST(r.body->>'submissionNumber' AS BIGINT)"
    );
    if let Some(id) = q.assignee {
        filter += &format!(
            " AND t.assignee_id=CAST({} AS BIGINT)",
            crate::sql::bind(&mut values, id, pg)
        );
    }
    let active = if pg {
        "(t.active=1)"
    } else {
        "json(CASE WHEN t.active=1 THEN 'true' ELSE 'false' END)"
    };
    let json = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let projection = format!(
        "{json}('requestId',r.id,'requestKind',r.kind,'companyCode',r.company,'versionNumber',r.version,'taskKey',t.task_key,'nodeKey',t.node_key,'assigneeId',t.assignee_id,'assigneeName',COALESCE(u.body->>'fullName',''),'contextId',t.context_id,'active',{active},'dueAt',t.due_at)"
    );
    let source = "workflow_current_tasks t JOIN record_rows r ON r.id=t.request_id LEFT JOIN record_rows u ON u.id=t.assignee_id AND u.kind='users'";
    let rows = if pg {
        "COALESCE((SELECT jsonb_agg(body) FROM page),'[]'::jsonb)::text"
    } else {
        "(SELECT json_group_array(json(body)) FROM page)"
    };
    (
        format!(
            "WITH matched AS (SELECT {projection} AS body,t.active,t.due_at,r.id,t.task_key FROM {source} WHERE {filter}), page AS (SELECT body FROM matched ORDER BY active DESC,due_at,id,task_key LIMIT {limit} OFFSET {offset}) SELECT (SELECT COUNT(*) FROM matched),{rows}",
            limit = q.limit.clamp(1, 100),
            offset = q.offset.max(0)
        ),
        values,
    )
}
