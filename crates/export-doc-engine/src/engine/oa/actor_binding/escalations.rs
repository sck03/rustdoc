//! Frozen escalation recipients are separate from ordinary actor selection.
use super::*;

pub(in crate::engine::oa) fn targets(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    employee: &Value,
    stage: &ApprovalStage,
    primary: &[ActorBinding],
) -> Result<Vec<Vec<ActorBinding>>> {
    use export_doc_domain::approval_graph::EscalationAction;
    let mut result = vec![];
    for escalation in &stage.escalations {
        let Some(target) = &escalation.target else {
            result.push(vec![]);
            continue;
        };
        let mut selector = stage.clone();
        selector.source = target.source;
        selector.source_id = target.source_id.clone();
        selector.approver_user_ids = target.user_ids.clone();
        selector.escalations.clear();
        if escalation.action == EscalationAction::NotifySupervisor {
            selector.kind = NodeKind::Copy;
        }
        let mut bindings = vec![];
        let mut seen = std::collections::HashSet::new();
        let subjects = if matches!(
            target.source,
            ApproverSource::LineManager
                | ApproverSource::FunctionalManagers
                | ApproverSource::DepartmentManager
                | ApproverSource::DepartmentLeaders
        ) {
            primary.to_vec()
        } else {
            vec![]
        };
        if subjects.is_empty() {
            bindings = resolve(tx, actor, row, employee, &selector)?;
        } else {
            for subject in subjects {
                let person = store::get(
                    tx,
                    "people",
                    subject
                        .person_id
                        .ok_or_else(|| invalid("升级缺少原责任人员身份。"))?,
                )?;
                let mut context = row.clone();
                context["applicantAppointmentId"] = json!(subject.origin.appointment_id);
                context["routingDepartment"] = json!(
                    super::super::super::personnel_organization::appointment_by_id(
                        tx,
                        &person,
                        &subject.origin.unit_code,
                        &subject.origin.appointment_id
                    )?
                    .department_id
                );
                for binding in resolve(tx, actor, &context, &person, &selector)? {
                    if seen.insert((binding.person_id, binding.user_id)) {
                        bindings.push(binding);
                    }
                }
            }
        }
        if escalation.action == EscalationAction::Reassign
            && bindings.iter().any(|b| {
                row["ownerUserId"] == b.user_id
                    || row["applicantAccountId"] == b.user_id
                    || b.person_id.is_some_and(|id| row["employeeId"] == id)
            })
        {
            return Err(invalid("升级接任人不能是申请人或代填人。"));
        }
        if bindings.len() > 50 {
            return Err(invalid("每级升级责任人最多五十人。"));
        }
        result.push(bindings);
    }
    Ok(result)
}
