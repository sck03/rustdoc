//! A row-bound permission evaluation shares its approval snapshot within one transaction.
use super::{FINANCE_STATUSES, approval, auth, fulfillment, handling, text};
use crate::engine::{
    error::Result,
    store::{Actor, Connection},
};
use serde_json::Value;

pub(in crate::engine) struct Visibility<'a, 'db> {
    context: &'a mut super::read_context::ReadContext<'db>,
    resource: &'a str,
    row: &'a Value,
    review: Option<approval::ReviewSnapshot>,
}
impl<'a, 'db> Visibility<'a, 'db> {
    pub fn new(
        context: &'a mut super::read_context::ReadContext<'db>,
        resource: &'a str,
        row: &'a Value,
    ) -> Self {
        Self {
            context,
            resource,
            row,
            review: None,
        }
    }
    pub fn review(&mut self) -> Result<&approval::ReviewSnapshot> {
        if self.review.is_none() {
            self.review = Some(approval::ReviewSnapshot::read_with(self.context, self.row)?);
        }
        Ok(self.review.as_ref().expect("initialized approval snapshot"))
    }
    pub fn can_handle(&mut self) -> Result<bool> {
        self.context.can_handle(self.resource, self.row)
    }
    fn permitted(&self) -> bool {
        self.row["companyScope"] == self.context.actor.company
            && auth::authorize(self.context.actor, self.resource, "view").is_ok()
            && auth::restriction_allows(self.context.actor, self.resource, "view", self.row, true)
    }
    pub fn viewable(&mut self) -> Result<bool> {
        if matches!(self.resource, "office.rooms" | "office.supplies") {
            return handling::resource_access(
                self.context.tx,
                self.context.actor,
                self.resource,
                "view",
                self.row,
                false,
            );
        }
        if self.context.actor.oa_assigned_only {
            return Ok(self.permitted() && self.assigned()?);
        }
        self.scope_viewable()
    }
    fn scope_viewable(&mut self) -> Result<bool> {
        let (actor, resource, row) = (self.context.actor, self.resource, self.row);
        Ok(self.permitted()
            && (auth::visible(actor, resource, "view", row)
                || (resource == "office.expenses"
                    && FINANCE_STATUSES.contains(&text(row, "status").as_str())
                    && auth::visible(actor, resource, "complete", row))
                || (resource == "office.general"
                    && handling::GENERAL_STATES.contains(&text(row, "status").as_str())
                    && self.context.can_handle(resource, row)?)
                || self.assigned()?))
    }
    fn assigned(&mut self) -> Result<bool> {
        let (tx, actor, resource, row) =
            (self.context.tx, self.context.actor, self.resource, self.row);
        if row["ownerUserId"] == actor.id || !self.review()?.task_keys().is_empty() {
            return Ok(true);
        }
        let principals = self.context.authority()?.approvers_for(resource, row);
        let users = std::iter::once(actor.id)
            .chain(principals.iter().map(|p| p.user_id))
            .collect::<Vec<_>>();
        let people = self.context.people(&users)?;
        let actor_person = people
            .get(&actor.id)
            .and_then(Option::as_ref)
            .and_then(|p| p["id"].as_i64());
        if self
            .review
            .as_ref()
            .and_then(|review| review.plan.as_ref())
            .is_some_and(|plan| {
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
                                        .and_then(Option::as_ref)
                                        .and_then(|p| p["id"].as_i64())
                                        == step.approver_person_id
                            })
                    })
            })
        {
            return Ok(true);
        }
        if fulfillment::assigned(tx, actor, row)? {
            return Ok(true);
        }
        Ok((resource == "office.general"
            && !text(row, "serviceId").is_empty()
            && handling::GENERAL_STATES.contains(&text(row, "status").as_str())
            || matches!(resource, "office.rooms" | "office.supplies")
                && matches!(
                    row["status"].as_str(),
                    Some("Approved" | "InUse" | "Completed" | "Issued" | "Returned")
                ))
            && self.context.can_handle(resource, row)?)
    }
}
pub(in crate::engine) fn viewable(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    row: &Value,
) -> Result<bool> {
    Visibility::new(
        &mut super::read_context::ReadContext::new(tx, actor),
        resource,
        row,
    )
    .viewable()
}
pub(in crate::engine) fn assigned_to(
    tx: &Connection,
    actor: &Actor,
    resource: &str,
    row: &Value,
) -> Result<bool> {
    Visibility::new(
        &mut super::read_context::ReadContext::new(tx, actor),
        resource,
        row,
    )
    .assigned()
}
