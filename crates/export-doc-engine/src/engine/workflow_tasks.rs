//! One task projection for all approval-bearing records; no SQL or alternate state machine.
use super::{
    error::{Result, unavailable},
    records::text,
};
use crate::engine::store::Connection;
use export_doc_domain::approval::ApprovalPlan;
use export_doc_storage::WorkflowTask;
use serde_json::Value;

pub(super) fn persist(tx: &Connection, kind: &str, row: &Value) -> Result<()> {
    if !matches!(
        kind,
        "oa-leave"
            | "oa-overtime"
            | "oa-expense"
            | "oa-travel"
            | "oa-purchase"
            | "oa-general"
            | "oa-personnel"
            | "bookings"
            | "supply-requests"
    ) {
        return Ok(());
    }
    let Some(value) = row.get("approvalPlan").filter(|p| p.is_object()) else {
        return Ok(());
    };
    let plan: ApprovalPlan = serde_json::from_value(value.clone())?;
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(unavailable)?;
    let current = plan.current_indices();
    let tasks: Vec<_> = plan
        .steps
        .iter()
        .enumerate()
        .map(|(i, step)| {
            let node = &plan.nodes[step.stage];
            let elapsed_seconds =
                if matches!(step.status.as_str(), "Approved" | "Rejected" | "Returned") {
                    let start = chrono::DateTime::parse_from_rfc3339(&node.started_at)
                        .map_err(|_| unavailable("审批节点开始时间无效。"))?;
                    let end = chrono::DateTime::parse_from_rfc3339(&step.acted_at)
                        .map_err(|_| unavailable("审批任务签署时间无效。"))?;
                    if end < start {
                        return Err(unavailable("审批任务签署时间早于节点开始时间。"));
                    }
                    Some((end - start).num_seconds())
                } else {
                    None
                };
            Ok(WorkflowTask {
                key: step.task_key.clone(),
                node: plan.nodes[step.stage].key.clone(),
                assignee: step.approver_user_id,
                context: step.approver_context.clone(),
                acted_by: step.acted_by_user_id,
                acted_by_context: step.acted_by_context.clone(),
                status: step.status.clone(),
                active: text(row, "status") == "Pending" && current.contains(&i),
                independent: plan.nodes[step.stage].independent_review,
                due_at: plan.nodes[step.stage].due_at.clone(),
                started_at: node.started_at.clone(),
                acted_at: step.acted_at.clone(),
                elapsed_seconds,
            })
        })
        .collect::<Result<_>>()?;
    let submission = row["submissionNumber"]
        .as_i64()
        .filter(|n| *n > 0)
        .ok_or_else(|| unavailable("流程提交版本缺失。"))?;
    tx.replace_workflow_tasks(row["id"].as_i64().unwrap_or(0), submission, &tasks)?;
    Ok(())
}
