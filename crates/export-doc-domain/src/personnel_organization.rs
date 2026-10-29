//! Concurrent appointments and reporting relationships, independent of accounts and storage.
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct PersonnelOrganization {
    #[serde(default)]
    pub primary_assignment_id: String,
    #[serde(default)]
    pub primary_starts_on: String,
    #[serde(default)]
    pub manager_assignment_id: Option<String>,
    #[serde(default)]
    pub functional_manager_assignment_ids: Vec<String>,
    #[serde(default)]
    pub manager_employee_id: Option<i64>,
    #[serde(default)]
    pub functional_manager_employee_ids: Vec<i64>,
    #[serde(default)]
    pub additional_assignments: Vec<Assignment>,
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn matrix_edges_cannot_loop_and_appointments_cannot_duplicate() {
        let mut graph = HashMap::from([(1, vec![2, 3]), (2, vec![4]), (3, vec![4])]);
        validate_reporting_graph(&graph).unwrap();
        graph.insert(4, vec![1]);
        assert!(validate_reporting_graph(&graph).is_err());
        let mut organization = PersonnelOrganization {
            primary_assignment_id: "primary".into(),
            primary_starts_on: String::new(),
            manager_assignment_id: None,
            functional_manager_assignment_ids: vec![],
            manager_employee_id: Some(2),
            functional_manager_employee_ids: vec![3],
            additional_assignments: vec![Assignment {
                id: "additional".into(),
                manager_assignment_id: None,
                functional_manager_assignment_ids: vec![],
                company_code: "A".into(),
                starts_on: String::new(),
                ends_on: String::new(),
                department_id: "B".into(),
                job_title: "专员".into(),
                manager_employee_id: Some(4),
                functional_manager_employee_ids: vec![],
            }],
        };
        organization.validate(1, "A", "A").unwrap();
        assert_eq!(organization.departments("A"), vec!["A", "B"]);
        organization.additional_assignments[0].department_id = "A".into();
        assert!(organization.validate(1, "A", "A").is_err());
        organization.additional_assignments.clear();
        organization.functional_manager_employee_ids.push(1);
        assert!(organization.validate(1, "A", "A").is_err());
    }
}
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct Assignment {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub manager_assignment_id: Option<String>,
    #[serde(default)]
    pub functional_manager_assignment_ids: Vec<String>,
    #[serde(default)]
    pub company_code: String,
    #[serde(default)]
    pub starts_on: String,
    #[serde(default)]
    pub ends_on: String,
    pub department_id: String,
    pub job_title: String,
    pub manager_employee_id: Option<i64>,
    #[serde(default)]
    pub functional_manager_employee_ids: Vec<i64>,
}
impl PersonnelOrganization {
    pub fn manager_ids(&self) -> Vec<i64> {
        self.manager_employee_id
            .into_iter()
            .chain(self.functional_manager_employee_ids.iter().copied())
            .chain(self.additional_assignments.iter().flat_map(|a| {
                a.manager_employee_id
                    .into_iter()
                    .chain(a.functional_manager_employee_ids.iter().copied())
            }))
            .collect::<HashSet<_>>()
            .into_iter()
            .collect()
    }
    pub fn departments(&self, primary: &str) -> Vec<String> {
        std::iter::once(primary.to_owned())
            .chain(
                self.additional_assignments
                    .iter()
                    .map(|a| a.department_id.clone()),
            )
            .collect()
    }
    pub fn validate(&self, employee: i64, primary: &str, company: &str) -> Result<(), String> {
        if self.additional_assignments.len() > 20 {
            return Err("兼职部门最多二十个。".into());
        }
        let mut departments = HashSet::from([(company, primary)]);
        for assignment in &self.additional_assignments {
            if assignment.department_id.trim().is_empty()
                || assignment.company_code.trim().is_empty()
                || !departments.insert((
                    assignment.company_code.as_str(),
                    assignment.department_id.as_str(),
                ))
                || assignment.job_title.trim().is_empty()
                || assignment.job_title.chars().count() > 120
            {
                return Err("兼职须填写部门和职位，部门不能与主职或其它兼职重复。".into());
            }
            assignment.validate_dates()?;
        }
        for (line, functional) in std::iter::once((
            self.manager_employee_id,
            &self.functional_manager_employee_ids,
        ))
        .chain(
            self.additional_assignments
                .iter()
                .map(|a| (a.manager_employee_id, &a.functional_manager_employee_ids)),
        ) {
            let mut managers = HashSet::new();
            if functional.len() > 10
                || line
                    .into_iter()
                    .chain(functional.iter().copied())
                    .any(|id| id <= 0 || id == employee || !managers.insert(id))
            {
                return Err(
                    "直属及职能负责人须有效且不重复，不能向自己汇报；每个任职最多十位职能负责人。"
                        .into(),
                );
            }
        }
        Ok(())
    }
}

impl Assignment {
    fn date(value: &str) -> Result<Option<chrono::NaiveDate>, String> {
        if value.is_empty() {
            return Ok(None);
        }
        let parsed = chrono::NaiveDate::parse_from_str(value, "%Y-%m-%d")
            .map_err(|_| "任职日期须为 YYYY-MM-DD。".to_owned())?;
        if parsed.to_string() != value {
            return Err("任职日期须为 YYYY-MM-DD。".into());
        }
        Ok(Some(parsed))
    }
    pub fn validate_dates(&self) -> Result<(), String> {
        if let (Some(start), Some(end)) = (Self::date(&self.starts_on)?, Self::date(&self.ends_on)?)
        {
            if end < start {
                return Err("任职终止日期不能早于生效日期。".into());
            }
        }
        Ok(())
    }
    pub fn active_on(&self, today: chrono::NaiveDate) -> Result<bool, String> {
        Ok(Self::date(&self.starts_on)?.is_none_or(|d| d <= today)
            && Self::date(&self.ends_on)?.is_none_or(|d| today <= d))
    }
}

pub fn validate_reporting_graph<K: Clone + Eq + std::hash::Hash>(
    edges: &HashMap<K, Vec<K>>,
) -> Result<(), String> {
    fn visit<K: Clone + Eq + std::hash::Hash>(
        id: K,
        edges: &HashMap<K, Vec<K>>,
        active: &mut HashSet<K>,
        done: &mut HashSet<K>,
    ) -> Result<(), String> {
        if done.contains(&id) {
            return Ok(());
        }
        if active.len() >= 64 || !active.insert(id.clone()) {
            return Err("直属或职能汇报关系存在循环，或超过六十四级。".into());
        }
        if let Some(managers) = edges.get(&id) {
            for manager in managers {
                visit(manager.clone(), edges, active, done)?;
            }
        }
        active.remove(&id);
        done.insert(id);
        Ok(())
    }
    let mut done = HashSet::new();
    for id in edges.keys() {
        visit(id.clone(), edges, &mut HashSet::new(), &mut done)?;
    }
    Ok(())
}
