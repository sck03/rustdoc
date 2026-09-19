//! Organization ancestry is shared by validation and native tree presentation.
use crate::generated_api::ApiOrganizationDepartmentDto;
use std::collections::{BTreeMap, BTreeSet};
use unicode_normalization::UnicodeNormalization;

pub fn search_key(value: &str) -> String {
    value.trim().nfc().collect::<String>().to_lowercase()
}

#[derive(Clone)]
pub struct DepartmentPath {
    pub department: ApiOrganizationDepartmentDto,
    pub ancestors: Vec<String>,
    pub label: String,
    pub child_count: usize,
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
pub fn paths(departments: &[ApiOrganizationDepartmentDto]) -> Result<Vec<DepartmentPath>, String> {
    let directory: BTreeMap<_, _> = departments
        .iter()
        .map(|department| (department.code.as_str(), department))
        .collect();
    let links: Vec<_> = departments
        .iter()
        .map(|department| DepartmentLink {
            code: department.code.clone(),
            company_code: department.company_code.clone(),
            parent_code: department.parent_code.clone(),
        })
        .collect();
    let lineages = ancestry(&links)?;
    let mut children = BTreeMap::new();
    for department in departments {
        if let Some(parent) = department
            .parent_code
            .as_deref()
            .filter(|value| !value.is_empty())
        {
            *children.entry(parent).or_insert(0) += 1;
        }
    }
    let mut output = Vec::with_capacity(departments.len());
    for department in departments {
        let ancestors = lineages
            .get(&department.code)
            .ok_or("部门层级不完整。")?
            .clone();
        let names: Vec<_> = ancestors
            .iter()
            .map(|code| {
                directory
                    .get(code.as_str())
                    .map(|node| node.name.as_str())
                    .ok_or("上级部门不存在。")
            })
            .chain(std::iter::once(Ok(department.name.as_str())))
            .collect::<Result<_, _>>()?;
        output.push(DepartmentPath {
            department: department.clone(),
            ancestors,
            label: names.join(" / "),
            child_count: *children.get(department.code.as_str()).unwrap_or(&0),
        });
    }
    let mut siblings: BTreeMap<String, Vec<DepartmentPath>> = BTreeMap::new();
    for entry in output {
        siblings
            .entry(entry.department.parent_code.clone().unwrap_or_default())
            .or_default()
            .push(entry);
    }
    for children in siblings.values_mut() {
        children.sort_by(|a, b| {
            a.department
                .name
                .cmp(&b.department.name)
                .then_with(|| a.department.code.cmp(&b.department.code))
        });
    }
    let mut pending = siblings.remove("").unwrap_or_default();
    pending.reverse();
    let mut ordered = Vec::with_capacity(departments.len());
    while let Some(entry) = pending.pop() {
        if let Some(children) = siblings.remove(&entry.department.code) {
            pending.extend(children.into_iter().rev());
        }
        ordered.push(entry);
    }
    Ok(ordered)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn department(code: &str, parent: Option<&str>, name: &str) -> ApiOrganizationDepartmentDto {
        ApiOrganizationDepartmentDto {
            code: code.into(),
            parent_code: parent.map(str::to_owned),
            name: name.into(),
            company_code: "COMPANY".into(),
            is_active: true,
            ..Default::default()
        }
    }
    #[test]
    fn equal_names_remain_under_their_own_parent() {
        let tree = paths(&[
            department("B", None, "同名"),
            department("A", None, "同名"),
            department("A1", Some("A"), "子部门"),
            department("B1", Some("B"), "子部门"),
        ])
        .unwrap();
        assert_eq!(
            tree.iter()
                .map(|entry| entry.department.code.as_str())
                .collect::<Vec<_>>(),
            ["A", "A1", "B", "B1"]
        );
        assert_eq!(tree[1].ancestors, ["A"]);
    }
    #[test]
    fn hierarchy_rejects_missing_parents_cycles_and_cross_company_links() {
        assert!(paths(&[department("A", Some("missing"), "部门")]).is_err());
        assert!(
            paths(&[
                department("A", Some("B"), "部门"),
                department("B", Some("A"), "部门")
            ])
            .is_err()
        );
        let mut child = department("B", Some("A"), "子部门");
        child.company_code = "OTHER".into();
        assert!(paths(&[department("A", None, "部门"), child]).is_err());
    }
}
