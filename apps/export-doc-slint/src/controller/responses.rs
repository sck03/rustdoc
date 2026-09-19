use super::*;
use crate::PageTab;
use export_doc_engine::{contracts, workspace};
use serde_json::{Value, json};

impl Desktop {
    pub fn data(&mut self, reply: &str, value: Value) {
        if reply == "invoice-package-imported" {
            self.invoice_package_imported(value);
            return;
        }
        if reply.starts_with("credit:") {
            self.credit_loaded(reply, value);
            return;
        }
        if reply.starts_with("sw:") {
            self.single_window_loaded(reply, value);
            return;
        }
        if reply.starts_with("ocr:") {
            self.ocr_loaded(reply, value);
            return;
        }
        if reply.starts_with("mail:") {
            self.mail_loaded(reply, value);
            return;
        }
        if reply.starts_with("hs:") {
            self.hs_loaded(reply, value);
            return;
        }
        if reply.starts_with("packing:") {
            self.packing_loaded(reply, value);
            return;
        }
        if reply.starts_with("recovery:") {
            self.recovery_loaded(reply, value);
            return;
        }
        if reply == "followup-record" {
            if let Some(row) = value["items"]
                .as_array()
                .and_then(|rows| rows.first())
                .cloned()
            {
                self.edit_record(Some(row));
            } else {
                self.error("跟进已删除或不在当前账号的数据范围内。");
            }
            return;
        }
        if reply == "product-save-candidates" {
            self.product_save_candidates(value);
            return;
        }
        if reply == "product-saved" {
            self.load_lookups = true;
            self.status("商品库已保存，发票明细草稿保留。");
            return;
        }
        if reply == "followup-contacts" {
            self.follow_up_contacts_loaded(value);
            return;
        }
        if reply == "list:sales-dashboard" {
            self.crm_dashboard_loaded(value);
            return;
        }
        if reply == "list:supplier-overview" {
            self.supplier_overview_loaded(value);
            return;
        }
        if reply.starts_with("party:") {
            self.party_file_loaded(reply, value);
            return;
        }
        if reply.starts_with("sales:") {
            self.sales_loaded(reply, value);
            return;
        }
        match reply {
            "exchange-rates" | "exchange-currencies" => {
                self.exchange_loaded(reply, value);
                return;
            }
            "audit-maintained" => {
                self.refresh_audit();
                return;
            }
            "audit-exported" => {
                self.status(value["message"].as_str().unwrap_or("审计日志已导出"));
                return;
            }
            "access-catalog" => {
                self.access_loaded(value);
                return;
            }
            "access-saved" => {
                self.access.open(Some(value), false);
                self.request(LIST_PERMISSION_TEMPLATES, 0, vec![], None, "access-catalog");
                self.load_lookups = true;
                return;
            }
            "access-deleted" => {
                self.access = Default::default();
                self.request(LIST_PERMISSION_TEMPLATES, 0, vec![], None, "access-catalog");
                self.load_lookups = true;
                return;
            }
            _ => {}
        }
        if reply == "list:backup" || reply == "backup-list" {
            self.backups_loaded(value);
            return;
        }
        if self.office_data(reply, &value) {
            return;
        }
        if reply == "list:worklist" {
            self.worklist_loaded(value);
            return;
        }
        if reply == "list:dashboard" {
            self.dashboard_loaded(value);
            return;
        }
        if let Some(key) = reply.strip_prefix("list:") {
            if key == self.workspace.resource {
                let selected_job = if key == "jobs" {
                    self.workspace
                        .selected
                        .as_ref()
                        .and_then(|job| job["jobId"].as_str())
                        .map(str::to_owned)
                } else {
                    None
                };
                self.workspace.payload = Some(value);
                self.workspace.loaded = true;
                self.sync_records();
                if let Some(id) = selected_job {
                    if let Some(index) = self
                        .workspace
                        .items()
                        .iter()
                        .position(|job| job["jobId"] == id)
                    {
                        self.select_record(index as i64 + 1);
                    } else {
                        self.workspace.selected = None;
                        if let Some(ui) = self.ui.upgrade() {
                            ui.global::<App>().set_job_detail("".into());
                        }
                    }
                }
                if self.pending_new_invoice && key == "invoices" {
                    self.pending_new_invoice = false;
                    self.new_record();
                    return;
                }
                if let Some((resource, id)) = self.pending_open.take() {
                    if resource == self.workspace.resource {
                        self.open_record(id);
                    } else {
                        self.pending_open = Some((resource, id));
                        self.open_tab(resource);
                    }
                }
            }
        } else if let Some(key) = reply.strip_prefix("edit:") {
            if key == self.workspace.resource {
                self.edit_record(Some(value));
            }
        } else {
            match reply {
                "backup-created" => {
                    self.request(LIST_DATABASE_BACKUPS, 0, vec![], None, "backup-list");
                    self.status("已创建并校验数据库备份");
                }
                "backup-cleaned" => {
                    let message = value["message"]
                        .as_str()
                        .unwrap_or("旧备份清理完成")
                        .to_owned();
                    self.backups_loaded(value);
                    self.status(message);
                }
                "backup-restored" => {
                    self.data("logout", json!({}));
                    self.status("数据库已还原，请重新登录");
                }
                "attachments" => self.attachments_loaded(value),
                "attachment-detail" => self.attachment_detail(value),
                "attachment-uploaded" => {
                    let id = value["id"].as_i64().unwrap_or(0);
                    self.cancel_form();
                    self.request(
                        GET_BUSINESS_ATTACHMENT,
                        id,
                        vec![],
                        None,
                        "attachment-detail",
                    );
                    self.status("已上传新版本，请核对后确认有效版本");
                }
                "attachment-saved" => {
                    self.cancel_form();
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<App>().set_attachment_note("".into());
                    }
                    self.refresh_attachments();
                }
                "attachment-deleted" => {
                    self.attachments.details = None;
                    self.refresh_attachments();
                }
                "attachment-category-saved" | "attachment-category-deleted" => {
                    if reply == "attachment-category-deleted" {
                        self.attachments.category_id = None;
                    }
                    self.cancel_form();
                    self.attachments.details = None;
                    self.refresh_attachments();
                }
                "attachment-text" => {
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<App>()
                            .set_attachment_text(value.as_str().unwrap_or("").into());
                    }
                }
                "new-invoice-user" => match serde_json::from_value::<ApiUserDto>(value) {
                    Ok(user) => {
                        let invoice = InvoiceDraft::new(&user.business_date);
                        self.user = Some(user);
                        self.open_invoice(invoice);
                    }
                    Err(cause) => self.error(cause.to_string()),
                },
                "business-detail" => self.open_business(value),
                "business-deleted" => {
                    let root = self.workspace.root;
                    self.cancel_form();
                    self.navigate_now(root);
                    self.load_lookups = true;
                }
                "new-personnel-user" => match serde_json::from_value::<ApiUserDto>(value) {
                    Ok(user) => {
                        self.user = Some(user);
                        self.new_personnel();
                    }
                    Err(cause) => self.error(cause.to_string()),
                },
                "personnel-directory" => self.personnel_directory(value),
                "personnel-record" | "personnel-saved" => {
                    self.open_personnel(value);
                    if reply == "personnel-saved" {
                        self.load_lookups = true;
                        self.status("人员档案已保存");
                    }
                }
                "personnel-history" => self.personnel_history(value),
                "personnel-clearance" => self.personnel_clearance(value),
                "personnel-image-saved" => {
                    self.refresh_personnel();
                    self.status("图片已保存");
                }
                "personnel-deleted" => {
                    self.cancel_form();
                    self.personnel_action("back", "");
                    self.load_lookups = true;
                }
                "organization" => self.organization_loaded(value),
                "organization-managers" => self.organization_managers(value),
                "organization-saved" | "organization-deleted" => {
                    self.pending_org_code = value["code"].as_str().map(str::to_owned);
                    self.cancel_form();
                    self.load_lookups = true;
                    self.refresh();
                    self.status(if reply == "organization-deleted" {
                        "目录项已删除"
                    } else {
                        "组织目录已保存"
                    });
                }
                "new-payment-user" => match serde_json::from_value::<ApiUserDto>(value) {
                    Ok(user) => {
                        let payment = export_doc_domain::payment::new(&user.business_date);
                        self.user = Some(user);
                        match payment {
                            Ok(payment) => self.open_payment(json!(payment)),
                            Err(cause) => self.error(cause.to_string()),
                        }
                    }
                    Err(cause) => self.error(cause.to_string()),
                },
                "payment" => self.open_payment(value),
                "payment-reloaded" => self.payment_saved(value, true),
                "payment-saved" => self.payment_saved(value, false),
                "payment-payee" => self.apply_payment_payee(value),
                "payment-options" => self.payment_options(value, false),
                "payment-option-saved" => self.payment_options(value, true),
                "excel-preview" => self.show_import(value),
                "task-action" => self.refresh(),
                "invoice" => match serde_json::from_value::<ApiInvoiceDetailDto>(value) {
                    Ok(invoice) => self.open_invoice(InvoiceDraft::from_dto(invoice)),
                    Err(e) => self.error(e.to_string()),
                },
                "invoice-customer" | "invoice-exporter" => self.apply_party(reply, &value),
                "form-saved" => {
                    self.cancel_form();
                    self.load_lookups = true;
                    self.refresh();
                    self.status("保存成功");
                }
                "settings" => {
                    self.settings_secrets(&value);
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<App>().set_settings_categories(model(
                            crate::settings_model::CATEGORIES
                                .iter()
                                .map(|(key, label)| PageTab {
                                    key: (*key).into(),
                                    label: (*label).into(),
                                })
                                .collect(),
                        ));
                        ui.global::<App>()
                            .set_storage_summary("SQLite · 本机数据 · 配置修改按版本保存".into());
                    }
                    self.form = Some(FormModel::new(
                        contracts::schema("AppSettings"),
                        value["settings"].clone(),
                        self.lookups.clone(),
                    ));
                    self.form_operation = Some(UPDATE_SETTINGS);
                    self.form_id = 0;
                    self.form_reply = "settings-saved".into();
                    self.sync_form();
                }
                "settings-saved" => {
                    self.settings_secrets(&value);
                    if let Some(form) = &mut self.form {
                        form.value = value["settings"].clone();
                        form.baseline = form.value.clone();
                        form.buffers.clear();
                    }
                    self.sync_form();
                    self.status("设置已保存");
                }
                "logout" => {
                    self.ocr = Default::default();
                    self.mail = Default::default();
                    self.hs = Default::default();
                    self.sales = Default::default();
                    self.party_preview = None;
                    self.pending_product = None;
                    self.office = Default::default();
                    self.backups.clear();
                    self.load_lookups = false;
                    self.personnel = Default::default();
                    self.business = None;
                    self.attachments = Default::default();
                    self.worklist_items.clear();
                    self.worklist_sources.clear();
                    self.payment_form = None;
                    self.payment_payee = None;
                    self.user = None;
                    self.cancel_form();
                    self.lookups.clear();
                    self.pdf = None;
                    self.template = None;
                    self.grid = InvoiceGrid::new(
                        InvoiceDraft::new(&chrono::Local::now().date_naive().to_string()),
                        0,
                    );
                    self.design_history.clear();
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<App>().set_logged_in(false);
                        ui.global::<App>().set_error("".into());
                        ui.global::<App>().set_pdf_image(Default::default());
                    }
                }
                "templates" => match serde_json::from_value(value) {
                    Ok(templates) => {
                        self.templates = templates;
                        self.document_package.load(&self.templates);
                        if let Some(ui) = self.ui.upgrade() {
                            ui.global::<App>().set_report_template_index(0);
                            ui.global::<App>().set_report_templates(model(
                                self.templates
                                    .iter()
                                    .map(|t| t.display_name.clone().into())
                                    .collect(),
                            ));
                        }
                        self.sync_document_package();
                    }
                    Err(e) => self.error(e.to_string()),
                },
                "products" => {
                    self.products = value["items"].as_array().cloned().unwrap_or_default();
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<App>().set_products(model(
                            self.products
                                .iter()
                                .map(|p| {
                                    format!(
                                        "{}  {}",
                                        workspace::display(&p["productCode"]),
                                        workspace::display(&p["nameEN"])
                                    )
                                    .into()
                                })
                                .collect(),
                        ));
                        ui.global::<App>().set_product_index(0);
                    }
                }
                "review" => {
                    if let Some(ui) = self.ui.upgrade() {
                        let issues = value["issues"]
                            .as_array()
                            .map(|items| {
                                items
                                    .iter()
                                    .map(|item| workspace::display(&item["message"]))
                                    .collect::<Vec<_>>()
                                    .join("\n")
                            })
                            .unwrap_or_default();
                        ui.global::<App>().set_review_text(if issues.is_empty() {
                            "核对检查完成，未发现阻止保存的问题。".into()
                        } else {
                            issues.into()
                        });
                    }
                }
                "template" => match serde_json::from_value(value) {
                    Ok(template) => self.open_designer(Some(template)),
                    Err(e) => self.error(e.to_string()),
                },
                "template-saved" => match serde_json::from_value(value) {
                    Ok(template) => {
                        self.template = Some(template);
                        self.design_history.clear();
                        if let Some(ui) = self.ui.upgrade() {
                            ui.global::<App>().set_template_saved(true);
                        }
                        self.status("模板已保存");
                    }
                    Err(e) => self.error(e.to_string()),
                },
                "field-catalog" => {
                    match serde_json::from_value::<ApiReportTemplateFieldCatalogResponse>(value) {
                        Ok(catalog) => {
                            self.bindings = export_doc_engine::designer::field_catalog(&catalog)
                                .iter()
                                .map(|f| (f.path.clone(), f.label.clone()))
                                .collect();
                            if let Some(ui) = self.ui.upgrade() {
                                ui.global::<App>().set_binding_fields(model(
                                    self.bindings
                                        .iter()
                                        .map(|(_, l)| l.clone().into())
                                        .collect(),
                                ));
                            }
                        }
                        Err(e) => self.error(e.to_string()),
                    }
                }
                _ => self.status("操作完成"),
            }
        }
    }
}
