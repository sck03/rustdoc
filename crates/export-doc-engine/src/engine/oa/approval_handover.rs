//! Explicit administrator handover changes one unsigned task and preserves prior event snapshots.
use super::*;
pub(super) fn reassign(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    body: &Value,
) -> Result<()> {
    auth::authorize(actor, "system.users", "manage")?;
    required(body, "note", "审批移交原因", 500)?;
    required(body, "taskKey", "审批任务", 180)?;
    if row["status"] != "Pending" {
        return Err(conflict("只有在途申请可以移交未签署任务。"));
    }
    let from = records::positive(body, "fromUserId", "原审批人")?;
    let to = records::positive(body, "toUserId", "接任人")?;
    let mut plan = approval::plan(row)?.ok_or_else(|| conflict("缺少审批流程。"))?;
    let index = plan
        .steps
        .iter()
        .position(|s| {
            s.task_key == text(body, "taskKey")
                && s.approver_user_id == from
                && s.status == "Waiting"
        })
        .ok_or_else(|| conflict("任务已变化或已经签署。"))?;
    let node = plan.steps[index].stage;
    if from == to
        || row["ownerUserId"] == to
        || row["applicantAccountId"] == to
        || plan.steps.iter().any(|s| {
            s.approver_user_id == to
                && (s.stage == node
                    || plan.nodes[node].independent_review
                    || plan.nodes[s.stage].independent_review)
        })
    {
        return Err(invalid(
            "接任人不能是申请人、同节点审批人或违反独立复核规则的人员。",
        ));
    }
    let reviewer = approval::reviewer(tx, actor, to, row)?;
    plan.steps[index].approver_user_id = to;
    plan.steps[index].approver_name = reviewer.name;
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["approvalPlan"] = serde_json::to_value(plan)?;
    Ok(())
}
