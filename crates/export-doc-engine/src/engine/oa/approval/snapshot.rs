//! Resolve a task set once inside the current request/transaction snapshot.
use super::{ApprovalPlan, plan, resource};
use crate::engine::{
    auth,
    error::Result,
    oa::settings,
    records::text,
    store::{Actor, Connection},
    units,
};
use serde_json::Value;
use std::collections::HashMap;

pub(in crate::engine) struct ReviewSnapshot {
    pub plan: Option<ApprovalPlan>,
    assignments: Vec<(String, i64, String)>,
}
impl ReviewSnapshot {
    pub fn read(tx: &Connection, actor: &Actor, row: &Value) -> Result<Self> {
        let mut snapshot = Self {
            plan: plan(row)?,
            assignments: vec![],
        };
        let Some(plan) = snapshot.plan.as_ref() else {
            return Ok(snapshot);
        };
        let Some(resource) = resource(&text(row, "kind")) else {
            return Ok(snapshot);
        };
        if !matches!(
            row["status"].as_str(),
            Some(
                "Pending"
                    | "Approved"
                    | "Issued"
                    | "InUse"
                    | "Completed"
                    | "Returned"
                    | "HandedOff"
            )
        ) || row["companyScope"] != actor.company
            || row["ownerUserId"] == actor.id
            || row["applicantAccountId"] == actor.id
            || auth::authorize(actor, resource, "view").is_err()
            || !auth::restriction_allows(actor, resource, "view", row, true)
        {
            return Ok(snapshot);
        }
        let signer = match crate::engine::oa::qualification::person_id(tx, actor) {
            Ok(id) => id,
            Err(cause) if cause.status == Some(403) => return Ok(snapshot),
            Err(cause) => return Err(cause),
        };
        if row["employeeId"] == signer {
            return Ok(snapshot);
        }
        let current = plan.current_indices();
        let mut independence = HashMap::new();
        let mut eligible = vec![];
        for index in current {
            let step = &plan.steps[index];
            let node = &plan.nodes[step.stage];
            let action = crate::engine::oa::actor_binding::node_action(
                node.phase,
                node.kind,
                node.handling_action,
            );
            if auth::authorize(actor, resource, action).is_err()
                || !auth::restriction_allows(actor, resource, action, row, true)
            {
                continue;
            }
            let allowed = *independence
                .entry(step.stage)
                .or_insert_with(|| !plan.signer_conflict(step.stage, actor.id, Some(signer)));
            if allowed {
                eligible.push(index);
            }
        }
        let needs_delegations = eligible.iter().any(|index| {
            let step = &plan.steps[*index];
            step.approver_user_id != actor.id || step.approver_context != actor.context
        });
        let delegations = if needs_delegations {
            settings::principals(tx, actor)?
        } else {
            vec![]
        };
        let mut principals: HashMap<(i64, String, String), Option<i64>> = HashMap::new();
        for index in eligible {
            let step = &plan.steps[index];
            let node = &plan.nodes[step.stage];
            let action = crate::engine::oa::actor_binding::node_action(
                node.phase,
                node.kind,
                node.handling_action,
            );
            if !crate::engine::oa::actor_binding::origin_available(
                tx,
                row,
                step.approver_person_id
                    .ok_or_else(|| crate::engine::error::unavailable("任务人员缺失。"))?,
                &step.origin,
            )? {
                continue;
            }
            if step.approver_user_id == actor.id
                && step.approver_context == actor.context
                && step.approver_person_id == Some(signer)
            {
                snapshot
                    .assignments
                    .push((step.task_key.clone(), actor.id, String::new()));
                continue;
            }
            for (principal, delegation) in &delegations {
                if *principal != step.approver_user_id {
                    continue;
                }
                let key = (*principal, step.approver_context.clone(), action.to_owned());
                if !principals.contains_key(&key) {
                    let person =
                        match units::resolve(tx, *principal, actor.edition, &step.approver_context)
                        {
                            Ok(p)
                                if p.company == actor.company
                                    && auth::authorize(&p, resource, "view").is_ok()
                                    && auth::authorize(&p, resource, action).is_ok()
                                    && auth::restriction_allows(
                                        &p, resource, "view", row, true,
                                    )
                                    && auth::restriction_allows(
                                        &p, resource, action, row, true,
                                    ) =>
                            {
                                match super::qualified_person_for(tx, &p, row, action, true) {
                                    Ok(id) => Some(id),
                                    Err(cause) if cause.status == Some(403) => None,
                                    Err(cause) => return Err(cause),
                                }
                            }
                            Ok(_) => None,
                            Err(cause) if matches!(cause.status, Some(400 | 401 | 403 | 404)) => {
                                None
                            }
                            Err(cause) => return Err(cause),
                        };
                    principals.insert(key.clone(), person);
                }
                if principals[&key].is_some() && principals[&key] == step.approver_person_id {
                    snapshot.assignments.push((
                        step.task_key.clone(),
                        *principal,
                        delegation.clone(),
                    ));
                    break;
                }
            }
        }
        Ok(snapshot)
    }
    pub fn task_keys(&self) -> Vec<String> {
        self.assignments
            .iter()
            .map(|(key, _, _)| key.clone())
            .collect()
    }
    pub fn authority(&self, task: Option<&str>) -> Option<(i64, String)> {
        self.assignments
            .iter()
            .find(|(key, _, _)| task.is_none_or(|task| key == task))
            .map(|(_, principal, delegation)| (*principal, delegation.clone()))
    }
}
