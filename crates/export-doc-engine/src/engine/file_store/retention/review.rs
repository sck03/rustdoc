//! A bounded review snapshot binds the reason and exact file versions to its token.
use super::*;

pub(super) fn preview(
    tx: &Connection,
    actor: &Actor,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    let ids = body["ids"]
        .as_array()
        .ok_or_else(|| invalid("请选择待处置档案。"))?;
    let reason = text(body, "reason").trim();
    if ids.is_empty() || ids.len() > 100 || reason.is_empty() {
        return Err(invalid("请选择 1–100 条档案并填写处置理由。"));
    }
    let mut seen = std::collections::BTreeSet::new();
    let mut rows = Vec::with_capacity(ids.len());
    let mut bytes = 0u64;
    for id in ids {
        let id = id.as_str().ok_or_else(|| invalid("档案编号无效。"))?;
        if !seen.insert(id) {
            return Err(invalid("待处置清单包含重复档案，请重新选择。"));
        }
        let row = tx
            .retained_file(id)?
            .ok_or_else(|| invalid("所选档案已不存在，请刷新。"))?;
        let row = project(today, row)?;
        if row["eligible"] != true {
            return Err(invalid(format!(
                "档案“{}”仍被引用、暂停保留、长期保留或未到期。请核对类别、依据日期和保留政策。",
                text(&row, "fileName")
            )));
        }
        bytes = bytes
            .checked_add(row["size"].as_u64().unwrap_or(0))
            .ok_or_else(|| invalid("档案容量超出计数范围。"))?;
        rows.push(row);
    }
    let summary = json!({"token":paths::nonce().map_err(unavailable)?,"count":rows.len(),"bytes":bytes,
        "expiresAt":(chrono::Utc::now()+chrono::Duration::minutes(15)).to_rfc3339(),"requestedBy":actor.id});
    let plan = json!({"state":"Ready","summary":summary,"rows":rows,"reason":reason});
    tx.set_settings(PLAN, 1, &plan)?;
    Ok(public_plan(&plan))
}

pub(super) fn approve(
    tx: &Connection,
    actor: &Actor,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    let mut plan = tx
        .settings(PLAN)?
        .ok_or_else(|| invalid("没有有效待批准处置计划。"))?;
    if plan["summary"]["token"] != body["token"] {
        return Err(conflict("处置计划已变更，请重新预览。"));
    }
    if plan["summary"]["requestedBy"] == actor.id {
        return Err(crate::engine::error::error(
            403,
            "正式档案处置需要另一名系统管理员复核批准，申请人不能自行批准。",
        ));
    }
    if plan["state"] == "Completed" {
        return Ok(
            json!({"message":"该处置计划已完成。","count":plan["summary"]["count"],"bytes":plan["summary"]["bytes"]}),
        );
    }
    let expires = chrono::DateTime::parse_from_rfc3339(text(&plan["summary"], "expiresAt"))
        .map_err(|_| invalid("处置计划期限无效。"))?;
    if expires < chrono::Utc::now() {
        return Err(conflict("处置预检已过期，请重新预览。"));
    }
    let rows = plan["rows"]
        .as_array()
        .filter(|rows| !rows.is_empty() && rows.len() <= 100)
        .ok_or_else(|| invalid("处置计划损坏或超过容量限制。"))?;
    for expected in rows {
        let current = tx
            .retained_file(text(expected, "id"))?
            .ok_or_else(|| conflict("档案已变化，请重新预检。"))?;
        let current = project(today, current)?;
        if &current != expected || current["eligible"] != true {
            return Err(conflict(
                "档案引用、暂停保留或政策已变化，未执行处置，请重新预览。",
            ));
        }
        tx.remove_retained_file(
            text(&current, "id"),
            current["revision"].as_i64().unwrap_or(0),
        )?;
    }
    plan["state"] = json!("Completed");
    plan["approvedBy"] = json!(actor.id);
    tx.set_settings(PLAN, 2, &plan)?;
    Ok(
        json!({"message":"已移除获批的归档引用；物理对象须再经无引用回收。已有恢复点按自身保留期继续保护历史资料。",
        "count":plan["summary"]["count"],"bytes":plan["summary"]["bytes"]}),
    )
}
