//! Publish the validated domain transition once; subsequent reads are relational.
use super::{
    error::{Result, unavailable},
    records::text,
    store::Connection,
};
use export_doc_domain::approval::ApprovalPlan;
use serde_json::Value;

pub(super) fn persist(tx: &Connection, kind: &str, row: &Value) -> Result<()> {
    if super::oa::approval::resource(kind).is_none() {
        return Ok(());
    }
    let Some(value) = row.get("approvalPlan").filter(|p| p.is_object()) else {
        return Ok(());
    };
    let plan: ApprovalPlan = serde_json::from_value(value.clone())?;
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(unavailable)?;
    let active = plan
        .current_indices()
        .into_iter()
        .map(|i| plan.steps[i].task_key.clone())
        .collect::<Vec<_>>();
    let revision = row["submissionNumber"]
        .as_i64()
        .filter(|n| *n > 0)
        .ok_or_else(|| unavailable("流程提交版本缺失。"))?;
    let state = text(row, "status");
    let handling = match state.as_str() {
        "Completed" | "HandedOff" => Some(("Completed", state.as_str())),
        "Returned" if kind == "supply-requests" => Some(("Completed", "Returned")),
        "Issued" | "InUse" => Some(("InProgress", state.as_str())),
        "Approved" if row["reservationState"] == "Insufficient" => Some(("Blocked", "Approved")),
        "Approved" => Some(("Pending", "Approved")),
        _ => None,
    };
    let approval = if handling.is_some() {
        "Approved"
    } else {
        match state.as_str() {
            "Suspended" => plan.outcome(),
            "Draft" => "Withdrawn",
            "Cancelled" if plan.outcome() == "Approved" => "Voided",
            state => state,
        }
    };
    let progress = export_doc_storage::WorkflowProgress { approval, handling };
    if (approval == "Pending"
        && (plan.outcome() != "Pending"
            || active.is_empty()
                && plan.suspended_at.is_empty()
                && !plan
                    .nodes
                    .iter()
                    .any(|n| n.state == export_doc_domain::approval_graph::NodeState::Blocked)))
        || (approval == "Approved" && plan.outcome() != "Approved")
        || (approval == "Rejected" && plan.outcome() != "Rejected")
    {
        return Err(unavailable("申请状态与审批结果不一致，未发布本次变更。"));
    }
    tx.save_workflow_state(
        row["id"].as_i64().unwrap_or(0),
        revision,
        &progress,
        value,
        &active,
    )?;
    Ok(())
}
