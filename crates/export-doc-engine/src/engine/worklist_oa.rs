//! One actionable queue per OA type; approval and fulfilment share their access checks.
use super::*;
use crate::engine::oa::{self, approval, handling};

pub(super) fn groups(tx: &Connection, actor: &Actor) -> Result<Vec<Group>> {
    let mut groups = vec![];
    for (kind, name) in [
        ("oa-leave", "请假审批与销假"),
        ("oa-overtime", "加班审批与确认"),
        ("oa-expense", "报销审批与财务接收"),
        ("oa-travel", "出差审批与归档"),
        ("oa-purchase", "采购审批与验收"),
        ("oa-general", "通用申请审批与办理"),
    ] {
        let resource = approval::resource(kind).ok_or_else(|| unavailable("申请类型无效。"))?;
        if !allowed(actor, resource, &["view"]) {
            continue;
        }
        let mut items = vec![];
        for row in store::all(tx, kind)? {
            if !oa::viewable(tx, actor, resource, &row)? {
                continue;
            }
            let own = row["ownerUserId"] == actor.id;
            let status = text(&row, "status");
            let complete = if kind == "oa-general" {
                handling::can_handle(tx, actor, &row, false)?
            } else {
                auth::visible(actor, resource, "complete", &row)
            };
            let label = match status.as_str() {
                "Draft" | "Rejected" if own && auth::visible(actor, resource, "edit", &row) => {
                    "待修改／提交"
                }
                "Pending" if approval::authority(tx, actor, &row)?.is_some() => "待我审批",
                "Pending" if own => "等待审批，可催办或撤回",
                "Approved" if complete => "待办理／验收／归档",
                _ => continue,
            };
            let date = if status == "Approved" {
                match kind {
                    "oa-leave" => row["leave"]["endsOn"].clone(),
                    "oa-travel" => row["travel"]["endsOn"].clone(),
                    _ => Value::Null,
                }
            } else {
                Value::Null
            };
            let instant = if status == "Approved" && kind == "oa-overtime" {
                row["overtime"]["endsAt"].clone()
            } else {
                Value::Null
            };
            items.push(item(
                &row,
                text(&row, "title"),
                format!("{} · {label}", text(&row, "employeeName")),
                Value::Null,
                date,
                instant,
            ));
        }
        groups.push(Group {
            key: kind,
            name,
            rows: items,
        });
    }
    Ok(groups)
}
