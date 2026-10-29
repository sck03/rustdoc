//! One task projection for all approval-bearing records; no SQL or alternate state machine.
use super::{
    error::{Result, unavailable},
    records::text,
};
use export_doc_domain::approval::ApprovalPlan;
use export_doc_storage::{Connection, WorkflowTask};
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
        .map(|(i, step)| WorkflowTask {
            key: step.task_key.clone(),
            node: plan.nodes[step.stage].key.clone(),
            assignee: step.approver_user_id,
            acted_by: step.acted_by_user_id,
            status: step.status.clone(),
            active: text(row, "status") == "Pending" && current.contains(&i),
            due_at: plan.nodes[step.stage].due_at.clone(),
        })
        .collect();
    let submission = row["submissionNumber"]
        .as_i64()
        .filter(|n| *n > 0)
        .ok_or_else(|| unavailable("流程提交版本缺失。"))?;
    tx.replace_workflow_tasks(row["id"].as_i64().unwrap_or(0), submission, &tasks)?;
    Ok(())
}
