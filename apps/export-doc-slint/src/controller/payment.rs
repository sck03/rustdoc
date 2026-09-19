use super::*;
use crate::payment_model;
use export_doc_domain::payment;
use export_doc_engine::contracts;
use serde_json::{Value, json};

impl Desktop {
    pub fn open_payment(&mut self, value: Value) {
        let payment: ApiPaymentDto = match serde_json::from_value(value) {
            Ok(value) => value,
            Err(cause) => {
                self.error(format!("付款数据无效：{cause}"));
                return;
            }
        };
        self.payment_form = Some(FormModel::new(
            contracts::schema("ApiPaymentDto"),
            json!(payment),
            self.lookups.clone(),
        ));
        self.payment_payee = None;
        self.templates.clear();
        self.clear_report();
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_page("payment-edit".into());
            app.set_payment_tab(0);
            app.set_payment_account_index(0);
            app.set_payment_invalid_field("".into());
            app.set_payment_new_method("".into());
            app.set_report_type("PaymentVoucher".into());
            app.set_report_templates(model(vec![]));
            app.set_report_with_seal(false);
        }
        self.sync_payment(true);
        self.start(Work::PaymentOptions);
    }

    pub fn payment_editable(&self) -> bool {
        self.payment_form.as_ref().is_some_and(|form| {
            self.can(if form.value["id"].as_i64().unwrap_or(0) > 0 {
                UPDATE_PAYMENT
            } else {
                CREATE_PAYMENT
            })
        })
    }
    pub fn payment_dirty(&self) -> bool {
        self.payment_form
            .as_ref()
            .is_some_and(|form| form.value != form.baseline || !form.error.is_empty())
    }
    pub fn sync_payment(&mut self, fields: bool) {
        let (Some(ui), Some(form)) = (self.ui.upgrade(), self.payment_form.as_ref()) else {
            return;
        };
        let app = ui.global::<App>();
        let id = form.value["id"].as_i64().unwrap_or(0);
        let title = form.value["voucherNo"]
            .as_str()
            .filter(|value| !value.is_empty())
            .or_else(|| {
                form.value["invoiceNo"]
                    .as_str()
                    .filter(|value| !value.is_empty())
            })
            .unwrap_or(if id > 0 {
                "付款报销"
            } else {
                "新建付款报销"
            });
        app.set_payment_title(title.into());
        app.set_payment_saved(id > 0);
        app.set_payment_dirty(self.payment_dirty());
        app.set_payment_editable(self.payment_editable());
        app.set_payment_error(form.error.clone().into());
        if form.error.is_empty() {
            app.set_payment_invalid_field("".into());
        }
        app.set_report_ready(self.can(PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML));
        app.set_report_export_ready(
            id > 0 && !self.payment_dirty() && self.can(START_PAYMENT_VOUCHER_PDF_SAVE_TO_PATH_JOB),
        );
        self.report_payment = Some((id, title.into()));
        if let Ok(payment) = serde_json::from_value::<ApiPaymentDto>(form.value.clone()) {
            let total = payment::expense_total(&payment)
                .map(|value| format!("¥ {value:.2}"))
                .unwrap_or_else(|cause| cause.to_string());
            app.set_payment_summary(
                format!(
                    "USD {:.2} / CNY {:.2} · 费用合计 {total}",
                    payment.usd_amount, payment.cny_amount
                )
                .into(),
            );
        }
        if fields {
            let mut sections = crate::form_sections::build(
                form,
                &crate::form_sections::payment(app.get_payment_tab()),
                &mut self.disclosure_state,
            );
            payment_model::localize(&mut sections);
            app.set_payment_sections(model(sections));
        }
    }
    pub fn payment_field_edit(&mut self, key: &str, value: &str) {
        if self.task.is_some() || !self.payment_editable() {
            return;
        }
        let Some(form) = &mut self.payment_form else {
            return;
        };
        let mut derived = false;
        if let Err(cause) = form.edit(key, value) {
            form.error = cause;
        } else if payment::EXPENSE_FIELDS
            .iter()
            .any(|(field, _)| *field == key)
        {
            match serde_json::from_value::<ApiPaymentDto>(form.value.clone())
                .map_err(|cause| cause.to_string())
                .and_then(|payment| {
                    payment::expense_total(&payment).map_err(|cause| cause.to_string())
                }) {
                Ok(total) => {
                    form.value["cnyAmount"] = json!(total);
                    form.buffers.remove("cnyAmount");
                    derived = true;
                }
                Err(cause) => form.error = cause,
            }
        }
        self.clear_report();
        self.sync_payment(derived);
    }
    pub fn payment_field_select(&mut self, key: &str, index: usize) {
        if self.task.is_some() || !self.payment_editable() {
            return;
        }
        let Some(form) = &mut self.payment_form else {
            return;
        };
        if let Err(cause) = form.choose(key, index) {
            form.error = cause;
        }
        if key == "payeeId" && form.value[key].is_null() {
            form.value[key] = json!(0);
        }
        let id = form.value["payeeId"].as_i64().unwrap_or(0);
        self.clear_report();
        self.sync_payment(true);
        if key == "payeeId" {
            self.payment_payee = None;
            if id > 0 {
                self.request(GET_PAYEE, id, vec![], None, "payment-payee");
            }
        }
    }
    pub fn payment_tab(&mut self, index: i32) {
        if self.task.is_some() {
            return;
        }
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_payment_tab(index.clamp(0, 3));
        }
        self.sync_payment(true);
        if index == 3 && self.templates.is_empty() {
            self.request(
                LIST_REPORT_TEMPLATES,
                0,
                vec![("reportType", "PaymentVoucher".into())],
                None,
                "templates",
            );
        }
    }
    pub fn payment_draft(&mut self) -> Result<ApiPaymentDto, String> {
        let form = self.payment_form.as_mut().ok_or("没有付款草稿。")?;
        form.validate()?;
        let draft: ApiPaymentDto =
            serde_json::from_value(form.value.clone()).map_err(|cause| cause.to_string())?;
        if let Err(cause) = payment::validate(&draft) {
            form.error = cause.message.clone();
            form.invalid_field = cause.field.into();
            if let Some(ui) = self.ui.upgrade() {
                let app = ui.global::<App>();
                app.set_payment_tab(payment_model::section(cause.field));
                app.set_payment_invalid_field(cause.field.into());
            }
            self.sync_payment(true);
            return Err(cause.message);
        }
        Ok(draft)
    }
    pub fn reload_payment(&mut self) {
        if let Some(id) = self
            .payment_form
            .as_ref()
            .and_then(|form| form.value["id"].as_i64())
            .filter(|id| *id > 0)
        {
            self.request(GET_PAYMENT, id, vec![], None, "payment-reloaded");
        }
    }
    pub fn payment_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        match action {
            "back" => self.navigate("payments"),
            "save" if self.payment_editable() => match self.payment_draft() {
                Ok(draft) => self.request(
                    if draft.id > 0 {
                        UPDATE_PAYMENT
                    } else {
                        CREATE_PAYMENT
                    },
                    draft.id,
                    vec![],
                    Some(json!(draft)),
                    "payment-saved",
                ),
                Err(cause) => {
                    if let Some(form) = &mut self.payment_form {
                        form.error = cause;
                    }
                    self.sync_payment(false);
                }
            },
            "reload" => {
                if self.payment_dirty() {
                    self.confirm(
                        Pending::ReloadPayment,
                        "重新加载会替换当前未保存的付款草稿，确认继续？",
                    );
                } else {
                    self.reload_payment();
                }
            }
            "account" if self.payment_editable() => {
                if let Some(payee) = self.payment_payee.clone() {
                    self.apply_payment_payee(payee);
                } else if let Some(id) = self
                    .payment_form
                    .as_ref()
                    .and_then(|form| form.value["payeeId"].as_i64())
                    .filter(|id| *id > 0)
                {
                    self.request(GET_PAYEE, id, vec![], None, "payment-payee");
                }
            }
            "add-method" if self.payment_editable() && self.can(SAVE_CUSTOM_OPTION) => {
                let Some(ui) = self.ui.upgrade() else {
                    return;
                };
                let value = ui
                    .global::<App>()
                    .get_payment_new_method()
                    .trim()
                    .to_owned();
                self.start(Work::Request {
                    operation: SAVE_CUSTOM_OPTION,
                    parameters: vec![("optionType", "PaymentMethod".into())],
                    query: vec![],
                    body: Some(json!({"value":value})),
                    reply: "payment-option-saved".into(),
                });
            }
            _ => {}
        }
    }
    pub fn apply_payment_payee(&mut self, payee: Value) {
        let (Some(ui), Some(form)) = (self.ui.upgrade(), self.payment_form.as_mut()) else {
            return;
        };
        if form.value["payeeId"] != payee["id"] {
            return;
        }
        let key = if ui.global::<App>().get_payment_account_index() == 1 {
            "usdAccount"
        } else {
            "rmbAccount"
        };
        for (source, target) in [
            ("name", "payeeName"),
            ("bankName", "bankName"),
            (key, "accountNo"),
        ] {
            form.value[target] = json!(payee[source].as_str().unwrap_or(""));
            form.buffers.remove(target);
        }
        self.payment_payee = Some(payee);
        self.clear_report();
        self.sync_payment(true);
    }
    pub fn payment_options(&mut self, value: Value, selected: bool) {
        let Some(form) = &mut self.payment_form else {
            return;
        };
        if selected {
            let Some(ui) = self.ui.upgrade() else {
                return;
            };
            let text = ui
                .global::<App>()
                .get_payment_new_method()
                .trim()
                .to_owned();
            let canonical = value["options"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(Value::as_str)
                .find(|option| option.to_lowercase() == text.to_lowercase())
                .unwrap_or(&text);
            form.value["paymentMethod"] = json!(canonical);
            form.buffers.remove("paymentMethod");
            ui.global::<App>().set_payment_new_method("".into());
        }
        for key in ["paymentMethod", "payerName"] {
            let response = if selected {
                if key != "paymentMethod" {
                    continue;
                }
                &value
            } else {
                &value[key]
            };
            let mut options: Vec<_> = response["options"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(|value| value.as_str().map(|label| (value.clone(), label.into())))
                .collect();
            if let Some(current) = form.value[key].as_str().filter(|value| !value.is_empty()) {
                if !options.iter().any(|(value, _)| value == current) {
                    options.push((json!(current), current.into()));
                }
            }
            // Payer names remain freely editable; their suggestions can be added
            // through the same candidate API without constraining the text box.
            if key == "paymentMethod" {
                form.lookups.insert(key.into(), options);
            }
        }
        self.sync_payment(true);
    }
    pub fn payment_saved(&mut self, value: Value, reload: bool) {
        let value = if reload {
            value
        } else {
            value["payment"].clone()
        };
        let Some(form) = &mut self.payment_form else {
            return;
        };
        if value["id"].as_i64().is_none_or(|id| id <= 0) {
            self.error("保存未返回有效付款编号，草稿已保留。");
            return;
        }
        form.value = value;
        form.baseline = form.value.clone();
        form.buffers.clear();
        form.error.clear();
        self.clear_report();
        self.sync_payment(true);
        self.status(if reload {
            "已重新加载付款记录"
        } else {
            "付款报销已保存"
        });
    }
}
