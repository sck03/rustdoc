use super::*;
use export_doc_engine::{contracts, engine::catalog, workspace};
use serde_json::{Value, json};

impl Desktop {
    pub fn edit_record(&mut self, record: Option<Value>) {
        if let Err(error) = self.workspace.edit(record) {
            self.error(error);
            return;
        }
        let Some(editor) = self.workspace.editor.take() else {
            return;
        };
        let Some(resource) = catalog::resource(self.workspace.resource) else {
            return;
        };
        self.form_id = editor.id;
        self.form_operation = Some(if editor.id > 0 {
            resource.update
        } else {
            resource.create
        });
        self.form = Some(FormModel::new(
            editor.schema,
            editor.record,
            self.lookups.clone(),
        ));
        if let (Some(business), Some(form)) = (&self.business, &mut self.form) {
            if self.workspace.resource != business.resource {
                let relation = business.relation();
                if self.form_id == 0 {
                    form.value[relation] = json!(business.id());
                }
                form.baseline = form.value.clone();
                if form.schema["properties"].get(relation).is_some() {
                    form.schema["properties"][relation]["readOnly"] = json!(true);
                }
            }
        }
        self.form_reply = "form-saved".into();
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(
                format!(
                    "{} · {}",
                    if self.form_id > 0 { "编辑" } else { "新建" },
                    resource.title
                )
                .into(),
            );
            app.set_form_open(true);
        }
        self.prepare_business_form();
    }
    pub fn sync_form(&mut self) {
        if let (Some(ui), Some(form)) = (self.ui.upgrade(), &self.form) {
            let app = ui.global::<App>();
            app.set_fields(model(if self.workspace.resource == "settings" {
                form.fields_for(&crate::settings_model::schema(&self.settings_category))
                    .into_iter()
                    .filter(|field| field.kind != "heading")
                    .collect()
            } else {
                form.fields()
            }));
            app.set_form_error(form.error.clone().into());
            let sections = if self.workspace.resource == "settings" {
                crate::form_sections::build(
                    form,
                    &crate::settings_model::sections(&self.settings_category),
                    &mut self.disclosure_state,
                )
            } else if self.form_reply == "office-saved" {
                crate::office_model::sections(
                    form,
                    self.form_operation.unwrap(),
                    &mut self.disclosure_state,
                )
            } else if self.form_operation == Some(CREATE_PERSONNEL)
                || self.form_operation == Some(UPDATE_PERSONNEL)
            {
                crate::personnel_model::sections(form, &mut self.disclosure_state)
            } else if self.form_reply == "organization-saved" {
                crate::form_sections::complete(
                    form,
                    if self.form_operation == Some(CREATE_ORGANIZATION_DEPARTMENT)
                        || self.form_operation == Some(UPDATE_ORGANIZATION_DEPARTMENT)
                    {
                        "departments"
                    } else {
                        "companies"
                    },
                    &mut self.disclosure_state,
                )
            } else if catalog::resource(self.workspace.resource).is_some_and(|resource| {
                self.form_operation == Some(resource.create)
                    || self.form_operation == Some(resource.update)
            }) {
                crate::form_sections::complete(
                    form,
                    self.workspace.resource,
                    &mut self.disclosure_state,
                )
            } else {
                vec![]
            };
            app.set_form_sections(model(sections));
        }
    }
    pub fn toggle_form_section(&mut self, key: &str) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let current = [
            app.get_form_sections(),
            app.get_invoice_sections(),
            app.get_payment_sections(),
            app.get_person_sections(),
            app.get_business_sections(),
        ]
        .into_iter()
        .flat_map(|sections| sections.iter().collect::<Vec<_>>())
        .find(|section| section.key == key);
        if let Some(section) = current {
            self.disclosure_state.insert(key.into(), !section.expanded);
            if key.starts_with("invoice.") {
                self.sync_invoice_fields();
            } else if key.starts_with("payment.") {
                self.sync_payment(true);
            } else if key.starts_with("person.") && !app.get_form_open() {
                self.sync_personnel();
            } else if app.get_page() == "business" && !app.get_form_open() {
                self.sync_business();
            } else {
                self.sync_form();
            }
        }
    }
    pub fn field_edit(&mut self, key: &str, value: &str) {
        if let Some(form) = &mut self.form {
            if let Err(error) = form.edit(key, value) {
                form.error = error;
            }
        }
        if let (Some(ui), Some(form)) = (self.ui.upgrade(), &self.form) {
            ui.global::<App>().set_form_error(form.error.clone().into());
        }
    }
    pub fn settings_category(&mut self, key: &str) {
        if crate::settings_model::CATEGORIES
            .iter()
            .any(|(category, _)| *category == key)
        {
            self.settings_category = key.into();
            if let Some(ui) = self.ui.upgrade() {
                ui.global::<App>().set_settings_category(key.into());
            }
            self.sync_form();
        }
    }
    pub fn field_select(&mut self, key: &str, index: usize) {
        if let Some(form) = &mut self.form {
            if let Err(error) = form.choose(key, index) {
                form.error = error;
            }
        }
        self.sync_form();
        if key == "crmCustomerId"
            && self.form_operation.is_some_and(|operation| {
                [CREATE_CRM_FOLLOW_UP, TRANSFER_CRM_FOLLOW_UP].contains(&operation)
            })
        {
            if let Some(form) = &mut self.form {
                form.value["crmContactId"] = Value::Null;
            }
            self.refresh_follow_up_contacts();
        }
    }
    pub fn array_action(&mut self, key: &str, action: &str) {
        if let Some(form) = &mut self.form {
            if let Err(error) = form.array_action(key, action) {
                form.error = error;
            }
        }
        self.sync_form();
    }
    pub fn save_form(&mut self) {
        let Some(form) = &mut self.form else {
            return;
        };
        if let Err(error) = form.validate() {
            form.error = error;
            self.sync_form();
            return;
        }
        let body = if self.form_operation == Some(UPDATE_SETTINGS) {
            json!({"settings":form.value,"updateSecrets":self.ui.upgrade().is_some_and(|ui|ui.global::<App>().get_settings_update_secrets())})
        } else {
            form.value.clone()
        };
        let Some(operation) = self.form_operation else {
            return;
        };
        let body = if self.form_reply == "office-saved" {
            match self
                .user
                .as_ref()
                .ok_or_else(|| "请重新登录。".to_owned())
                .and_then(|user| crate::office_model::request_body(form, operation, user))
            {
                Ok(value) => value,
                Err(cause) => {
                    form.error = cause;
                    self.sync_form();
                    return;
                }
            }
        } else {
            body
        };
        if operation == UPLOAD_BUSINESS_ATTACHMENT {
            self.save_attachment_upload(body);
            return;
        }
        let body = match self.crm_request_body(body) {
            Ok(body) => body,
            Err(cause) => {
                if let Some(form) = &mut self.form {
                    form.error = cause;
                }
                self.sync_form();
                return;
            }
        };
        if self.form_reply == "delete" {
            self.confirm(Pending::Delete, "删除后不能撤销，确认永久删除选中的记录？");
            return;
        }
        self.request(
            operation,
            self.form_id,
            vec![],
            Some(body),
            self.form_reply.clone(),
        );
    }
    pub fn cancel_form(&mut self) {
        if self.form_operation == Some(UPLOAD_BUSINESS_ATTACHMENT) {
            self.attachments.source = None;
        }
        self.form = None;
        self.form_operation = None;
        self.workspace.editor = None;
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_form_open(false);
            ui.global::<App>().set_form_sections(model(vec![]));
        }
    }
    pub fn record_action(&mut self, key: &str) {
        if key == GET_CUSTOMS_COO_DOCUMENT.id || key == GET_AGENT_CONSIGNMENT_DOCUMENT.id {
            if let Some(id) = self
                .workspace
                .selected
                .as_ref()
                .and_then(|r| r["id"].as_i64())
            {
                self.open_single_window_invoice(
                    id,
                    if key == GET_CUSTOMS_COO_DOCUMENT.id {
                        export_doc_domain::single_window::Business::Coo
                    } else {
                        export_doc_domain::single_window::Business::Acd
                    },
                );
            }
            return;
        }
        if self.workspace.resource == "opportunities" {
            if let Some(record) = self.workspace.selected.clone() {
                self.sales.open(Some(record), None, self.lookups.clone());
                if let Some(ui) = self.ui.upgrade() {
                    ui.global::<App>().set_page("sales".into());
                    ui.global::<crate::Sales>().set_view("history".into());
                }
                self.sync_sales();
                self.sales_action(
                    if key == ARCHIVE_SALES_OPPORTUNITY.id {
                        "archive"
                    } else {
                        "history"
                    },
                    "",
                );
            }
            return;
        }
        if key == "payment-report" {
            self.open_payment_report();
            return;
        }
        let Some(operation) = ALL_OPERATIONS.iter().find(|op| op.id == key).copied() else {
            return;
        };
        let Some(record) = &self.workspace.selected else {
            return;
        };
        let mut body = contracts::object(operation.id, true);
        if !body.is_object() {
            body = json!({});
        }
        body["expectedVersion"] = record["versionNumber"].clone();
        body["rowVersion"] = record["rowVersion"].clone();
        self.form_id = record["id"].as_i64().unwrap_or(0);
        self.form_operation = Some(operation);
        self.form_reply = "form-saved".into();
        self.form = Some(FormModel::new(
            contracts::request(operation.id),
            body,
            self.lookups.clone(),
        ));
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(
                workspace::actions(self.workspace.resource, record)
                    .iter()
                    .find(|(op, _)| op.id == key)
                    .map(|(_, label)| *label)
                    .unwrap_or("业务操作")
                    .into(),
            );
            app.set_form_open(true);
        }
        self.prepare_business_form();
    }
    pub fn delete_record(&mut self) {
        let Some(resource) = catalog::resource(self.workspace.resource) else {
            return;
        };
        let Some(operation) = resource.delete else {
            return;
        };
        let Some(record) = &self.workspace.selected else {
            return;
        };
        self.form_id = record["id"].as_i64().unwrap_or(0);
        self.form_operation = Some(operation);
        self.form_reply = "delete".into();
        let body = json!({"expectedVersion":record["versionNumber"],"rowVersion":record["rowVersion"],"reason":"","note":""});
        let schema = json!({"type":"object","properties":{"reason":{"type":"string"}}});
        self.form = Some(FormModel::new(&schema, body, self.lookups.clone()));
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(format!("删除{} · 填写原因", resource.title).into());
            app.set_form_open(true);
        }
        self.sync_form();
    }
    pub fn delete_now(&mut self) {
        let (Some(operation), Some(form)) = (self.form_operation, self.form.as_ref()) else {
            return;
        };
        if form.value["reason"]
            .as_str()
            .unwrap_or("")
            .trim()
            .is_empty()
        {
            self.error("请填写删除原因。");
            return;
        }
        let mut body = form.value.clone();
        body["note"] = body["reason"].clone();
        self.request(
            operation,
            self.form_id,
            vec![
                ("expectedVersion", body["expectedVersion"].to_string()),
                (
                    "rowVersion",
                    body["rowVersion"].as_str().unwrap_or("").into(),
                ),
            ],
            Some(body),
            if self.workspace.root == "companies" {
                "organization-deleted"
            } else if self.workspace.root == "people" {
                "personnel-deleted"
            } else if self
                .business
                .as_ref()
                .is_some_and(|business| business.resource == self.workspace.resource)
            {
                "business-deleted"
            } else {
                "form-saved"
            },
        );
    }
}

impl Desktop {
    pub fn settings_secrets(&self, value: &serde_json::Value) {
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_settings_update_secrets(false);
            let names = [
                ("emailPasswordSet", "邮件"),
                ("webDavPasswordSet", "远程备份"),
                ("aiApiKeySet", "AI"),
                ("postgreSqlPasswordSet", "数据库"),
            ];
            app.set_settings_secret_summary(
                names
                    .iter()
                    .filter(|(key, _)| value["secrets"][*key] == true)
                    .map(|(_, label)| format!("{label}凭证已保存"))
                    .collect::<Vec<_>>()
                    .join(" · ")
                    .into(),
            );
        }
    }
}
