use super::*;

/// Called after an account or scheme write inside its transaction, so rejection rolls back both.
pub(in crate::engine) fn validate_account(
    tx: &Connection,
    actor: &Actor,
    user: &Value,
) -> Result<()> {
    let id = records::positive(user, "id", "账号")?;
    let company = text(user, "companyScope");
    let settings = settings::load(tx, &company)?;
    let rules: Vec<export_doc_domain::approval_policy::ApprovalRule> =
        serde_json::from_value(settings["rules"].clone())?;
    let executive = match super::super::personnel_accounts::person_for_account(tx, id)? {
        Some(person) => tx
            .find_identity("companies", &store::normalize(&company))?
            .is_some_and(|c| c["executiveEmployeeId"] == person["id"]),
        None => false,
    };
    let current = || {
        auth::current_actor_in(tx, id, actor.edition).map_err(|e| {
            if matches!(e.status, Some(403 | 404)) {
                conflict("账号仍承担审批或公司执行负责人职责，请先移交、更新规则后再调整权限。")
            } else {
                e
            }
        })
    };
    if executive && !auth::is_company_approver(&current()?) {
        return Err(conflict(
            "权限调整会使公司执行负责人失去公司审批职责，请先交接。",
        ));
    }
    if rules.iter().any(|r| r.account_ids().contains(&id)) {
        let reviewer = current()?;
        for kind in KINDS {
            if !rules
                .iter()
                .any(|r| r.kind == kind.trim_start_matches("oa-") && r.account_ids().contains(&id))
            {
                continue;
            }
            let resource = approval::resource(kind).ok_or_else(|| invalid("未知审批类型。"))?;
            let rank = auth::scope_rank(&reviewer, resource, "view")
                .min(auth::scope_rank(&reviewer, resource, "approve"));
            if rank < 2 {
                return Err(conflict(
                    "权限调整会使公司执行负责人或指定审批规则失效，请先交接职责或更新规则。",
                ));
            }
        }
    }
    for kind in KINDS {
        let mut offset = 0;
        loop {
            crate::operation::check()?;
            let (total, rows) = tx.query_records(&RecordQuery {
                kind,
                company: &company,
                status: Some("Pending"),
                visible_user: Some(id),
                offset,
                limit: 100,
                ..Default::default()
            })?;
            for row in rows {
                if approval::plan(&row)?.is_some_and(|p| {
                    p.steps
                        .iter()
                        .any(|s| s.approver_user_id == id && s.status == "Waiting")
                }) {
                    let reviewer = current()?;
                    let resource =
                        approval::resource(kind).ok_or_else(|| invalid("未知审批类型。"))?;
                    if !viewable(tx, &reviewer, resource, &row)?
                        || !auth::visible(&reviewer, resource, "approve", &row)
                    {
                        return Err(conflict(
                            "权限调整会使在途审批失去审批人，请先从申请详情移交未完成步骤。",
                        ));
                    }
                }
            }
            offset += 100;
            if offset >= total {
                break;
            }
        }
    }
    Ok(())
}

pub(in crate::engine) fn assigned(
    tx: &Connection,
    company: &str,
    user: i64,
    pending: bool,
) -> Result<i64> {
    let mut total = 0;
    for kind in KINDS {
        let mut offset = 0;
        loop {
            crate::operation::check()?;
            let (count, rows) = tx.query_records(&RecordQuery {
                kind,
                company,
                status: pending.then_some("Pending"),
                visible_user: Some(user),
                offset,
                limit: 100,
                ..Default::default()
            })?;
            for row in rows {
                if let Some(plan) = approval::plan(&row)? {
                    total += i64::from(plan.steps.iter().any(|s| {
                        if pending {
                            s.approver_user_id == user && s.status == "Waiting"
                        } else {
                            s.approver_user_id == user || s.acted_by_user_id == Some(user)
                        }
                    }));
                }
            }
            offset += 100;
            if offset >= count {
                break;
            }
        }
    }
    Ok(total)
}
pub(in crate::engine) fn check(tx: &Connection, kind: &str, row: &Value) -> Result<()> {
    if !matches!(kind, "users" | "companies") {
        return Ok(());
    }
    let company = text(
        row,
        if kind == "companies" {
            "code"
        } else {
            "companyScope"
        },
    );
    let settings = settings::load(tx, &company)?;
    let referenced = if kind == "companies" {
        settings["id"].as_i64().is_some()
    } else {
        let user = records::positive(row, "id", "账号")?;
        let rules: Vec<export_doc_domain::approval_policy::ApprovalRule> =
            serde_json::from_value(settings["rules"].clone())?;
        rules.iter().any(|rule| rule.account_ids().contains(&user))
            || settings["delegations"].as_array().is_some_and(|items| {
                items
                    .iter()
                    .any(|d| d["principalUserId"] == user || d["delegateUserId"] == user)
            })
            || handling::services(tx, &company)?.iter().any(|s| {
                s["handlerUserIds"]
                    .as_array()
                    .is_some_and(|ids| ids.contains(&json!(user)))
            })
            || assigned(tx, &company, user, false)? > 0
    };
    if referenced {
        return Err(conflict(
            "账号或公司仍有审批规则、代理或审批历史引用，不能删除。",
        ));
    }
    Ok(())
}
