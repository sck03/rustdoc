use super::*;
use crate::engine::{error::unavailable, import_previews};

pub(super) fn handle(
    service: &NativeService,
    tx: &Connection,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    let now = service.clock.now().map_err(unavailable)?.utc_now;
    if operation == PREVIEW_HS_CODE_CLEANUP {
        let rows = codes(tx)?;
        let total = rows.len();
        let items = rows
            .values()
            .filter(|row| hs::cleanup_eligible(row, now))
            .take(5000)
            .map(serde_json::to_value)
            .collect::<std::result::Result<Vec<_>, _>>()?;
        let token = import_previews::save(tx, actor, &service.clock, "hs-cleanup", &items)?;
        return Ok(
            json!({"token":token,"eligibleCount":items.len(),"retainedCount":total-items.len(),"items":items,
            "policy":"仅清理已确认失效且最后更新已满一年的编码，每批最多 5000 条；疑似失效、年度待复核和日期不明的编码保留。历史单据、商品、申报实例及替代关系保持原记录。预检 30 分钟内有效。"}),
        );
    }
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
