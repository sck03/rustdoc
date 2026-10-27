use super::*;
use crate::engine::{error::unavailable, import_previews};

pub(super) fn preview(service: &NativeService, actor: &Actor) -> Result<Value> {
    use export_doc_storage::{DataScope, GenericQuery};
    let now = service.clock.now().map_err(unavailable)?.utc_now;
    let (total, items) = service
        .store
        .read_for(actor, PREVIEW_HS_CODE_CLEANUP, &[], |tx, _| {
            let total = tx
                .query_generic(&GenericQuery {
                    kind: CODES,
                    scope: DataScope {
                        all: true,
                        ..Default::default()
                    },
                    filters: &[],
                    keyword: "",
                    search_root: None,
                    fields: &[("id", "id")],
                    offset: 0,
                    limit: 1,
                })?
                .0;
            let fields = [
                ("id", "id"),
                ("status", "status"),
                ("updateTime", "updateTime"),
            ];
            let mut query = crate::engine::hs_queries::query(CODES, &fields);
            let mut items = vec![];
            loop {
                operation::check()?;
                let rows = tx.query_hs(&query)?;
                let count = rows.len() as i64;
                for row in rows {
                    operation::check()?;
                    if !hs::cleanup_eligible_state(
                        row["status"].as_str().unwrap_or(""),
                        row["updateTime"].as_str(),
                        now,
                    ) {
                        continue;
                    }
                    let id = row["id"]
                        .as_i64()
                        .filter(|id| *id > 0)
                        .ok_or_else(|| unavailable("编码记录编号损坏。"))?;
                    let dto: ApiHsCodeDto = serde_json::from_value(contracts::project(
                        contracts::schema("ApiHsCodeDto"),
                        store::get(tx, CODES, id)?,
                    ))?;
                    items.push(serde_json::to_value(dto)?);
                    if items.len() == MAX_BATCH {
                        break;
                    }
                }
                query.offset += count;
                if count < query.limit || items.len() == MAX_BATCH {
                    break;
                }
            }
            Ok((total as usize, items))
        })?;
    let prepared = import_previews::prepare("hs-cleanup", &items)?;
    let token =
        service
            .store
            .transaction_for(actor, PREVIEW_HS_CODE_CLEANUP, &[], |tx, actor| {
                import_previews::save_prepared(tx, actor, &service.clock, &prepared)
            })?;
    let mut value = json!({"token":token,"eligibleCount":items.len(),"retainedCount":total-items.len(),
        "policy":"仅清理已人工确认作废且最后更新满 12 个日历月的编码。未导入新年度资料不会导致编码清理；有效、参考、疑似作废及日期不明的编码保留。每批最多 5000 条，预检 30 分钟内有效，确认后才删除目录记录；历史单据、商品、申报实例和替代关系保留。"});
    value["items"] = Value::Array(items);
    Ok(value)
}

pub(super) fn commit(
    service: &NativeService,
    tx: &Connection,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    let now = service.clock.now().map_err(unavailable)?.utc_now;
    let rows = import_previews::consume(
        tx,
        actor,
        &service.clock,
        "hs-cleanup",
        &text(body, "token"),
    )?;
    let mut ids = BTreeSet::new();
    for row in rows {
        operation::check()?;
        let id = row["id"]
            .as_i64()
            .ok_or_else(|| unavailable("清理预检缺少记录编号。"))?;
        let current = store::get(tx, CODES, id)?;
        let dto: ApiHsCodeDto = serde_json::from_value(contracts::project(
            contracts::schema("ApiHsCodeDto"),
            current.clone(),
        ))?;
        if store::expected(&current) != store::expected(&row) || !hs::cleanup_eligible(&dto, now) {
            return Err(conflict("编码在预检后已变化，请重新预检。"));
        }
        ids.insert(id);
    }
    let count = delete(tx, actor, CODES, &ids)?;
    Ok(
        json!({"success":true,"message":format!("已清理 {count} 个失效满一年的编码；历史单据和归类经验保持不变。" )}),
    )
}
