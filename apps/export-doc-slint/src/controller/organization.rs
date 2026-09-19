use super::*;
use crate::{OrganizationRow, PageTab};
use export_doc_engine::contracts;
use serde_json::{Value, json};

impl Desktop {
    pub fn sync_organization(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let entries = match self.organization.visible() {
            Ok(entries) => entries,
            Err(cause) => {
                self.error(cause);
                return;
            }
        };
        app.set_org_companies(model(
            self.organization
                .directory
                .companies
                .iter()
                .map(|company| PageTab {
                    key: company.code.clone().into(),
                    label: format!(
                        "{}{}",
                        company.name,
                        if company.is_active { "" } else { " · 停用" }
                    )
                    .into(),
                })
                .collect(),
        ));
        app.set_org_company_code(self.organization.company.clone().into());
        app.set_org_company_name(
            self.organization
                .company()
                .map(|company| company.name.as_str())
                .unwrap_or("请选择公司")
                .into(),
        );
        app.set_org_company_active(
            self.organization
                .company()
                .is_some_and(|company| company.is_active),
        );
        app.set_org_search(self.organization.keyword.clone().into());
        app.set_org_selected(self.organization.selected.clone().into());
        app.set_org_rows(model(
            entries
                .iter()
                .map(|entry| OrganizationRow {
                    code: entry.department.code.clone().into(),
                    name: entry.department.name.clone().into(),
                    manager: entry.department.manager_name.clone().into(),
                    depth: entry.ancestors.len() as i32,
                    children: entry.child_count as i32,
                    expanded: self.organization.expanded(entry),
                    active: entry.department.is_active,
                })
                .collect(),
        ));
        let departments: Vec<_> = self
            .organization
            .directory
            .departments
            .iter()
            .filter(|department| department.company_code == self.organization.company)
            .collect();
        app.set_org_summary(
            format!(
                "{} · {} 个部门 · {} 个启用",
                self.organization.company,
                departments.len(),
                departments
                    .iter()
                    .filter(|department| department.is_active)
                    .count()
            )
            .into(),
        );
        let selected = self.organization.entries().ok().and_then(|entries| {
            entries
                .into_iter()
                .find(|entry| entry.department.code == self.organization.selected)
        });
        app.set_org_selected_path(
            selected
                .map(|entry| {
                    format!(
                        "{} / {}{}",
                        self.organization
                            .company()
                            .map(|company| company.name.as_str())
                            .unwrap_or(""),
                        entry.label,
                        if entry.department.manager_name.is_empty() {
                            String::new()
                        } else {
                            format!(" · 负责人：{}", entry.department.manager_name)
                        }
                    )
                })
                .unwrap_or_default()
                .into(),
        );
        app.set_can_create(self.can(CREATE_ORGANIZATION_COMPANY));
        app.set_can_edit(self.can(UPDATE_ORGANIZATION_COMPANY));
        app.set_can_delete(self.can(DELETE_ORGANIZATION_COMPANY));
    }

    pub fn organization_loaded(&mut self, value: Value) {
        let directory = match serde_json::from_value(value) {
            Ok(directory) => directory,
            Err(cause) => {
                self.error(format!("组织目录格式无效：{cause}"));
                return;
            }
        };
        let preferred = self
            .user
            .as_ref()
            .map(|user| user.company_scope.as_str())
            .unwrap_or("");
        if let Err(cause) = self.organization.load(directory, preferred) {
            self.error(cause);
            return;
        }
        if let Some(code) = self.pending_org_code.take() {
            if self
                .organization
                .directory
                .companies
                .iter()
                .any(|company| company.code == code)
            {
                self.organization.company = code;
            } else if let Err(cause) = self.organization.reveal(&code) {
                self.error(cause);
            }
        }
        self.sync_organization();
    }

    pub fn organization_action(&mut self, action: &str, code: &str) {
        if self.task.is_some() {
            return;
        }
        match action {
            "company" => {
                if self
                    .organization
                    .directory
                    .companies
                    .iter()
                    .any(|company| company.code == code)
                {
                    self.organization.company = code.into();
                    self.organization.selected.clear();
                    self.organization.keyword.clear();
                }
            }
            "select" => self.organization.selected = code.into(),
            "toggle" => {
                if let Err(cause) = self.organization.toggle(code) {
                    self.error(cause);
                }
            }
            "depth" => self.organization.expand_to(match code {
                "0" => 0,
                "1" => 1,
                _ => 32,
            }),
            "new-company" | "edit-company" | "new-department" | "edit-department"
            | "delete-company" | "delete-department" => {
                self.edit_organization(action, code);
                return;
            }
            _ => return,
        }
        self.sync_organization();
    }

    fn edit_organization(&mut self, action: &str, code: &str) {
        let company = action.ends_with("company");
        let new = action.starts_with("new-");
        let delete = action.starts_with("delete-");
        let operation = match (company, new, delete) {
            (true, true, _) => CREATE_ORGANIZATION_COMPANY,
            (true, false, true) => DELETE_ORGANIZATION_COMPANY,
            (true, false, false) => UPDATE_ORGANIZATION_COMPANY,
            (false, true, _) => CREATE_ORGANIZATION_DEPARTMENT,
            (false, false, true) => DELETE_ORGANIZATION_DEPARTMENT,
            (false, false, false) => UPDATE_ORGANIZATION_DEPARTMENT,
        };
        if !self.can(operation) {
            self.error("当前账号没有维护组织目录的权限。");
            return;
        }
        let record = if new {
            None
        } else if company {
            self.organization
                .directory
                .companies
                .iter()
                .find(|item| item.code == code)
                .map(|item| json!(item))
        } else {
            self.organization
                .directory
                .departments
                .iter()
                .find(|item| item.code == code)
                .map(|item| json!(item))
        };
        if !new && record.is_none() {
            self.error("目录项已变化，请刷新后重试。");
            return;
        }
        self.workspace.selected = record.clone();
        let mut value = contracts::object(operation.id, true);
        if delete {
            value["expectedVersion"] = record.as_ref().unwrap()["versionNumber"].clone();
        } else if let Some(record) = &record {
            value = contracts::overlay(value, record);
            value["expectedVersion"] = record["versionNumber"].clone();
        } else {
            value["isActive"] = json!(true);
            if !company {
                value["companyCode"] = json!(self.organization.company);
                value["parentCode"] = if code.is_empty() {
                    Value::Null
                } else {
                    json!(code)
                };
            }
        }
        let mut schema = contracts::resolve(contracts::request(operation.id)).clone();
        if !new {
            schema["properties"]["code"]["readOnly"] = json!(true);
        }
        if !company {
            schema["properties"]["companyCode"]["readOnly"] = json!(true);
        }
        let mut lookups = self.lookups.clone();
        if !company && !delete {
            let entries = match self.organization.entries() {
                Ok(entries) => entries,
                Err(cause) => {
                    self.error(cause);
                    return;
                }
            };
            lookups.insert(
                "parentCode".into(),
                entries
                    .into_iter()
                    .filter(|entry| {
                        entry.department.is_active
                            && (new
                                || (entry.department.code != code
                                    && !entry.ancestors.iter().any(|ancestor| ancestor == code)))
                    })
                    .map(|entry| (json!(entry.department.code), entry.label))
                    .collect(),
            );
            lookups.insert(
                "managerEmployeeId".into(),
                record
                    .as_ref()
                    .and_then(|record| record["managerEmployeeId"].as_i64())
                    .map(|id| {
                        (
                            json!(id),
                            record.as_ref().unwrap()["managerName"]
                                .as_str()
                                .unwrap_or("")
                                .to_owned(),
                        )
                    })
                    .into_iter()
                    .collect(),
            );
        }
        self.form_id = 0;
        self.form_operation = Some(operation);
        self.form_reply = if delete {
            "delete"
        } else {
            "organization-saved"
        }
        .into();
        self.form = Some(FormModel::new(&schema, value, lookups));
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(
                format!(
                    "{}{}{}",
                    if delete {
                        "删除"
                    } else if new {
                        "新增"
                    } else {
                        "编辑"
                    },
                    if company { "公司" } else { "部门" },
                    if delete { " · 填写原因" } else { "" }
                )
                .into(),
            );
            app.set_form_open(true);
        }
        self.sync_form();
        if !company && !delete {
            self.start(Work::OrganizationManagers {
                company: self.organization.company.clone(),
            });
        }
    }

    pub fn organization_managers(&mut self, value: Value) {
        match serde_json::from_value::<PagedResultOfOrganizationManagerRecord>(value) {
            Ok(page) => {
                if let Some(form) = &mut self.form {
                    form.lookups.insert(
                        "managerEmployeeId".into(),
                        page.items
                            .into_iter()
                            .map(|person| {
                                (
                                    json!(person.id),
                                    format!(
                                        "{} · {} · {}",
                                        person.full_name,
                                        person.employee_number,
                                        person.department_name
                                    ),
                                )
                            })
                            .collect(),
                    );
                    self.sync_form();
                }
            }
            Err(cause) => self.error(format!("负责人目录格式无效：{cause}")),
        }
    }
}
