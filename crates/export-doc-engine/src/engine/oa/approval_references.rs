use super::*;

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
        settings["rules"].as_array().is_some_and(|rules| {
            rules.iter().any(|r| {
                r["approverUserIds"]
                    .as_array()
                    .is_some_and(|ids| ids.contains(&json!(user)))
            })
        }) || settings["delegations"].as_array().is_some_and(|items| {
            items
                .iter()
                .any(|d| d["principalUserId"] == user || d["delegateUserId"] == user)
        }) || assigned(tx, &company, user, false)? > 0
    };
    if referenced {
        return Err(conflict(
            "账号或公司仍有审批规则、代理或审批历史引用，不能删除。",
        ));
    }
    Ok(())
}
