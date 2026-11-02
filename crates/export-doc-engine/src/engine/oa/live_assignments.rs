//! Activation preserves the frozen candidate set; unavailable duties require explicit handover.
use super::*;
use export_doc_domain::{
    approval::ApprovalPlan,
    approval_graph::{ActorBinding, NodeKind, NodeState},
};
pub(super) fn check(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    plan: &mut ApprovalPlan,
) -> Result<()> {
    for index in 0..plan.nodes.len() {
        let node = &plan.nodes[index];
        if node.state != NodeState::Active
            || !matches!(node.kind, NodeKind::Approval | NodeKind::Handling)
        {
            continue;
        }
        let action = super::actor_binding::node_action(node.phase, node.kind, node.handling_action);
        let candidates = if node.kind == NodeKind::Approval {
            plan.steps
                .iter()
                .filter(|s| s.stage == index && s.status == "Waiting")
                .map(|s| ActorBinding {
                    user_id: s.approver_user_id,
                    person_id: s.approver_person_id,
                    name: s.approver_name.clone(),
                    context: s.approver_context.clone(),
                    administration: false,
                    origin: s.origin.clone(),
                })
                .collect::<Vec<_>>()
        } else {
            node.actors.clone()
        };
        let mut available = 0;
        for binding in &candidates {
            let result =
                super::super::units::resolve(tx, binding.user_id, actor.edition, &binding.context)
                    .and_then(|candidate| {
                        if !super::fulfillment::binding_available(tx, &candidate, row, binding)? {
                            return Ok(false);
                        }
                        let resource = approval::resource(&text(row, "kind"))
                            .ok_or_else(|| invalid("申请类型无效。"))?;
                        auth::authorize(&candidate, resource, "view")?;
                        auth::authorize(&candidate, resource, action)?;
                        if !auth::restriction_allows(&candidate, resource, action, row, true) {
                            return Ok(false);
                        }
                        if !candidate.admin {
                            approval::qualified_person_for(
                                tx,
                                &candidate,
                                row,
                                action,
                                node.kind == NodeKind::Approval,
                            )?;
                        }
                        Ok(true)
                    });
            match result {
                Ok(true) => available += 1,
                Ok(false) => {}
                Err(cause) if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) => {}
                Err(cause) => return Err(cause),
            }
        }
        if available == 0 || node.kind == NodeKind::Approval && available != candidates.len() {
            plan.nodes[index].state = NodeState::Blocked;
            plan.nodes[index].error_code = "AssignmentUnavailable".into();
        }
    }
    plan.refresh_subflows().map_err(invalid)
}
