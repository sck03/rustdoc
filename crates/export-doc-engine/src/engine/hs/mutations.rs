//! Versioned catalog mutations; annual reconciliation and retention stay separate.
use super::*;
use crate::engine::{error::unavailable, records::required};

pub(super) fn preview_obsolete(tx: &Connection) -> Result<Value> {
    use export_doc_storage::{DataScope, GenericQuery};
    let (total, items) = tx.query_generic(&GenericQuery {
        kind: CODES,
        scope: DataScope {
            all: true,
            ..Default::default()
        },
        filters: &[("status", "SuspectedObsolete".into())],
        keyword: "",
        search_root: None,
        fields: &[
            ("id", "id"),
            ("code", "code"),
            ("name", "name"),
            ("status", "status"),
            ("rowVersion", "rowVersion"),
            ("updateTime", "updateTime"),
        ],
        offset: 0,
        limit: MAX_BATCH as i64,
    })?;
    for item in &items {
        if item["id"].as_i64().is_none_or(|id| id <= 0) || store::expected(item) <= 0 {
            return Err(unavailable("疑似作废编码的编号或版本损坏。"));
        }
    }
    Ok(
        json!({"items":items,"totalCount":total,"maxBatchCount":MAX_BATCH,
        "policy":"仅列出疑似作废编码，每批最多 5000 条。请按官方税则核实后选择，可逐条取消选择；确认作废须填写原因并复核每条版本，确认后保留至少 12 个日历月。"}),
    )
}

fn versions(value: &Value) -> Result<BTreeMap<i64, i64>> {
    let rows = value
        .as_array()
        .filter(|rows| !rows.is_empty() && rows.len() <= MAX_BATCH)
        .ok_or_else(|| invalid("请选择 1–5000 条编码并提供读取时的版本。"))?;
    let mut versions = BTreeMap::new();
    for row in rows {
        let id = row["id"]
            .as_i64()
            .filter(|id| *id > 0)
            .ok_or_else(|| invalid("编码记录编号无效。"))?;
        let expected = store::expected(row);
        if expected <= 0 || versions.insert(id, expected).is_some() {
            return Err(invalid("编码版本缺失、无效或记录重复，请重新读取。"));
        }
    }
    Ok(versions)
}

pub(super) fn delete_codes(
    tx: &Connection,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let expected = if operation == DELETE_HS_CODE {
        versions(&json!([{"id":id(parameters)?,"rowVersion":read(query,"expectedRowVersion")}]))?
    } else {
        let selected = ids(body)?;
        let expected = versions(&body["expectedVersions"])?;
        if selected != expected.keys().copied().collect() {
            return Err(invalid("选中的编码与版本清单不一致，请重新选择。"));
        }
        expected
    };
    for (id, version) in &expected {
        operation::check()?;
        store::check_version(&store::get(tx, CODES, *id)?, *version)?;
    }
    let count = delete(tx, actor, CODES, &expected.keys().copied().collect())?;
    Ok(json!({"success":true,"message":format!("已删除 {count} 个编码。"),"deletedCount":count}))
}

pub(super) fn confirm_obsolete(
    service: &NativeService,
    tx: &Connection,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    required(body, "reason", "确认作废原因", 1000)?;
    let expected = versions(&body["items"])?;
    let now = service
        .clock
        .now()
        .map_err(unavailable)?
        .utc_now
        .to_rfc3339();
    let mut count = 0;
    for (id, version) in expected {
        operation::check()?;
        let mut row = store::get(tx, CODES, id)?;
        store::check_version(&row, version)?;
        if row["status"] == "Obsolete" {
            continue;
        }
        if !matches!(
            row["status"].as_str(),
            Some("Active" | "ReferenceOnly" | "SuspectedObsolete")
        ) {
            return Err(unavailable("编码有效性状态损坏。"));
        }
        row["status"] = json!("Obsolete");
        row["obsoleteReason"] = json!(text(body, "reason"));
        row["obsoletedAt"] = json!(now);
        row["updateTime"] = json!(now);
        let code = text(&row, "code");
        store::save(tx, CODES, id, row, Some(code), actor, "confirm-obsolete")?;
        // The reason is part of the versioned record and its ordinary audit diff.
        count += 1;
    }
    Ok(
        json!({"success":true,"message":format!("已确认 {count} 个编码作废；最后更新满 12 个日历月后可预检清理。历史业务数据保留。" )}),
    )
}
