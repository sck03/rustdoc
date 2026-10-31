//! Finite object scopes compose by intersection, independent of rank labels.
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;
#[derive(Clone, Debug, Serialize, Deserialize)]
pub enum ScopeMode {
    Deny,
    Unrestricted,
    Units,
    Departments,
    Projects,
    Participation,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AccessRule {
    pub resource_key: String,
    pub action: String,
    pub mode: ScopeMode,
    pub unit_codes: BTreeSet<String>,
    pub departments: BTreeSet<(String, String)>,
    pub project_ids: BTreeSet<String>,
}
impl AccessRule {
    pub fn accepts(
        &self,
        unit: &str,
        department: &str,
        project: Option<&str>,
        owner: i64,
        actor: i64,
    ) -> bool {
        match self.mode {
            ScopeMode::Deny => false,
            ScopeMode::Unrestricted => true,
            ScopeMode::Units => self.unit_codes.contains(unit),
            ScopeMode::Departments => self.departments.contains(&(unit.into(), department.into())),
            ScopeMode::Projects => project.is_some_and(|id| self.project_ids.contains(id)),
            ScopeMode::Participation => owner == actor,
        }
    }
}
