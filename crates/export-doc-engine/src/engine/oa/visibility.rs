//! A row-bound permission evaluation shares its approval snapshot within one transaction.
use super::{FINANCE_STATUSES, approval, auth, fulfillment, handling, text};
use crate::engine::{
    error::Result,
    personnel_accounts,
    store::{Actor, Connection},
};
use serde_json::Value;

pub(in crate::engine) struct Visibility<'a> {
    tx: &'a Connection<'a>,
    actor: &'a Actor,
    resource: &'a str,
    row: &'a Value,
    review: Option<approval::ReviewSnapshot>,
}
impl<'a> Visibility<'a> {
    pub fn new(tx: &'a Connection, actor: &'a Actor, resource: &'a str, row: &'a Value) -> Self {
        Self {
            tx,
            actor,
            resource,
            row,
            review: None,
        }
    }
    pub fn review(&mut self) -> Result<&approval::ReviewSnapshot> {
        if self.review.is_none() {
            self.review = Some(approval::ReviewSnapshot::read(
                self.tx, self.actor, self.row,
            )?);
        }
        Ok(self.review.as_ref().expect("initialized approval snapshot"))
    }
    fn permitted(&self) -> bool {
        self.row["companyScope"] == self.actor.company
            && auth::authorize(self.actor, self.resource, "view").is_ok()
            && auth::restriction_allows(self.actor, self.resource, "view", self.row, true)
    }
    pub fn viewable(&mut self) -> Result<bool> {
        if matches!(self.resource, "office.rooms" | "office.supplies") {
            return handling::resource_access(
                self.tx,
                self.actor,
                self.resource,
                "view",
                self.row,
                false,
            );
        }
        if self.actor.oa_assigned_only {
            return Ok(self.permitted() && self.assigned()?);
        }
        self.scope_viewable()
    }
    fn scope_viewable(&mut self) -> Result<bool> {
        let (tx, actor, resource, row) = (self.tx, self.actor, self.resource, self.row);
        Ok(self.permitted()
            && (auth::visible(actor, resource, "view", row)
                || (resource == "office.expenses"
                    && FINANCE_STATUSES.contains(&text(row, "status").as_str())
                    && auth::visible(actor, resource, "complete", row))
                || (resource == "office.general"
                    && handling::GENERAL_STATES.contains(&text(row, "status").as_str())
                    && handling::can_handle(tx, actor, row, resource)?)
                || self.assigned()?))
    }
    fn assigned(&mut self) -> Result<bool> {
        let (tx, actor, resource, row) = (self.tx, self.actor, self.resource, self.row);
        if row["ownerUserId"] == actor.id || !self.review()?.task_keys().is_empty() {
            return Ok(true);
        }
        let principals = approval::object_approvers(tx, actor, resource, row)?;
        let people = personnel_accounts::links(
            tx,
            &principals.iter().map(|p| p.user_id).collect::<Vec<_>>(),
        )?;
        let actor_person =
            personnel_accounts::person_for_account(tx, actor.id)?.and_then(|p| p["id"].as_i64());
        if self.review()?.plan.as_ref().is_some_and(|plan| {
            plan.notify_user_ids.contains(&actor.id)
                || plan.steps.iter().any(|step| {
                    (step.acted_by_user_id == Some(actor.id)
                        && step.acted_by_context == actor.context
                        && step.acted_by_person_id == actor_person)
                        || principals.iter().any(|principal| {
                            principal.user_id == step.approver_user_id
                                && principal.context == step.approver_context
                                && people
                                    .get(&principal.user_id)
                                    .and_then(|p| p["id"].as_i64())
                                    == step.approver_person_id
                        })
                })
        }) {
            return Ok(true);
        }
        if fulfillment::assigned(tx, actor, row)? {
            return Ok(true);
        }
        Ok((resource == "office.general"
            && !text(row, "handlingKey").is_empty()
            && handling::GENERAL_STATES.contains(&text(row, "status").as_str())
            || matches!(resource, "office.rooms" | "office.supplies")
                && matches!(
                    row["status"].as_str(),
                    Some("Approved" | "InUse" | "Completed" | "Issued" | "Returned")
                ))
            && handling::can_handle(tx, actor, row, resource)?)
    }
}
pub(in crate::engine) fn viewable(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    row: &Value,
) -> Result<bool> {
    Visibility::new(tx, actor, resource, row).viewable()
}
pub(in crate::engine) fn assigned_to(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    row: &Value,
) -> Result<bool> {
    Visibility::new(tx, actor, resource, row).assigned()
}
