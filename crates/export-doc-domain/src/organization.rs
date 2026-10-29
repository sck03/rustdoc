//! Pure organization ancestry and identifier normalization.
use std::collections::{BTreeMap, BTreeSet};
use unicode_normalization::UnicodeNormalization;

pub fn search_key(value: &str) -> String {
    value.trim().nfc().collect::<String>().to_lowercase()
}

#[derive(Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct OrganizationLeader {
    pub employee_id: i64,
    #[serde(default)]
    pub appointment_id: String,
    pub workflow_kinds: Vec<String>,
}
pub fn validate_leaders(
    leaders: &[OrganizationLeader],
    primary: Option<i64>,
) -> Result<(), String> {
    let mut people = BTreeSet::new();
    people.extend(primary);
    if leaders.len() > 10 {
        return Err("分管负责人最多十位。".into());
    }
    for leader in leaders {
        let mut kinds = BTreeSet::new();
        if leader.employee_id <= 0
            || !people.insert(leader.employee_id)
            || leader.workflow_kinds.is_empty()
            || leader.workflow_kinds.iter().any(|kind| {
                ![
                    "leave",
                    "overtime",
                    "expense",
                    "travel",
                    "purchase",
                    "general",
                    "personnel",
                    "room",
                    "supply",
                ]
                .contains(&kind.as_str())
                    || !kinds.insert(kind)
            })
        {
            return Err("分管负责人须互不重复，并明确选择负责的申请业务。".into());
        }
    }
    Ok(())
}

#[derive(Clone, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DepartmentLink {
    pub code: String,
    pub company_code: String,
    pub parent_code: Option<String>,
}
pub fn ancestry(departments: &[DepartmentLink]) -> Result<BTreeMap<String, Vec<String>>, String> {
    let directory: BTreeMap<_, _> = departments
        .iter()
        .map(|department| (department.code.as_str(), department))
        .collect();
    if directory.len() != departments.len() {
        return Err("部门代码重复。".into());
    }
    let mut result = BTreeMap::new();
    for department in departments {
        let mut ancestors = vec![];
        let mut seen = BTreeSet::from([department.code.as_str()]);
        let mut parent = department
            .parent_code
            .as_deref()
            .filter(|value| !value.is_empty());
        while let Some(code) = parent {
            if !seen.insert(code) {
                return Err("部门层级不能形成循环。".into());
            }
            let node = directory.get(code).ok_or("上级部门不存在。")?;
            if node.company_code != department.company_code {
                return Err("上级部门必须属于同一公司。".into());
            }
            ancestors.push(code.to_owned());
            if ancestors.len() >= 32 {
                return Err("部门层级不能超过 32 层。".into());
            }
            parent = node
                .parent_code
                .as_deref()
                .filter(|value| !value.is_empty());
        }
        ancestors.reverse();
        result.insert(department.code.clone(), ancestors);
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn department(code: &str, parent: Option<&str>) -> DepartmentLink {
        DepartmentLink {
            code: code.into(),
            parent_code: parent.map(str::to_owned),
            company_code: "COMPANY".into(),
        }
    }
    #[test]
    fn ancestry_uses_codes_and_preserves_each_branch() {
        let tree = ancestry(&[
            department("B", None),
            department("A", None),
            department("A1", Some("A")),
            department("B1", Some("B")),
        ])
        .unwrap();
        assert_eq!(tree["A1"], ["A"]);
        assert_eq!(tree["B1"], ["B"]);
        assert!(ancestry(&[department("A", None), department("A", None)]).is_err());
    }
    #[test]
    fn hierarchy_rejects_missing_parents_cycles_and_cross_company_links() {
        assert!(ancestry(&[department("A", Some("missing"))]).is_err());
        assert!(ancestry(&[department("A", Some("B")), department("B", Some("A"))]).is_err());
        let mut child = department("B", Some("A"));
        child.company_code = "OTHER".into();
        assert!(ancestry(&[department("A", None), child]).is_err());
    }
}
