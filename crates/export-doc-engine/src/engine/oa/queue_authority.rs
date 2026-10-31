//! Resolve delegation and unit identities once within the current read snapshot.
use super::*;
use export_doc_storage::RecordApprover;

pub(in crate::engine) struct QueueAuthority {
    current: RecordApprover,
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
            current: RecordApprover {
                user_id: actor.id,
                context: actor.context.clone(),
            },
            principals,
        })
    }
    pub(in crate::engine) fn approvers(&self, resource: &str) -> Vec<RecordApprover> {
        std::iter::once(RecordApprover {
            user_id: self.current.user_id,
            context: self.current.context.clone(),
        })
        .chain(
            self.principals
                .iter()
                .filter(|actor| {
                    auth::authorize(actor, resource, "view").is_ok()
                        && auth::authorize(actor, resource, "approve").is_ok()
                })
                .map(|actor| RecordApprover {
                    user_id: actor.id,
                    context: actor.context.clone(),
                }),
        )
        .collect()
    }
}
