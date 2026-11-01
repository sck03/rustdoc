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
        if row["status"] != "Pending"
            || row["companyScope"] != actor.company
            || row["ownerUserId"] == actor.id
            || row["applicantAccountId"] == actor.id
            || auth::authorize(actor, resource, "view").is_err()
            || auth::authorize(actor, resource, "approve").is_err()
            || !auth::restriction_allows(actor, resource, "view", row, true)
            || !auth::restriction_allows(actor, resource, "approve", row, true)
        {
            return Ok(snapshot);
        }
        let signer = match super::qualified_person(tx, actor, row) {
            Ok(id) => id,
            Err(cause) if cause.status == Some(403) => return Ok(snapshot),
            Err(cause) => return Err(cause),
        };
        let current = plan.current_indices();
        let mut independence = HashMap::new();
        let mut eligible = vec![];
        for index in current {
            let step = &plan.steps[index];
            let allowed = *independence.entry(step.stage).or_insert_with(|| {
                !plan.independent_signer_conflict(step.stage, actor.id)
                    && !plan.steps.iter().any(|other| {
                        other.stage != step.stage
                            && (plan.nodes[step.stage].independent_review
                                || plan.nodes[other.stage].independent_review)
                            && other.acted_by_user_id.is_some()
                            && (other.acted_by_person_id == Some(signer)
                                || other.approver_person_id == Some(signer))
                    })
            });
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
        let mut principals: HashMap<(i64, String), Option<i64>> = HashMap::new();
        for index in eligible {
            let step = &plan.steps[index];
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
                let key = (*principal, step.approver_context.clone());
                if !principals.contains_key(&key) {
                    let person =
                        match units::resolve(tx, *principal, actor.edition, &step.approver_context)
                        {
                            Ok(p)
                                if p.company == actor.company
                                    && auth::authorize(&p, resource, "view").is_ok()
                                    && auth::authorize(&p, resource, "approve").is_ok()
                                    && auth::restriction_allows(
                                        &p, resource, "view", row, true,
                                    )
                                    && auth::restriction_allows(
                                        &p, resource, "approve", row, true,
                                    ) =>
                            {
                                match super::qualified_person(tx, &p, row) {
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
