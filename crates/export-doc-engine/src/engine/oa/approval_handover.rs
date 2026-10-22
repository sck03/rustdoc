//! Explicit reassignment changes only waiting signatures; prior event snapshots remain immutable.
use super::*;

pub(super) fn reassign(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    body: &Value,
) -> Result<()> {
    auth::authorize(actor, "system.users", "manage")?;
    required(body, "note", "审批移交原因", 500)?;
    if row["status"] != "Pending" {
        return Err(conflict("只有待审批申请可以移交未完成步骤。"));
    }
    let from = records::positive(body, "fromUserId", "原审批人")?;
    let to = records::positive(body, "toUserId", "接任审批人")?;
    let mut plan = approval::plan(row)?.ok_or_else(|| conflict("申请没有指定审批步骤。"))?;
    if from == to
        || plan
            .steps
            .iter()
            .any(|s| s.approver_user_id == to || s.acted_by_user_id == Some(to))
        || row["ownerUserId"] == to
        || row["applicantAccountId"] == to
    {
        return Err(invalid("接任人不能是申请人、现有审批人或已经代批的人员。"));
    }
    let reviewer = approval::reviewer(tx, actor, to, row)?;
    let step = plan
        .steps
        .iter_mut()
        .find(|s| s.approver_user_id == from && s.status == "Waiting")
        .ok_or_else(|| conflict("原审批人没有可移交的未完成步骤。"))?;
    step.approver_user_id = to;
    step.approver_name = reviewer.name;
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["currentApproverId"] = json!(
        plan.current()
            .map(|i| plan.steps[i].approver_user_id)
            .unwrap_or(0)
    );
    row["approvalPlan"] = serde_json::to_value(plan)?;
    Ok(())
}
