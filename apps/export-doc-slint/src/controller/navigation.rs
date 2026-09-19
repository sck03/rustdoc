use super::*;
use crate::{NavRow, PageTab};
use export_doc_engine::{
    engine::catalog,
    workspace::{self, NAVIGATION},
};

impl Desktop {
    fn visible_page(&self, key: &str) -> bool {
        if key == "about" {
            return self.user.is_some();
        }
        if key == "settings" {
            return self.allowed(UPDATE_SETTINGS);
        }
        let tabs = workspace::tabs(key);
        if tabs.is_empty() {
            workspace::read_operation(key).is_some_and(|operation| self.allowed(operation))
        } else {
            tabs.iter().any(|(key, _)| {
                workspace::read_operation(key).is_some_and(|operation| self.allowed(operation))
            })
        }
    }
    pub fn sync_navigation(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let mut rows = vec![];
        for group in NAVIGATION {
            let selected = group.items.iter().any(|i| i.key == self.workspace.root);
            let matches: Vec<_> = group
                .items
                .iter()
                .filter(|item| self.visible_page(item.key))
                .filter(|item| {
                    self.nav_search.is_empty()
                        || item.label.contains(&self.nav_search)
                        || item.description.contains(&self.nav_search)
                })
                .collect();
            if matches.is_empty() {
                continue;
            }
            rows.push(NavRow {
                key: format!("group:{}", group.key).into(),
                label: format!(
                    "{}  {}",
                    if self.expanded.contains(group.key) {
                        "▾"
                    } else {
                        "›"
                    },
                    group.label
                )
                .into(),
                group: true,
                active: selected,
            });
            if self.expanded.contains(group.key) || !self.nav_search.is_empty() {
                rows.extend(matches.into_iter().map(|item| NavRow {
                    key: item.key.into(),
                    label: item.label.into(),
                    group: false,
                    active: item.key == self.workspace.root,
                }));
            }
        }
        app.set_navigation(model(rows));
    }
    pub fn navigate(&mut self, key: &str) {
        if let Some(search) = key.strip_prefix("search:") {
            self.nav_search = search.into();
            self.sync_navigation();
            return;
        }
        if let Some(group) = key.strip_prefix("group:") {
            if !self.expanded.remove(group) {
                self.expanded.insert(group.into());
            }
            self.sync_navigation();
            return;
        }
        if self.task.is_some() {
            self.error("请等待当前操作完成。");
            return;
        }
        if self.has_unsaved() {
            self.confirm(
                Pending::Navigate(key.into()),
                "当前页面存在未保存修改，确认离开并放弃这些修改？",
            );
            return;
        }
        self.navigate_now(key);
    }
    pub fn navigate_now(&mut self, key: &str) {
        if !self.visible_page(key) {
            self.error("当前账号没有此页面权限。");
            return;
        }
        let Some((group, item)) = workspace::navigation(key) else {
            self.error("没有找到该功能。");
            return;
        };
        self.expanded.insert(group.key.into());
        self.workspace.open(item.key);
        if let Some((key, _)) = workspace::tabs(item.key).into_iter().find(|(key, _)| {
            workspace::read_operation(key).is_some_and(|operation| self.allowed(operation))
        }) {
            self.workspace.resource = key;
        }
        self.form = None;
        self.form_operation = None;
        self.form_id = 0;
        self.payment_form = None;
        self.payment_payee = None;
        self.personnel.record = None;
        self.office = Default::default();
        self.access = Default::default();
        self.sales = Default::default();
        self.party_preview = None;
        self.hs = Default::default();
        self.mail = Default::default();
        self.ocr = Default::default();
        self.single_window = Default::default();
        self.business = None;
        self.attachments.details = None;
        self.attachments.preview_kind.clear();
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        app.set_root_key(key.into());
        app.set_can_import(self.can(PREVIEW_EXCEL_IMPORT) && self.can(CREATE_INVOICE));
        app.set_can_excel(self.can(START_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB));
        app.set_title(item.label.into());
        app.set_description(item.description.into());
        app.set_group_label(group.label.into());
        app.set_search("".into());
        app.set_job_status_index(0);
        app.set_job_detail("".into());
        ui.global::<crate::Crm>().set_status_index(0);
        ui.global::<crate::Crm>().set_customer_index(0);
        ui.global::<crate::Crm>().set_include_completed(false);
        app.set_people_department_index(0);
        app.set_people_status_index(0);
        app.set_people_attention(false);
        app.set_office_requests(false);
        app.set_office_schedule(false);
        app.set_office_history_open(false);
        app.set_office_status_index(0);
        app.set_office_inactive(false);
        app.set_office_low_stock(false);
        app.set_office_mine(false);
        if self.is_office() {
            if let Some((resource, id)) = self.pending_open.filter(|(resource, _)| *resource == key)
            {
                self.office.focus_request = Some(id);
                self.workspace.resource = resource;
                self.pending_open = None;
                app.set_office_requests(true);
            }
        }
        app.set_worklist_source_index(0);
        app.set_worklist_due_index(0);
        self.worklist_sources.clear();
        app.set_form_open(false);
        app.set_workbench(false);
        app.set_tabs(model(
            workspace::tabs(item.key)
                .iter()
                .filter(|(key, _)| {
                    workspace::read_operation(key).is_some_and(|operation| self.allowed(operation))
                })
                .map(|(key, label)| PageTab {
                    key: (*key).into(),
                    label: (*label).into(),
                })
                .collect(),
        ));
        app.set_resource(self.workspace.resource.into());
        app.set_records(model(vec![]));
        app.set_metrics(model(vec![]));
        app.set_actions(model(vec![]));
        app.set_page(
            if key == "about" {
                "about"
            } else if key == "query" {
                "query"
            } else if key == "pdf-merge" {
                "pdf-merge"
            } else if key == "settings" {
                "settings"
            } else if key == "excel" {
                "excel"
            } else if key == "companies" {
                "organization"
            } else if key == "people" || key == "directory" {
                "people"
            } else if self.is_office() {
                "office"
            } else if key == "dashboard" || key == "worklist" {
                key
            } else if key == "attachments" {
                "attachments"
            } else if key == "audit" {
                "audit"
            } else if key == "exchange-rates" {
                "exchange"
            } else if key == "opportunities" {
                "sales"
            } else if key == "ocr" {
                "ocr"
            } else if key == "email" {
                "mail"
            } else if key == "hs-codes" {
                "hs"
            } else if key == "container-projects" {
                "packing"
            } else if key == "sales-dashboard" {
                "sales-dashboard"
            } else {
                "records"
            }
            .into(),
        );
        self.sync_navigation();
        if key == "query" {
            self.setup_query();
        }
        if key == "pdf-merge" {
            self.pdf_merge_action("refresh", 0);
            return;
        }
        if key == "single-window" {
            self.setup_single_window();
            return;
        }
        if key == "ocr" {
            self.setup_ocr();
            return;
        }
        if key == "email" {
            self.setup_mail();
            return;
        }
        if key == "hs-codes" {
            self.setup_hs();
            return;
        }
        if key == "container-projects" {
            self.setup_packing();
            return;
        }
        if key == "opportunities" {
            self.setup_sales();
        }
        if key == "audit" {
            self.setup_audit();
        }
        if !["about", "excel", "exchange-rates"].contains(&key) {
            self.refresh();
        }
    }
    pub fn open_tab(&mut self, key: &str) {
        if self.task.is_some() {
            return;
        }
        if self.access.dirty() {
            self.error("请先保存或取消权限方案的修改。");
            return;
        }
        if self
            .form
            .as_ref()
            .is_some_and(|form| form.value != form.baseline)
        {
            self.error("请先保存当前分类的修改。");
            return;
        }
        let key = workspace::tabs(self.workspace.root)
            .into_iter()
            .find(|(k, _)| *k == key)
            .map(|(k, _)| k);
        if let Some(key) = key {
            self.workspace.resource = key;
            self.workspace.page = 1;
            self.workspace.selected = None;
            self.form = None;
            if let Some(ui) = self.ui.upgrade() {
                ui.global::<App>().set_resource(key.into());
                ui.global::<App>().set_page(
                    if key == "settings" {
                        "settings"
                    } else if key == "backup" {
                        "backup"
                    } else if key == "permission-templates" {
                        "access"
                    } else if key == "supplier-overview" {
                        "supplier-overview"
                    } else {
                        "records"
                    }
                    .into(),
                );
                ui.global::<App>().set_search("".into());
            }
            self.refresh();
        }
    }
    pub fn refresh(&mut self) {
        self.sync_invoice_files();
        if self.workspace.root == "query" {
            self.refresh_query();
            return;
        }
        if self.workspace.root == "single-window" {
            self.refresh_single_window();
            return;
        }
        if self.workspace.root == "ocr" {
            self.request(GET_HEALTH, 0, vec![], None, "ocr:health");
            return;
        }
        if self.workspace.root == "email" {
            self.refresh_mail();
            return;
        }
        if self.workspace.root == "hs-codes" {
            self.refresh_hs();
            return;
        }
        self.sync_party_files();
        self.sync_business_filters();
        if self.workspace.resource == "audit" {
            self.refresh_audit();
            return;
        }
        if self.workspace.resource == "permission-templates" {
            self.request(LIST_PERMISSION_TEMPLATES, 0, vec![], None, "access-catalog");
            return;
        }
        if self.workspace.resource == "backup" {
            self.refresh_recovery();
            return;
        }
        if self.is_office() {
            self.refresh_office();
            return;
        }
        if self.workspace.root == "attachments" {
            self.refresh_attachments();
            return;
        }
        if self.refresh_business_profile() {
            return;
        }
        if matches!(self.workspace.root, "people" | "directory") {
            self.refresh_personnel();
            return;
        }
        if self.workspace.root == "companies" {
            self.request(GET_ORGANIZATION_DIRECTORY, 0, vec![], None, "organization");
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        self.workspace.search = app.get_search().to_string();
        app.set_selected_id(0);
        if self.workspace.resource == "jobs" {
            self.last_job_refresh = std::time::Instant::now();
        }
        app.set_actions(model(vec![]));
        let resource = catalog::resource(self.workspace.resource);
        app.set_can_create(
            resource.is_some_and(|r| self.can(r.create)) && self.workspace.resource != "directory",
        );
        app.set_can_edit(resource.is_some_and(|r| self.can(r.update)));
        app.set_can_delete(
            resource.is_some_and(|r| r.delete.is_some_and(|operation| self.can(operation))),
        );
        let Some(operation) = workspace::read_operation(self.workspace.resource) else {
            self.error("此功能尚未完成原生迁移。");
            return;
        };
        let reply = if self.workspace.resource == "settings" {
            "settings".into()
        } else {
            format!("list:{}", self.workspace.resource)
        };
        let mut query = vec![
            ("pageNumber", self.workspace.page.max(1).to_string()),
            ("pageSize", "50".into()),
            ("keyword", self.workspace.search.clone()),
        ];
        if self.workspace.resource == "jobs" {
            let statuses = [
                "",
                "Running",
                "Canceling",
                "Succeeded",
                "Failed",
                "Canceled",
            ];
            query.push((
                "status",
                statuses
                    .get(app.get_job_status_index() as usize)
                    .unwrap_or(&"")
                    .to_string(),
            ));
        }
        if let Some(business) = &self.business {
            query.push((business.relation(), business.id().to_string()));
        }
        if self.workspace.resource == "opportunities" {
            let stage = ui
                .global::<crate::Sales>()
                .get_stage_filter()
                .checked_sub(1)
                .and_then(|index| export_doc_domain::sales::STAGES.get(index as usize))
                .copied()
                .unwrap_or("");
            query.push(("stage", stage.into()));
        }
        if self.workspace.resource == "crm-follow-ups" {
            let filter = ui.global::<crate::Crm>();
            query.push((
                "includeCompleted",
                filter.get_include_completed().to_string(),
            ));
            if self.business.is_none() {
                if let Some((id, _)) =
                    filter
                        .get_customer_index()
                        .checked_sub(1)
                        .and_then(|index| {
                            self.lookups
                                .get("crmCustomerId")
                                .and_then(|rows| rows.get(index as usize))
                        })
                {
                    query.push(("crmCustomerId", id.to_string()));
                }
            }
        }
        if ["crm-customers", "suppliers"].contains(&self.workspace.resource) {
            let choices = if self.workspace.resource == "suppliers" {
                export_doc_domain::party::SUPPLIER_STATUSES
            } else {
                export_doc_domain::crm::CUSTOMER_STATUSES
            };
            if let Some(status) = ui
                .global::<crate::Crm>()
                .get_status_index()
                .checked_sub(1)
                .and_then(|index| choices.get(index as usize))
            {
                query.push(("status", (*status).into()));
            }
        }
        if self.workspace.resource == "worklist" {
            let source = app
                .get_worklist_source_index()
                .checked_sub(1)
                .and_then(|index| self.worklist_sources.get(index as usize))
                .cloned()
                .unwrap_or_default();
            query.push(("source", source));
            query.push((
                "due",
                match app.get_worklist_due_index() {
                    1 => "Overdue",
                    2 => "Upcoming",
                    3 => "Undated",
                    _ => "All",
                }
                .into(),
            ));
        }
        self.request(operation, 0, query, None, reply);
    }
    pub fn change_page(&mut self, direction: i32) {
        self.workspace.page = (self.workspace.page + i64::from(direction)).max(1);
        self.refresh();
    }
}
