mod access;
mod attachments;
mod audit;
mod bindings;
mod business;
mod crm;
mod designer;
mod document_packages;
mod exchange;
mod files;
mod forms;
mod hs;
mod hs_files;
mod invoice;
mod invoice_files;
mod letter_of_credit;
mod mail;
mod mail_templates;
mod maintenance;
mod navigation;
mod ocr;
mod office;
mod organization;
mod packing;
mod party_files;
mod payment;
mod pdf_merge;
mod personnel;
mod products;
mod query;
mod records;
mod recovery;
mod reports;
mod responses;
mod sales;
mod single_window;
mod single_window_actions;
mod single_window_responses;
mod single_window_tools;
mod supplier_overview;
mod workspace;

use crate::{
    App, AppWindow,
    form_model::{FormModel, Lookups},
    model,
    worker::{Event, Task, Work},
};
use export_doc_domain::invoice_grid::InvoiceGrid;
use export_doc_engine::{
    api::ApiClient, designer::Design, generated_api::*, history::History, invoice::InvoiceDraft,
    paths::RuntimePaths, workspace::Workspace,
};
use slint::{ComponentHandle, Model};
use std::{cell::RefCell, collections::BTreeSet, rc::Rc, sync::Arc};

pub struct Desktop {
    pub ui: slint::Weak<AppWindow>,
    pub client: ApiClient,
    pub user: Option<ApiUserDto>,
    pub platform: crate::platform::DesktopPlatform,
    pub paths: RuntimePaths,
    pub task: Option<Task>,
    pub workspace: Workspace,
    pub form: Option<FormModel>,
    pub form_operation: Option<Operation>,
    pub form_reply: String,
    pub form_id: i64,
    pub lookups: Lookups,
    pub grid: InvoiceGrid,
    pub invoice_form: FormModel,
    pub pending_invoice: Option<InvoiceDraft>,
    pub pending_product: Option<ApiInvoiceItemDto>,
    pub first_row: usize,
    pub visible_rows: usize,
    pub templates: Vec<ApiReportTemplateDto>,
    pub products: Vec<serde_json::Value>,
    pub pdf: Option<Arc<Vec<u8>>>,
    pub report_payment: Option<(i64, String)>,
    pub payment_form: Option<FormModel>,
    pub payment_payee: Option<serde_json::Value>,
    pub design: Design,
    pub design_history: History<Design>,
    pub template: Option<ApiUserReportTemplateDto>,
    pub selected_element: String,
    pub bindings: Vec<(String, String)>,
    pub expanded: BTreeSet<String>,
    pub nav_search: String,
    pub pending_confirm: Option<Pending>,
    pub close_when_idle: bool,
    pub load_lookups: bool,
    pub settings_category: String,
    pub disclosure_state: crate::form_sections::DisclosureState,
    pub organization: crate::organization_model::OrganizationModel,
    pub pending_org_code: Option<String>,
    pub personnel: crate::personnel_model::PersonnelModel,
    pub office: crate::office_model::OfficeModel,
    pub backups: Vec<ApiBackupItemDto>,
    pub business: Option<crate::business_model::BusinessModel>,
    pub worklist_sources: Vec<String>,
    pub worklist_items: Vec<WorklistItem>,
    pub pending_new_invoice: bool,
    pub attachments: crate::attachment_model::AttachmentModel,
    pub pending_open: Option<(&'static str, i64)>,
    pub import_preview: Option<ApiExcelImportPreviewResponse>,
    pub last_job_refresh: std::time::Instant,
    pub access: crate::access_model::AccessModel,
    pub sales: crate::sales_model::SalesModel,
    pub party_preview: Option<serde_json::Value>,
    pub packing: crate::packing_model::PackingModel,
    pub hs: crate::hs_model::HsModel,
    pub mail: crate::mail_model::MailModel,
    pub ocr: crate::ocr_model::OcrModel,
    pub single_window: crate::single_window_model::SingleWindowModel,
    pub query_filters: Option<serde_json::Value>,
    pub transfer_source: Option<(String, Arc<Vec<u8>>)>,
    pub pdf_sources: Vec<std::path::PathBuf>,
    pub document_package: crate::document_package_model::DocumentPackageModel,
    pub recovery: crate::recovery_model::RecoveryModel,
}
#[derive(Clone)]
pub enum Pending {
    Navigate(String),
    Logout,
    Close,
    Delete,
    ReloadPayment,
    PersonnelImage(String),
    AttachmentMutation(Operation, serde_json::Value),
    Maintenance(Operation, serde_json::Value),
    AccessDelete(i64, i64),
    AuditMaintenance(Operation, serde_json::Value),
    SalesAction(String, String),
    ProductSave(Box<ApiProductDto>),
    AttachmentCategoryDelete(i64, i64),
    PackingAction(String, i32),
    HsAction(String, i32),
    MailAction(String, i32),
    SingleWindowAction(String, i32),
    InvoicePackageImport,
    RecoveryAction(Operation, serde_json::Value, String),
    RecoveryRestore(serde_json::Value),
}
impl Desktop {
    pub fn new(ui: &AppWindow, client: ApiClient, paths: RuntimePaths) -> Rc<RefCell<Self>> {
        let draft = InvoiceDraft::new(&chrono::Local::now().date_naive().to_string());
        let invoice_form = FormModel::new(
            export_doc_engine::contracts::schema("ApiInvoiceDetailDto"),
            serde_json::to_value(&draft.header).expect("invoice DTO"),
            Lookups::new(),
        );
        let state = Rc::new(RefCell::new(Self {
            ui: ui.as_weak(),
            client,
            user: None,
            platform: Default::default(),
            paths,
            task: None,
            workspace: Workspace::default(),
            form: None,
            form_operation: None,
            form_reply: String::new(),
            form_id: 0,
            lookups: Lookups::new(),
            grid: InvoiceGrid::new(draft, 0),
            invoice_form,
            pending_invoice: None,
            pending_product: None,
            first_row: 0,
            visible_rows: 24,
            templates: vec![],
            products: vec![],
            pdf: None,
            report_payment: None,
            payment_form: None,
            payment_payee: None,
            design: Design::invoice(),
            design_history: History::new(60),
            template: None,
            selected_element: String::new(),
            bindings: vec![],
            expanded: BTreeSet::from(["documents".into()]),
            nav_search: String::new(),
            pending_confirm: None,
            close_when_idle: false,
            load_lookups: false,
            settings_category: "runtime".into(),
            disclosure_state: Default::default(),
            organization: Default::default(),
            pending_org_code: None,
            personnel: Default::default(),
            office: Default::default(),
            backups: vec![],
            business: None,
            worklist_sources: vec![],
            worklist_items: vec![],
            pending_new_invoice: false,
            attachments: Default::default(),
            pending_open: None,
            import_preview: None,
            last_job_refresh: std::time::Instant::now(),
            access: Default::default(),
            sales: Default::default(),
            party_preview: None,
            packing: Default::default(),
            hs: Default::default(),
            mail: Default::default(),
            ocr: Default::default(),
            single_window: Default::default(),
            query_filters: None,
            transfer_source: None,
            pdf_sources: vec![],
            document_package: Default::default(),
            recovery: Default::default(),
        }));
        state.borrow().sync_navigation();
        bindings::bind(ui, state.clone());
        state
    }
    pub fn start(&mut self, work: Work) {
        if self.task.is_some() {
            self.error("请等待当前操作完成。");
            return;
        }
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_busy(true);
            ui.global::<App>().set_status("正在处理…".into());
            ui.global::<App>().set_error("".into());
        }
        self.task = Some(Task::spawn(self.client.clone(), self.paths.clone(), work));
    }
    pub fn allowed(&self, operation: Operation) -> bool {
        let Some(user) = &self.user else {
            return false;
        };
        let grants = user
            .capabilities
            .permissions
            .iter()
            .map(|grant| export_doc_domain::permissions::Grant {
                resource_key: grant.resource_key.clone(),
                action: grant.action.clone(),
                data_scope: grant.data_scope.clone(),
            })
            .collect::<Vec<_>>();
        export_doc_domain::permissions::allows_operation(
            &grants,
            user.capabilities.can_manage_settings,
            operation,
            &[],
        )
    }
    pub fn can(&self, operation: Operation) -> bool {
        self.client.supports(operation) && self.allowed(operation)
    }
    pub fn request(
        &mut self,
        operation: Operation,
        id: i64,
        mut query: Vec<(&'static str, String)>,
        mut body: Option<serde_json::Value>,
        reply: impl Into<String>,
    ) {
        let mut parameters = crate::worker::parameters(operation, id);
        if let Some(business) = &self.business {
            for (key, value) in &mut parameters {
                if (*key == "customerId" && business.resource == "crm-customers")
                    || (*key == "supplierId" && business.resource == "suppliers")
                {
                    *value = business.id().to_string();
                }
            }
        }
        if operation.path.contains("{code}") {
            let code = self
                .workspace
                .selected
                .as_ref()
                .and_then(|record| record["code"].as_str())
                .or_else(|| body.as_ref().and_then(|value| value["code"].as_str()));
            let Some(code) = code.filter(|value| !value.is_empty()) else {
                self.error("缺少记录代码，请重新选择记录。");
                return;
            };
            for (key, value) in &mut parameters {
                if *key == "code" {
                    *value = code.into();
                }
            }
        }
        let contract = &export_doc_engine::contracts::contract()["operations"][operation.id];
        if contract["request"].is_null() {
            if let Some(value) = &body {
                for parameter in contract["parameters"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .filter(|parameter| parameter["in"] == "query")
                {
                    if let Some(key) = parameter["name"].as_str() {
                        if let Some(item) = value.get(key).filter(|item| !item.is_null()) {
                            if !query.iter().any(|(name, _)| *name == key) {
                                query.push((
                                    key,
                                    item.as_str()
                                        .map(str::to_owned)
                                        .unwrap_or_else(|| item.to_string()),
                                ));
                            }
                        }
                    }
                }
            }
            body = None;
        }
        self.start(Work::Request {
            operation,
            parameters,
            query,
            body,
            reply: reply.into(),
        });
    }
    pub fn error(&self, error: impl Into<String>) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_error(error.into().into());
        }
    }
    pub fn status(&self, text: impl Into<String>) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_status(text.into().into());
        }
    }
    pub fn poll(&mut self) {
        self.poll_packing();
        let result = self
            .task
            .as_ref()
            .and_then(|task| match task.events.try_recv() {
                Ok(event) => Some(event),
                Err(std::sync::mpsc::TryRecvError::Disconnected) => {
                    Some(Err("后台操作提前退出。".into()))
                }
                Err(_) => None,
            });
        if let Some(result) = result {
            if let Some(mut task) = self.task.take() {
                task.finish();
            }
            if let Some(ui) = self.ui.upgrade() {
                ui.global::<App>().set_busy(false);
            }
            match result {
                Ok(event) => self.event(event),
                Err(error) => {
                    self.pending_invoice = None;
                    self.error(error);
                    self.status("操作未完成，草稿已保留");
                }
            }
            if self.close_when_idle && self.task.is_none() {
                let _ = slint::quit_event_loop();
            }
        } else if self.task.is_none() && self.load_lookups {
            self.load_lookups = false;
            self.start(Work::Lookups);
        } else if self.task.is_none()
            && self.workspace.resource == "jobs"
            && self.last_job_refresh.elapsed().as_secs() >= 2
            && self.workspace.items().iter().any(|job| {
                matches!(
                    job["status"].as_str(),
                    Some("Running" | "Queued" | "Pending" | "Canceling")
                )
            })
        {
            self.refresh();
        }
    }
    fn event(&mut self, event: Event) {
        match event {
            Event::InvoicePackage(name, bytes, preview) => {
                self.invoice_package_loaded(name, bytes, preview)
            }
            Event::OcrImage(image) => self.ocr_image_loaded(image),
            Event::Image(reply, frame) => {
                if let Some(ui) = self.ui.upgrade() {
                    let pixels = slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(
                        &frame.rgba,
                        frame.width,
                        frame.height,
                    );
                    if reply == "personnel" {
                        ui.global::<App>()
                            .set_person_image(slint::Image::from_rgba8(pixels));
                    } else if reply == "attachment" {
                        ui.global::<App>()
                            .set_attachment_image(slint::Image::from_rgba8(pixels));
                    }
                }
                self.status("图片已读取");
            }
            Event::Login(client, user) => {
                self.client = client;
                if let Some(ui) = self.ui.upgrade() {
                    ui.global::<App>().set_logged_in(true);
                    ui.global::<App>()
                        .set_user_name(user.full_name.clone().into());
                }
                self.user = Some(user);
                self.load_lookups = true;
                let initial = if self.allowed(GET_DASHBOARD) {
                    "dashboard"
                } else if self.allowed(GET_CRM_DASHBOARD) {
                    "sales-dashboard"
                } else if self.allowed(LIST_PERSONNEL) {
                    "people"
                } else {
                    "worklist"
                };
                self.navigate_now(initial);
                self.status("登录成功");
            }
            Event::Lookups(lookups) => {
                self.lookups = lookups;
                self.sync_business_filters();
                self.invoice_form.lookups = self.lookups.clone();
                if let Some(form) = &mut self.form {
                    form.lookups = self.lookups.clone();
                }
                if let Some(form) = &mut self.sales.form {
                    form.lookups = self.lookups.clone();
                    self.sync_sales();
                }
                self.sync_invoice_fields();
                self.status("资料目录已就绪");
            }
            Event::Data(reply, value) => {
                self.status("就绪");
                self.data(&reply, value);
            }
            Event::Invoice(invoice) => self.invoice_saved(*invoice),
            Event::Pdf(bytes, page) => {
                self.pdf = Some(bytes);
                self.show_pdf(page);
                self.status("PDF 已生成");
            }
            Event::Page(page) => self.show_pdf(page),
            Event::SavedFile(path) => {
                if let Some(path) = path {
                    self.status(format!("已导出：{}", path.display()));
                }
            }
        }
    }
    pub fn close(&mut self) {
        if self.has_unsaved() {
            self.confirm(
                Pending::Close,
                "存在尚未保存的修改，确认退出并放弃这些修改？",
            );
        } else {
            self.close_now();
        }
    }
    pub fn close_now(&mut self) {
        if let Some(task) = &self.task {
            task.cancel();
            self.close_when_idle = true;
            self.status("正在取消操作并关闭…");
        } else {
            let _ = slint::quit_event_loop();
        }
    }
    pub fn has_unsaved(&self) -> bool {
        let pending_cell = self.ui.upgrade().is_some_and(|ui| {
            let app = ui.global::<App>();
            let active = self.grid.selection.active;
            app.get_page() == "invoice-edit"
                && app.get_invoice_tab() == 1
                && app.get_cell_value().as_str() != self.grid.display(active.row, active.column)
        });
        pending_cell
            || self.access.dirty()
            || self.sales.dirty()
            || self.hs.dirty()
            || self.mail.dirty()
            || self.single_window.dirty()
            || (self.workspace.root == "container-projects" && self.packing.dirty())
            || self.payment_dirty()
            || self.grid.dirty()
            || !self.invoice_form.error.is_empty()
            || self
                .form
                .as_ref()
                .is_some_and(|form| form.value != form.baseline || !form.error.is_empty())
            || self.design_history.can_undo()
    }
    pub fn confirm(&mut self, pending: Pending, message: &str) {
        self.pending_confirm = Some(pending);
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_confirm_message(message.into());
            ui.global::<App>().set_confirm_open(true);
        }
    }
    pub fn confirmation(&mut self, yes: bool) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_confirm_open(false);
        }
        let pending = self.pending_confirm.take();
        if !yes {
            return;
        }
        match pending {
            Some(Pending::InvoicePackageImport) => self.import_invoice_package(),
            Some(Pending::MailAction(action, index)) => self.mail_confirmed(&action, index),
            Some(Pending::SingleWindowAction(action, index)) => {
                self.single_window_confirmed(&action, index)
            }
            Some(Pending::HsAction(action, index)) => self.hs_confirmed(&action, index),
            Some(Pending::PackingAction(action, index)) => self.packing_confirmed(&action, index),
            Some(Pending::AttachmentCategoryDelete(id, version)) => self.request(
                DELETE_BUSINESS_ATTACHMENT_CATEGORY,
                id,
                vec![("expectedVersion", version.to_string())],
                None,
                "attachment-category-deleted",
            ),
            Some(Pending::ProductSave(product)) => self.save_product(*product),
            Some(Pending::SalesAction(action, value)) => self.sales_confirmed(&action, &value),
            Some(Pending::AuditMaintenance(operation, body)) => {
                self.request(operation, 0, vec![], Some(body), "audit-maintained");
            }
            Some(Pending::AccessDelete(id, version)) => {
                self.request(
                    DELETE_PERMISSION_TEMPLATE,
                    id,
                    vec![("expectedVersion", version.to_string())],
                    None,
                    "access-deleted",
                );
            }
            Some(Pending::Navigate(key)) => {
                self.form = None;
                self.grid.history.clear();
                self.grid = InvoiceGrid::new(self.grid.draft.clone(), 0);
                self.design_history.clear();
                self.navigate_now(&key);
            }
            Some(Pending::Logout) => self.logout_now(),
            Some(Pending::Close) => self.close_now(),
            Some(Pending::Delete) => self.delete_now(),
            Some(Pending::ReloadPayment) => self.reload_payment(),
            Some(Pending::PersonnelImage(kind)) => self.delete_personnel_image(&kind),
            Some(Pending::AttachmentMutation(operation, body)) => {
                self.mutate_attachment(operation, body)
            }
            Some(Pending::Maintenance(operation, body)) => self.mutate_maintenance(operation, body),
            Some(Pending::RecoveryAction(operation, body, reply)) => {
                self.request(operation, 0, vec![], Some(body), reply);
            }
            Some(Pending::RecoveryRestore(body)) => self.request(
                RESTORE_DISASTER_RECOVERY_PACKAGE,
                0,
                vec![],
                Some(body),
                "recovery:restored",
            ),
            None => {}
        }
    }
    pub fn logout(&mut self) {
        if self.has_unsaved() {
            self.confirm(Pending::Logout, "尚有未保存修改，确认退出登录？");
        } else {
            self.logout_now();
        }
    }
    fn logout_now(&mut self) {
        self.request(LOGOUT, 0, vec![], Some(serde_json::json!({})), "logout");
    }
}
