//! Reuse identity lookups only within one database read snapshot; never cache row permissions.
use super::{queue_authority::QueueAuthority, settings};
use crate::engine::{
    error::Result,
    handling, personnel_accounts,
    store::{Actor, Connection},
};
use serde_json::Value;
use std::collections::HashMap;

pub(in crate::engine) struct ReadContext<'a> {
    pub tx: &'a Connection<'a>,
    pub actor: &'a Actor,
    signer: Option<Option<i64>>,
    delegations: Option<Vec<(i64, String)>>,
    authority: Option<QueueAuthority>,
    people: HashMap<i64, Option<Value>>,
    handling_keys: HashMap<String, Vec<String>>,
}
impl<'a> ReadContext<'a> {
    pub fn new(tx: &'a Connection<'a>, actor: &'a Actor) -> Self {
        Self {
            tx,
            actor,
            signer: None,
            delegations: None,
            authority: None,
            people: HashMap::new(),
            handling_keys: HashMap::new(),
        }
    }
    pub fn signer(&mut self) -> Result<Option<i64>> {
        if self.signer.is_none() {
            self.signer = Some(match super::qualification::person_id(self.tx, self.actor) {
                Ok(id) => Some(id),
                Err(cause) if cause.status == Some(403) => None,
                Err(cause) => return Err(cause),
            });
        }
        Ok(self.signer.flatten())
    }
    pub fn delegations(&mut self) -> Result<&[(i64, String)]> {
        if self.delegations.is_none() {
            self.delegations = Some(settings::principals(self.tx, self.actor)?);
        }
        Ok(self.delegations.as_deref().unwrap())
    }
    pub fn authority(&mut self) -> Result<&QueueAuthority> {
        if self.authority.is_none() {
            self.delegations()?;
            self.authority = Some(QueueAuthority::from_delegations(
                self.tx,
                self.actor,
                self.delegations.as_deref().unwrap(),
            )?);
        }
        Ok(self.authority.as_ref().unwrap())
    }
    pub fn people(&mut self, users: &[i64]) -> Result<&HashMap<i64, Option<Value>>> {
        let mut missing: Vec<_> = users
            .iter()
            .copied()
            .filter(|id| !self.people.contains_key(id))
            .collect();
        missing.sort_unstable();
        missing.dedup();
        if !missing.is_empty() {
            let mut people = personnel_accounts::links(self.tx, &missing)?;
            for id in missing {
                self.people.insert(id, people.remove(&id));
            }
        }
        Ok(&self.people)
    }
    pub fn handling_keys(&mut self, resource: &str) -> Result<&[String]> {
        if !self.handling_keys.contains_key(resource) {
            self.handling_keys.insert(
                resource.into(),
                handling::keys(self.tx, self.actor, resource)?,
            );
        }
        Ok(&self.handling_keys[resource])
    }
    pub fn can_handle(&mut self, resource: &str, row: &Value) -> Result<bool> {
        let actor = self.actor;
        handling::can_handle_using(actor, row, resource, || {
            Ok(handling::assigned(self.handling_keys(resource)?, row))
        })
    }
}
