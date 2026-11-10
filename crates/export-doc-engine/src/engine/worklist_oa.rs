//! One actionable queue per OA type; approval and fulfilment share their access checks.
use super::*;
use crate::engine::{
    handling,
    oa::{self, approval},
};

pub(super) fn append_groups(
    tx: &Connection,
    actor: &Actor,
    selection: &Selection<'_>,
    groups: &mut Vec<Group>,
) -> Result<()> {
    let workflow_date = crate::engine::units::today(tx)?;
    for (kind, name) in [
        ("oa-personnel", "人员变动审批"),
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
        let mut group = Group::new(kind, name);
        if !selection.needs(&[kind], groups) {
            groups.push(group);
            continue;
        }
        let principals = approval::queue_approvers(tx, actor, resource)?;
        let keys = if kind == "oa-general" {
            handling::keys(tx, actor, "office.general")?
        } else {
            vec![]
        };
        let rank = auth::scope_rank(actor, resource, "view")
            .max(auth::scope_rank(actor, resource, "complete"));
        let mut cursor = None;
        loop {
            crate::operation::check()?;
            let (next, rows) = tx.scan_records(
                &export_doc_storage::RecordQuery {
                    kind,
                    company: &actor.company,
                    statuses: &["Draft", "Returned", "Pending", "Approved"],
                    departments: (rank == 2 && !actor.oa_assigned_only)
                        .then_some(actor.departments.as_slice()),
                    owner: (rank == 1 && !actor.oa_assigned_only).then_some(actor.id),
                    visible_user: Some(actor.id),
                    visible_principals: &principals,
                    node_access: auth::node_access(actor, resource),
                    workflow_date: Some(workflow_date),
                    visible_context: Some(&actor.context),
                    include_participants: !actor.oa_assigned_only,
                    handling_keys: (kind == "oa-general").then_some(keys.as_slice()),
                    handling_states: handling::GENERAL_STATES,
                    limit: 100,
                    ..Default::default()
                },
                cursor,
            )?;
            let count = rows.len() as i64;
            for row in rows {
                if !oa::viewable(tx, actor, resource, &row)? {
                    continue;
                }
                let own = row["ownerUserId"] == actor.id;
                let status = text(&row, "status");
                let complete = if kind == "oa-general" {
                    handling::can_handle(tx, actor, &row, "office.general")?
                } else {
                    auth::visible(actor, resource, "complete", &row)
                };
                let label = match status.as_str() {
                    "Draft" | "Returned" if own && auth::visible(actor, resource, "edit", &row) => {
                        "待修改／提交"
                    }
                    "Pending" if approval::authority(tx, actor, &row)?.is_some() => "待我审批",
                    "Pending" if own => "等待审批，可催办或撤回",
                    "Approved" if complete => "待办理／验收／归档",
                    "Approved" if own => "已批准，等待办理／验收／归档",
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
                let instant = if status == "Pending" {
                    let plan = approval::plan(&row)?;
                    json!(plan.as_ref().and_then(|p| {
                        p.current_indices()
                            .iter()
                            .map(|i| p.nodes[p.steps[*i].stage].due_at.clone())
                            .filter(|d| !d.is_empty())
                            .min()
                    }))
                } else if status == "Approved" && kind == "oa-overtime" {
                    row["overtime"]["endsAt"].clone()
                } else {
                    Value::Null
                };
                group.push(
                    item(
                        &row,
                        text(&row, "title"),
                        format!("{} · {label}", text(&row, "employeeName")),
                        Value::Null,
                        date,
                        instant,
                    ),
                    selection,
                )?;
            }
            cursor = next;
            if count < 100 {
                break;
            }
        }
        groups.push(group);
    }
    Ok(())
}
