//! Deferred reservations preserve the approval and atomically retry the declared stock use case.
use super::*;
use export_doc_domain::approval_graph::{NodeState, SystemAction};
pub(super) fn tick(service: &NativeService) -> Result<()> {
    let entries = service
        .store
        .read(|tx| Ok(tx.workflow_system_requests("ReserveSupply")?))?;
    for entry in entries {
        crate::operation::check()?;
        let id = records::positive(&entry, "requestId", "领用申请")?;
        service.store.transaction(|tx| {
            let mut row = store::get(tx, "supply-requests", id)?;
            if row["submissionNumber"] != entry["revision"]
                || row["approvalPlan"]["generation"] != entry["generation"]
                || !super::fulfillment::active_system(&row, SystemAction::ReserveSupply)?
            {
                return Ok(());
            }
            let plan = approval::plan(&row)?
                .ok_or_else(|| super::super::error::unavailable("库存流程缺失。"))?;
            let signer = plan
                .steps
                .iter()
                .filter(|s| {
                    plan.nodes[s.stage].phase == export_doc_domain::approval_graph::Phase::Decision
                        && s.status == "Approved"
                })
                .max_by(|a, b| a.acted_at.cmp(&b.acted_at))
                .ok_or_else(|| {
                    super::super::error::unavailable("库存系统动作缺少真实批准签署。")
                })?;
            let actor = super::super::units::resolve(
                tx,
                signer
                    .acted_by_user_id
                    .ok_or_else(|| super::super::error::unavailable("实际批准身份缺失。"))?,
                service.store.edition,
                &signer.acted_by_context,
            );
            let result = actor.and_then(|actor| {
                approval::qualified_person(tx, &actor, &row)?;
                super::super::office_workflows::reserve(
                    tx,
                    &actor,
                    &mut row,
                    super::super::units::today(tx)?,
                )?;
                Ok(())
            });
            let code = match result {
                Ok(()) if row["reservationState"] == "Insufficient" => "StockInsufficient",
                Ok(()) => "",
                Err(cause) if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) => {
                    "ReservationIdentityUnavailable"
                }
                Err(cause) => return Err(cause),
            };
            super::fulfillment::system_result(
                &mut row,
                SystemAction::ReserveSupply,
                if code.is_empty() {
                    NodeState::Completed
                } else {
                    NodeState::Blocked
                },
                code,
            )?;
            // The reservation flag is a business fact and is persisted with the stock change.
            let actor = Actor {
                scope_rules: vec![],
                context: String::new(),
                edition: service.store.edition,
                id: 0,
                name: "库存预留任务".into(),
                company: text(&row, "companyScope"),
                department: text(&row, "departmentId"),
                departments: vec![],
                oa_assigned_only: false,
                admin: false,
                grants: vec![],
            };
            let mut current = approval::plan(&row)?
                .ok_or_else(|| super::super::error::unavailable("库存流程缺失。"))?;
            super::live_assignments::check(tx, &actor, &row, &mut current)?;
            row["approvalPlan"] = serde_json::to_value(current)?;
            store::save_in_scope(
                tx,
                "supply-requests",
                id,
                row,
                None,
                &actor,
                "scheduled-reservation",
                None,
            )?;
            Ok(())
        })?;
    }
    Ok(())
}
