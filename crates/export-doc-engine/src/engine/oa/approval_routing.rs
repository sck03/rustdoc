//! Resolve the published graph and freeze explicit task assignments on submission.
use super::*;
use export_doc_domain::{
    approval::{ApprovalNode, ApprovalPlan, ApprovalStep},
    approval_graph::NodeKind,
    approval_policy::ConditionContext,
};
use rust_decimal::Decimal;
use std::collections::HashSet;
use std::str::FromStr;

pub(super) fn submit(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    let kind = text(row, "kind");
    let variant = if kind == "personnel" {
        text(&row["personnelChange"], "action")
    } else {
        String::new()
    };
    let (rule, version) = super::definitions::published(tx, &actor.company, &kind, &variant)?;
    let hiring = kind == "personnel" && variant == "Hire";
    let employee = if kind == "personnel" && matches!(variant.as_str(), "Hire" | "Rehire") {
        super::personnel_changes::prepare(tx, actor, row, super::super::units::today(tx)?, false)?
    } else {
        store::get(
            tx,
            "people",
            records::positive(row, "employeeId", "申请人员")?,
        )?
    };
    let employee_user = if hiring {
        None
    } else {
        employee["account"]["id"].as_i64()
    };
    let appointment = super::request_identity::appointment(tx, actor, &employee, row)?;
    row["applicantAppointmentId"] = json!(appointment.id);
    row["applicantJobTitle"] = json!(appointment.job_title);
    row["applicantAccountId"] = json!(employee_user);
    let duration = match kind.as_str() {
        "leave" => {
            let (a, b) = export_doc_domain::oa::leave_span(&row["leave"]).map_err(invalid)?;
            Some(Decimal::from(b - a) / Decimal::from(2))
        }
        "overtime" => {
            let (a, b) = export_doc_domain::oa::overtime_span(&row["overtime"]).map_err(invalid)?;
            Some(Decimal::from((b - a).num_minutes()) / Decimal::from(60))
        }
        "travel" => {
            let a =
                export_doc_domain::oa::date(&text(&row["travel"], "startsOn")).map_err(invalid)?;
            let b =
                export_doc_domain::oa::date(&text(&row["travel"], "endsOn")).map_err(invalid)?;
            Some(Decimal::from((b - a).num_days() + 1))
        }
        _ => None,
    };
    let context = ConditionContext {
        amount: if text(row, "totalAmount").is_empty() {
            None
        } else {
            Some(
                Decimal::from_str(&text(row, "totalAmount"))
                    .map_err(|_| invalid("申请金额无效。"))?,
            )
        },
        currency: text(row, "currency"),
        duration,
        department: text(row, "departmentId"),
        category: if kind == "leave" {
            text(&row["leave"], "category")
        } else {
            text(row, "category")
        },
        personnel_action: variant,
        returnable: row["isReturnable"]
            .as_bool()
            .map(|value| value.to_string())
            .unwrap_or_default(),
    };
    build_plan(tx, actor, row, &employee, &rule, version, &context)
}

pub(super) fn build_plan(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    employee: &Value,
    rule: &export_doc_domain::approval_policy::ApprovalRule,
    version: i64,
    context: &ConditionContext,
) -> Result<()> {
    let mut compiled = super::subflows::compile(tx, &actor.company, rule)?;
    super::request_projects::bind(tx, actor, row, employee)?;
    super::subflows::map_contexts(&mut compiled, row, context)?;
    let rule = &compiled.rule;
    let employee_user = row["applicantAccountId"].as_i64();
    let mut plan = ApprovalPlan {
        participants: vec![],
        subflow_progress: Default::default(),
        subflows: compiled.instances,
        calendars: compiled.calendars,
        generation: 1,
        suspended_at: String::new(),
        paused_seconds: 0,
        definition_key: rule.key.clone(),
        policy_version: version,
        nodes: vec![],
        steps: vec![],
        notify_user_ids: rule.notify_user_ids.clone(),
    };
    let mut previous = HashSet::new();
    let mut previous_people = HashSet::new();
    let excluded = rule
        .routed_exclusions_in(context, &compiled.contexts)
        .map_err(invalid)?;
    let mut notes = vec![];
    for (index, node) in rule.stages.iter().enumerate() {
        let context = compiled.contexts.get(&node.key).unwrap_or(context);
        super::materials::validate_policy(&node.material_policy)?;
        let included =
            !excluded.contains(&node.key) && rule.includes(index, &context).map_err(invalid)?;
        let mut routing_row = row.clone();
        routing_row["routingDepartment"] = json!(context.department);
        let mut bindings = if included
            && matches!(
                node.kind,
                NodeKind::Approval | NodeKind::Handling | NodeKind::Copy
            ) {
            super::actor_binding::resolve(tx, actor, &routing_row, employee, node)?
        } else {
            vec![]
        };
        let ids = if included && node.kind == NodeKind::Approval {
            let (ids, routed) = rule
                .route_group(
                    index,
                    bindings.iter().map(|b| b.user_id).collect(),
                    row["ownerUserId"].as_i64().unwrap_or(0),
                    employee_user,
                    &mut previous,
                )
                .map_err(invalid)?;
            notes.extend(routed);
            bindings.retain(|b| ids.contains(&b.user_id));
            for binding in &bindings {
                let person = binding
                    .person_id
                    .ok_or_else(|| invalid("审批任务缺少自然人身份。"))?;
                if node.repeated_approver
                    == export_doc_domain::approval_policy::RepeatedApprover::Reject
                    && previous_people.contains(&person)
                {
                    return Err(invalid("独立复核不能由同一自然人的其他账号或任职承担。"));
                }
                previous_people.insert(person);
            }
            ids
        } else {
            if !included {
                notes.push(format!("{}：条件或排他分支未命中，跳过节点。", node.name));
            }
            vec![]
        };
        plan.nodes.push(ApprovalNode {
            kind: node.kind,
            pair_key: node.pair_key.clone(),
            phase: node.phase,
            actors: if node.kind == NodeKind::Approval {
                vec![]
            } else {
                bindings.clone()
            },
            handling_action: node.handling_action,
            system_action: node.system_action,
            escalations: node.escalations.clone(),
            escalation_targets: super::actor_binding::escalation_targets(
                tx,
                actor,
                &routing_row,
                employee,
                node,
                &bindings,
            )?,
            allow_administration: node.allow_administration,
            material_policy: node.material_policy.clone(),
            deadline_mode: node.deadline_mode,
            calendar_key: if node.runtime_calendar_key.is_empty() {
                "main".into()
            } else {
                node.runtime_calendar_key.clone()
            },
            key: node.key.clone(),
            name: node.name.clone(),
            review_points: node.review_points.clone(),
            depends_on: node.depends_on.clone(),
            decision: node.decision,
            voting: node.voting.clone(),
            due_hours: node.due_hours,
            independent_review: node.repeated_approver
                == export_doc_domain::approval_policy::RepeatedApprover::Reject,
            skipped: !included,
            started_at: String::new(),
            due_at: String::new(),
            revision_policy: node.revision_policy.clone(),
            ..Default::default()
        });
        for id in ids {
            let reviewer = bindings
                .iter()
                .find(|b| b.user_id == id)
                .ok_or_else(|| invalid("审批身份解析结果缺失。"))?;
            plan.steps.push(ApprovalStep {
                mandatory: node.revision_policy.protected_user_ids.contains(&id),
                origin: reviewer.origin.clone(),
                approver_person_id: reviewer.person_id,
                acted_by_person_id: None,
                verification_values: Default::default(),
                confirmed_review_points: vec![],
                task_key: format!("{}:{id}", node.key),
                stage: index,
                decision: node.decision,
                approver_user_id: id,
                approver_name: reviewer.name.clone(),
                approver_context: reviewer.context.clone(),
                status: "Waiting".into(),
                acted_by_user_id: None,
                acted_by_name: String::new(),
                acted_by_context: String::new(),
                acted_at: String::new(),
                note: String::new(),
                delegation_key: String::new(),
                source_generation: None,
            });
        }
    }
    super::business_nodes::validate_plan(rule, &plan, context)?;
    for child in &plan.subflows {
        for output in child.outputs.iter().filter(|o| o.required) {
            if plan
                .nodes
                .iter()
                .find(|n| n.key == output.node_key)
                .is_none_or(|n| n.skipped)
            {
                return Err(invalid("必需子流程核验输出在本样例路线中不可达。"));
            }
        }
    }
    plan.activate(chrono::Utc::now()).map_err(invalid)?;
    for (i, node) in rule.stages.iter().enumerate() {
        if !plan.nodes[i].skipped
            && node.kind == NodeKind::Approval
            && node.revision_policy.protected_user_ids.iter().any(|id| {
                !plan
                    .steps
                    .iter()
                    .any(|s| s.stage == i && s.approver_user_id == *id)
            })
        {
            return Err(invalid(
                "节点缺少已发布的必经责任人，不能通过回避或条件跳过其职责。",
            ));
        }
    }
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    row["approvalStageLabels"] = json!(plan.nodes.iter().map(|n| &n.name).collect::<Vec<_>>());
    row["approvalPlan"] = serde_json::to_value(plan)?;
    row["approvalRoutingNotes"] = json!(notes);
    row["lastRemindedAt"] = json!("");
    Ok(())
}
