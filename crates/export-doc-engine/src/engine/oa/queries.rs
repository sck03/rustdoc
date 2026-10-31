use super::*;

pub(super) fn list(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    query: &[(&str, String)],
) -> Result<Value> {
    list_with(tx, actor, meta, query, None, &[])
}

pub(super) fn list_with(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    query: &[(&str, String)],
    authority: Option<&super::queue_authority::QueueAuthority>,
    fields: &[(&str, &str)],
) -> Result<Value> {
    let resource = text(meta, "resource");
    auth::authorize(actor, &resource, "view")?;
    let get = |name| {
        query
            .iter()
            .find(|(k, _)| *k == name)
            .map(|(_, v)| v.as_str())
            .unwrap_or("")
    };
    let handling = match get("handlingOnly") {
        "" | "false" => false,
        "true" if meta["kind"] != "expense" => true,
        _ => return Err(invalid("办理筛选参数无效。")),
    };
    let finance = match get("financeOnly") {
        "" | "false" => false,
        "true" if meta["kind"] == "expense" => true,
        _ => return Err(invalid("财务筛选参数无效。")),
    };
    let approvals = match get("approvalsOnly") {
        "" | "false" => false,
        "true" => true,
        _ => return Err(invalid("审批筛选参数无效。")),
    };
    if (finance || handling) && approvals {
        return Err(invalid("审批与财务接收不能同时筛选。"));
    }
    let scope_permission = if finance || handling {
        "complete"
    } else if approvals {
        "approve"
    } else {
        "view"
    };
    auth::authorize(actor, &resource, scope_permission)?;
    let mine = match get("mineOnly") {
        "" => !finance && !approvals && !handling,
        "true" => true,
        "false" => false,
        _ => return Err(invalid("筛选开关无效。")),
    };
    let status = if approvals && get("status").is_empty() {
        "Pending"
    } else if (finance || handling) && get("status").is_empty() {
        "Approved"
    } else {
        get("status")
    };
    if handling && mine {
        return Err(invalid("待我办理不能同时限定为本人提交。"));
    }
    if approvals && status != "Pending" {
        return Err(invalid("待我审批仅查询待审批申请。"));
    }
    if finance && !FINANCE_STATUSES.contains(&status) {
        return Err(invalid("财务接收仅查询已批准或已移交财务的报销单。"));
    }
    if handling && !handling::GENERAL_STATES.contains(&status) {
        return Err(invalid("待我办理仅查询已批准或已完成的申请。"));
    }
    if !status.is_empty()
        && ![
            "Draft",
            "Pending",
            "Approved",
            "Rejected",
            "Returned",
            "Cancelled",
            "Completed",
            "HandedOff",
        ]
        .contains(&status)
    {
        return Err(invalid("申请状态无效。"));
    }
    let mut rank = auth::scope_rank(actor, &resource, scope_permission);
    if approvals || handling && meta["kind"] != "general" {
        rank = rank.min(auth::scope_rank(actor, &resource, "view"));
    }
    if rank == 0 {
        return Err(error(403, "没有有效的申请数据访问范围。"));
    }
    let (page, size) = paging(query)?;
    let handling_keys = handling::keys(tx, actor, "office.general")?;
    let approvers = if approvals || actor.oa_assigned_only || !mine {
        match authority {
            Some(authority) => authority.approvers(&resource),
            None => approval::queue_approvers(tx, actor, &resource)?,
        }
    } else {
        vec![]
    };
    let (count, rows) = tx.query_records(&RecordQuery {
        restrictions: auth::scope_filters(actor, &resource, scope_permission),
        kind: &kind(meta),
        company: &actor.company,
        departments: (rank == 2 && !approvals && !actor.oa_assigned_only)
            .then_some(actor.departments.as_slice()),
        owner: (mine || (rank == 1 && !approvals && !actor.oa_assigned_only)).then_some(actor.id),
        status: (!status.is_empty()).then_some(status),
        approvers: approvals.then_some(approvers.as_slice()),
        approval_actor: approvals.then_some(actor.id),
        visible_user: (actor.oa_assigned_only || (!mine && !finance && !handling && !approvals))
            .then_some(actor.id),
        visible_principals: &approvers,
        visible_context: Some(&actor.context),
        include_participants: !actor.oa_assigned_only
            && !mine
            && !finance
            && !handling
            && !approvals,
        handling_keys: (meta["kind"] == "general" && !approvals && !mine)
            .then_some(handling_keys.as_slice()),
        handling_states: handling::GENERAL_STATES,
        handling_only: handling && meta["kind"] == "general",
        fields,
        offset: (page - 1) * size,
        limit: size,
        ..Default::default()
    })?;
    let rows = if fields.is_empty() {
        rows.into_iter()
            .map(|r| project(tx, actor, r, false))
            .collect::<Result<Vec<_>>>()?
    } else {
        rows.into_iter()
            .map(|row| materials::project(tx, actor, &row, row.clone()))
            .collect::<Result<Vec<_>>>()?
    };
    Ok(json!({"items":rows,"totalCount":count,"pageNumber":page,"pageSize":size}))
}
