use super::*;

pub(super) const WORKFLOWS: &[&str] = &[
    "oa-leave",
    "oa-overtime",
    "oa-expense",
    "oa-travel",
    "oa-purchase",
    "oa-general",
    "oa-personnel",
    "bookings",
    "supply-requests",
];

fn referenced_rules(
    tx: &Connection,
    company: &str,
    settings: &Value,
) -> Result<Vec<export_doc_domain::approval_policy::ApprovalRule>> {
    let mut rules: Vec<export_doc_domain::approval_policy::ApprovalRule> =
        serde_json::from_value(settings["rules"].clone())?;
    for publication in settings["publications"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|p| p["enabled"] == true)
    {
        let key = text(publication, "key");
        let (kind, variant) = key.split_once(':').unwrap_or((&key, ""));
        rules.push(super::definitions::published(tx, company, kind, variant)?.0);
    }
    Ok(rules)
}

/// Called after an account or scheme write inside its transaction, so rejection rolls back both.
pub(in crate::engine) fn validate_account_in(
    tx: &Connection,
    actor: &Actor,
    user: &Value,
    company: &str,
) -> Result<()> {
    let id = records::positive(user, "id", "账号")?;
    let settings = settings::load(tx, &company)?;
    let rules = referenced_rules(tx, &company, &settings)?;
    let executive = match super::super::personnel_accounts::person_for_account(tx, id)? {
        Some(person) => tx
            .find_identity("companies", &store::normalize(&company))?
            .map(|c| {
                super::super::organization::leads(
                    &c,
                    person["id"].as_i64().unwrap_or(0),
                    "executiveEmployeeId",
                )
            })
            .transpose()?
            .unwrap_or(false),
        None => false,
    };
    let current = || {
        super::super::units::approval_actor(tx, id, actor.edition, company).map_err(|e| {
            if matches!(e.status, Some(403 | 404)) {
                conflict("账号仍承担审批或公司领导审批负责人职责，请先移交、更新规则后再调整权限。")
            } else {
                e
            }
        })
    };
    if executive && !auth::is_company_approver(&current()?) {
        return Err(conflict(
            "权限调整会使公司领导审批负责人失去公司审批职责，请先交接。",
        ));
    }
    if let Some(person) = super::super::personnel_accounts::person_for_account(tx, id)? {
        if let Some(row) = tx.find_identity("companies", &store::normalize(company))? {
            for leader in super::super::organization::leaders(&row)?
                .iter()
                .filter(|l| person["id"] == l.employee_id)
            {
                let mut scope = actor.clone();
                scope.company = company.into();
                for kind in &leader.workflow_kinds {
                    approval::reviewer(tx, &scope, id, &json!({"kind":kind})).map_err(|e| {
                        if matches!(e.status, Some(400 | 403 | 404)) {
                            conflict("账号仍承担公司分管审批职责，请先交接对应业务再调整权限。")
                        } else {
                            e
                        }
                    })?;
                }
            }
        }
    }
    for rule in &rules {
        let responsible = rule.account_ids().contains(&id);
        if responsible || rule.notify_user_ids.contains(&id) {
            let resource =
                approval::resource(&rule.kind).ok_or_else(|| invalid("未知审批类型。"))?;
            let reviewer = super::super::units::for_operation(
                tx,
                id,
                actor.edition,
                company,
                resource,
                if responsible { "approve" } else { "view" },
            )
            .map_err(|e| {
                if matches!(e.status, Some(400 | 403 | 404)) {
                    conflict("账号仍被流程引用，请先交接职责或停用流程。")
                } else {
                    e
                }
            })?;
            if reviewer.company != company
                || auth::authorize(&reviewer, resource, "view").is_err()
                || (responsible && auth::authorize(&reviewer, resource, "approve").is_err())
            {
                return Err(conflict(
                    "账号仍被流程草稿或启用版本引用，请先更新并发布流程或停用版本。",
                ));
            }
        }
    }
    for kind in WORKFLOWS {
        let mut offset = 0;
        loop {
            crate::operation::check()?;
            let (total, rows) = tx.query_records(&RecordQuery {
                kind,
                company: &company,
                statuses: &["Pending", "Suspended"],
                visible_user: Some(id),
                offset,
                limit: 100,
                ..Default::default()
            })?;
            for row in rows {
                let Some(plan) = approval::plan(&row)? else {
                    continue;
                };
                for step in plan
                    .steps
                    .iter()
                    .filter(|s| s.approver_user_id == id && s.status == "Waiting")
                {
                    let resource =
                        approval::resource(kind).ok_or_else(|| invalid("未知审批类型。"))?;
                    let reviewer =
                        super::super::units::resolve(tx, id, actor.edition, &step.approver_context)
                            .map_err(|e| {
                                if matches!(e.status, Some(400 | 401 | 403 | 404)) {
                                    conflict("此单位身份仍承担在途审批，请先交接任务。")
                                } else {
                                    e
                                }
                            })?;
                    if reviewer.company != company
                        || auth::authorize(&reviewer, resource, "view").is_err()
                        || auth::authorize(&reviewer, resource, "approve").is_err()
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
    super::approval_integrity::company(tx, actor, company)
}

pub(in crate::engine) fn assigned(
    tx: &Connection,
    company: &str,
    user: i64,
    pending: bool,
) -> Result<i64> {
    let mut total = 0;
    for kind in WORKFLOWS {
        let mut offset = 0;
        loop {
            crate::operation::check()?;
            let (count, rows) = tx.query_records(&RecordQuery {
                kind,
                company,
                statuses: if pending {
                    &["Pending", "Suspended"]
                } else {
                    &[]
                },
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
    let referenced =
        if kind == "companies" {
            settings["id"].as_i64().is_some()
        } else {
            let user = records::positive(row, "id", "账号")?;
            let rules = referenced_rules(tx, &company, &settings)?;
            rules.iter().any(|rule| {
                rule.account_ids().contains(&user) || rule.notify_user_ids.contains(&user)
            }) || settings["delegations"].as_array().is_some_and(|items| {
                items
                    .iter()
                    .any(|d| d["principalUserId"] == user || d["delegateUserId"] == user)
            }) || handling::services(tx, &company)?.iter().any(|s| {
                s["handlerUserIds"]
                    .as_array()
                    .is_some_and(|ids| ids.contains(&json!(user)))
            }) || assigned(tx, &company, user, false)? > 0
        };
    if referenced {
        return Err(conflict(
            "账号或公司仍有审批规则、代理或审批历史引用，不能删除。",
        ));
    }
    Ok(())
}
