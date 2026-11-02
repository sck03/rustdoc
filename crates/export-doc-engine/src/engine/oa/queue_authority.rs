//! Resolve delegation and unit identities once within the current read snapshot.
use super::*;
use export_doc_storage::RecordApprover;

pub(in crate::engine) struct QueueAuthority {
    current: Actor,
    principals: Vec<Actor>,
}
impl QueueAuthority {
    pub(in crate::engine) fn new(tx: &Connection, actor: &Actor) -> Result<Self> {
        let mut principals = vec![];
        let mut loaded = std::collections::HashSet::new();
        for (id, _) in settings::principals(tx, actor)? {
            if !loaded.insert(id) {
                continue;
            }
            match super::super::units::business_actors(tx, id, actor.edition, &actor.company) {
                Ok(actors) => principals.extend(actors),
                Err(e) if matches!(e.status, Some(400 | 401 | 403 | 404)) => {}
                Err(e) => return Err(e),
            }
        }
        Ok(Self {
            current: actor.clone(),
            principals,
        })
    }
    fn delegates<'a>(
        &'a self,
        resource: &'a str,
        action: &'a str,
    ) -> impl Iterator<Item = &'a Actor> {
        self.principals.iter().filter(move |actor| {
            auth::authorize(actor, resource, "view").is_ok()
                && auth::authorize(actor, resource, action).is_ok()
        })
    }
    fn principal(actor: &Actor, resource: &str, action: &str) -> RecordApprover {
        RecordApprover {
            user_id: actor.id,
            context: actor.context.clone(),
            restrictions: auth::object_scope_filters(actor, resource, action),
        }
    }
    pub(in crate::engine) fn approvers(&self, resource: &str) -> Vec<RecordApprover> {
        self.for_action(resource, "approve")
    }
    pub(in crate::engine) fn for_action(
        &self,
        resource: &str,
        action: &str,
    ) -> Vec<RecordApprover> {
        std::iter::once(Self::principal(&self.current, resource, action))
            .chain(
                self.delegates(resource, action)
                    .map(|actor| Self::principal(actor, resource, action)),
            )
            .collect()
    }
    pub(in crate::engine) fn approvers_for(
        &self,
        resource: &str,
        row: &Value,
    ) -> Vec<RecordApprover> {
        let direct = auth::restriction_allows(&self.current, resource, "view", row, true);
        direct
            .then(|| Self::principal(&self.current, resource, "approve"))
            .into_iter()
            .chain(
                self.delegates(resource, "approve")
                    .filter(|actor| {
                        auth::restriction_allows(actor, resource, "view", row, true)
                            && auth::restriction_allows(actor, resource, "approve", row, true)
                    })
                    .map(|actor| Self::principal(actor, resource, "approve")),
            )
            .collect()
    }
}
