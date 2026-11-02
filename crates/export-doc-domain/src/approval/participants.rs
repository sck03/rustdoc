//! A bounded signer index keeps independent review valid across partial handling and retries.
use super::*;
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Participant {
    pub node_key: String,
    pub user_id: i64,
    pub person_id: Option<i64>,
    pub principal_person_id: Option<i64>,
    pub context: String,
    pub generation: u32,
}
impl ApprovalPlan {
    pub fn record_participant(
        &mut self,
        node: usize,
        user: i64,
        person: Option<i64>,
        principal: Option<i64>,
        context: &str,
    ) -> Result<(), String> {
        let key = self
            .nodes
            .get(node)
            .ok_or("办理参与人引用未知节点。")?
            .key
            .clone();
        let fact = Participant {
            node_key: key.clone(),
            user_id: user,
            person_id: person,
            principal_person_id: principal,
            context: context.into(),
            generation: self.generation,
        };
        if let Some(previous) = self.participants.iter_mut().find(|p| {
            p.node_key == key
                && p.person_id == person
                && p.principal_person_id == principal
                && (person.is_some() || p.user_id == user)
        }) {
            *previous = fact;
        } else {
            if self.participants.len() >= 200 {
                return Err("实际办理参与人累计超过二百项，请使用新的正式申请。".into());
            }
            self.participants.push(fact);
        }
        Ok(())
    }
    pub(super) fn participant_conflict(&self, node: usize, user: i64, person: Option<i64>) -> bool {
        self.participants.iter().any(|p| {
            let same = p.user_id == user
                || person
                    .is_some_and(|id| p.person_id == Some(id) || p.principal_person_id == Some(id));
            if !same {
                return false;
            }
            if p.node_key == self.nodes[node].key {
                return self.nodes[node].kind == NodeKind::Approval
                    && p.generation == self.generation;
            }
            self.nodes[node].independent_review
                || self
                    .nodes
                    .iter()
                    .find(|n| n.key == p.node_key)
                    .is_some_and(|n| n.independent_review)
        })
    }
}
