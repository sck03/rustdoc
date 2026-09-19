//! Company selection, tree search and disclosure state are independent of IO.
use export_doc_domain::organization::{self, DepartmentPath};
use export_doc_engine::generated_api::*;
use std::collections::{BTreeMap, BTreeSet};

#[derive(Default)]
pub struct OrganizationModel {
    pub directory: ApiOrganizationDirectoryResponse,
    pub company: String,
    pub keyword: String,
    pub selected: String,
    pub expanded_depth: usize,
    pub overrides: BTreeMap<String, bool>,
}
impl OrganizationModel {
    pub fn load(
        &mut self,
        directory: ApiOrganizationDirectoryResponse,
        preferred_company: &str,
    ) -> Result<(), String> {
        organization::paths(&directory.departments)?;
        if !directory
            .companies
            .iter()
            .any(|company| company.code == self.company)
        {
            self.company = directory
                .companies
                .iter()
                .find(|company| company.code == preferred_company)
                .or_else(|| directory.companies.iter().find(|company| company.is_active))
                .or(directory.companies.first())
                .map(|company| company.code.clone())
                .unwrap_or_default();
            self.expanded_depth = 1;
        }
        self.directory = directory;
        Ok(())
    }
    pub fn company(&self) -> Option<&ApiOrganizationCompanyDto> {
        self.directory
            .companies
            .iter()
            .find(|company| company.code == self.company)
    }
    pub fn entries(&self) -> Result<Vec<DepartmentPath>, String> {
        organization::paths(
            &self
                .directory
                .departments
                .iter()
                .filter(|department| department.company_code == self.company)
                .cloned()
                .collect::<Vec<_>>(),
        )
    }
    fn state_key(&self, code: &str) -> String {
        format!("{}:{code}", self.company)
    }
    pub fn expanded(&self, entry: &DepartmentPath) -> bool {
        !self.keyword.trim().is_empty()
            || *self
                .overrides
                .get(&self.state_key(&entry.department.code))
                .unwrap_or(&(entry.ancestors.len() < self.expanded_depth))
    }
    pub fn visible(&self) -> Result<Vec<DepartmentPath>, String> {
        let entries = self.entries()?;
        let keyword = organization::search_key(&self.keyword);
        let mut keep = BTreeSet::new();
        if !keyword.is_empty() {
            for entry in &entries {
                if organization::search_key(&format!(
                    "{} {} {}",
                    entry.department.code, entry.department.name, entry.department.manager_name
                ))
                .contains(&keyword)
                {
                    keep.insert(entry.department.code.clone());
                    keep.extend(entry.ancestors.iter().cloned());
                }
            }
        }
        let expanded: BTreeSet<_> = entries
            .iter()
            .filter(|entry| self.expanded(entry))
            .map(|entry| entry.department.code.as_str())
            .collect();
        Ok(entries
            .iter()
            .filter(|entry| {
                if keyword.is_empty() {
                    entry
                        .ancestors
                        .iter()
                        .all(|code| expanded.contains(code.as_str()))
                } else {
                    keep.contains(&entry.department.code)
                }
            })
            .cloned()
            .collect())
    }
    pub fn toggle(&mut self, code: &str) -> Result<(), String> {
        if !self.keyword.trim().is_empty() {
            return Ok(());
        }
        if let Some(entry) = self
            .entries()?
            .iter()
            .find(|entry| entry.department.code == code)
        {
            self.overrides
                .insert(self.state_key(code), !self.expanded(entry));
        }
        Ok(())
    }
    pub fn expand_to(&mut self, depth: usize) {
        self.expanded_depth = depth;
        let prefix = format!("{}:", self.company);
        self.overrides.retain(|key, _| !key.starts_with(&prefix));
    }
    pub fn reveal(&mut self, code: &str) -> Result<(), String> {
        if let Some(department) = self
            .directory
            .departments
            .iter()
            .find(|department| department.code == code)
        {
            self.company = department.company_code.clone();
        }
        self.keyword.clear();
        if let Some(entry) = self
            .entries()?
            .iter()
            .find(|entry| entry.department.code == code)
        {
            for code in &entry.ancestors {
                self.overrides.insert(self.state_key(code), true);
            }
            self.selected = code.into();
        }
        Ok(())
    }
}
