use super::*;
use crate::{
    Mail, MailVariable,
    mail_model::{self, text},
};
use serde_json::{Value, json};

impl Desktop {
    pub fn setup_mail(&mut self) {
        self.mail = Default::default();
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Mail>();
            view.set_tab(if self.can(SEND_EMAIL) {
                0
            } else if self.can(LIST_EMAIL_DELIVERIES) {
                1
            } else {
                2
            });
            view.set_template_tab(0);
            view.set_keyword("".into());
            view.set_page(1);
            view.set_status_index(0);
            view.set_include_archived(false);
            ui.global::<App>().set_tabs(model(vec![]));
        }
        self.sync_mail();
        if self.can(GET_EMAIL_TOOL_STATUS) {
            self.request(GET_EMAIL_TOOL_STATUS, 0, vec![], None, "mail:status");
        } else {
            self.refresh_mail();
        }
    }
    pub fn sync_mail(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Mail>();
        view.set_can_send(self.can(SEND_EMAIL));
        view.set_can_configure(self.can(TEST_EMAIL_CONNECTION));
        view.set_can_view_deliveries(self.can(LIST_EMAIL_DELIVERIES));
        view.set_can_view_templates(self.can(LIST_EMAIL_TEMPLATES));
        view.set_can_create(self.can(CREATE_EMAIL_TEMPLATE));
        view.set_can_edit(if let Some(current) = &self.mail.current {
            current["canEdit"] == true
        } else {
            self.can(CREATE_EMAIL_TEMPLATE)
        });
        let current = self.mail.current.as_ref().unwrap_or(&Value::Null);
        view.set_can_publish(current["canPublish"] == true);
        view.set_can_share(current["canShare"] == true);
        view.set_can_disable(current["canDisable"] == true);
        view.set_can_restore(current["canRestore"] == true);
        view.set_can_archive(current["canArchive"] == true);
        view.set_to(self.mail.compose.to.clone().into());
        view.set_subject(self.mail.compose.subject.clone().into());
        view.set_rich_body(self.mail.compose.html.is_some());
        view.set_source_mode(self.mail.rich_source);
        view.set_body(
            if self.mail.rich_source {
                self.mail.compose.body()
            } else {
                self.mail.compose.text.clone()
            }
            .into(),
        );
        view.set_attachments(model(
            self.mail
                .compose
                .attachments
                .iter()
                .map(|p| {
                    p.file_name()
                        .unwrap_or_default()
                        .to_string_lossy()
                        .into_owned()
                        .into()
                })
                .collect(),
        ));
        view.set_rows(model(self.mail.delivery_rows()));
        view.set_template_rows(model(self.mail.template_rows(view.get_template_scope())));
        view.set_template_selected(
            self.mail
                .templates
                .iter()
                .position(|r| r["id"] == current["id"])
                .map_or(0, |i| i as i32 + 1),
        );
        view.set_name(self.mail.template.name.clone().into());
        view.set_category(self.mail.template.category.clone().into());
        view.set_template_subject(self.mail.template.subject.clone().into());
        view.set_template_body(self.mail.template.html.clone().into());
        view.set_template_status(
            if self.mail.id() > 0 {
                format!(
                    "{} · V{}",
                    mail_model::label(&text(current, "status")),
                    text(current, "versionNumber")
                )
            } else {
                "新模板".into()
            }
            .into(),
        );
        view.set_share_scope(
            ["Private", "Department", "Company", "All"]
                .iter()
                .position(|key| current["shareScope"] == *key)
                .unwrap_or(0) as i32,
        );
        view.set_can_undo(self.mail.template_history.can_undo());
        view.set_can_redo(self.mail.template_history.can_redo());
        view.set_variables(model(
            self.mail
                .variables
                .iter()
                .map(|row| {
                    let key = text(row, "key");
                    MailVariable {
                        key: key.clone().into(),
                        label: text(row, "label").into(),
                        token: text(row, "token").into(),
                        value: self
                            .mail
                            .values
                            .get(&key)
                            .cloned()
                            .unwrap_or_default()
                            .into(),
                    }
                })
                .collect(),
        ));
        view.set_customers(model(
            self.mail
                .customers
                .iter()
                .map(|r| text(r, "name").into())
                .collect(),
        ));
        view.set_versions(model(self.mail.version_rows()));
    }
    pub fn refresh_mail(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Mail>();
        match view.get_tab() {
            0 if self.can(GET_EMAIL_TOOL_STATUS) => self.request(
                GET_EMAIL_TOOL_STATUS,
                0,
                vec![],
                None,
                "mail:status-refresh",
            ),
            1 if self.can(LIST_EMAIL_DELIVERIES) => self.request(
                LIST_EMAIL_DELIVERIES,
                0,
                vec![
                    ("keyword", view.get_keyword().to_string()),
                    (
                        "status",
                        ["", "Sent", "Attempting", "Uncertain"]
                            [view.get_status_index().clamp(0, 3) as usize]
                            .into(),
                    ),
                    ("pageNumber", view.get_page().max(1).to_string()),
                    ("pageSize", "30".into()),
                ],
                None,
                "mail:deliveries",
            ),
            2 if self.can(LIST_EMAIL_TEMPLATES) => self.request(
                LIST_EMAIL_TEMPLATES,
                0,
                vec![
                    ("keyword", view.get_keyword().to_string()),
                    ("includeArchived", view.get_include_archived().to_string()),
                ],
                None,
                "mail:templates",
            ),
            _ => {}
        }
    }
    pub fn mail_edit(&mut self, key: &str, value: &str) {
        if self.task.is_some() {
            return;
        }
        if key.starts_with("template-") || key == "insert-token" {
            self.mail_template_edit(key, value);
            return;
        }
        if let Some(key) = key.strip_prefix("variable:") {
            self.mail.values.insert(key.into(), value.into());
            self.mail.preview = None;
            return;
        }
        if !self.can(SEND_EMAIL) {
            return;
        }
        match key {
            "to" => self.mail.compose.to = value.into(),
            "subject" => self.mail.compose.subject = value.into(),
            "body" => {
                if self.mail.rich_source {
                    self.mail.compose.html = Some(value.into());
                    if let Ok((_, plain)) = export_doc_mail::content::sanitize(value) {
                        self.mail.compose.text = plain;
                    }
                } else if self.mail.compose.html.is_none() {
                    self.mail.compose.text = value.into();
                }
            }
            _ => {}
        }
    }
    pub fn mail_action(&mut self, action: &str, index: i32) {
        if self.task.is_some() {
            return;
        }
        if ["clear", "plain", "archive", "restore-version"].contains(&action)
            || (["template", "open-template", "new-template", "copy-template"].contains(&action)
                && self.mail.template != self.mail.baseline)
        {
            self.confirm(
                Pending::MailAction(action.into(), index),
                match action {
                    "plain" => "转为纯文本会移除这封草稿的富文本格式，确认转换？",
                    "clear" => "确认清空尚未发送的邮件草稿？",
                    "archive" => "确认归档此邮件模板？",
                    "restore-version" => "确认以所选版本恢复为私有草稿？",
                    _ => "当前模板修改尚未保存，确认放弃修改？",
                },
            );
            return;
        }
        self.mail_confirmed(action, index);
    }
    pub fn mail_confirmed(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Mail>();
        match action {
            "tab" => {
                if !(0..=2).contains(&index) {
                    return;
                }
                view.set_tab(index);
                view.set_keyword("".into());
                view.set_page(1);
                self.refresh_mail();
            }
            "refresh" | "search" => {
                if action == "search" {
                    view.set_page(1);
                }
                self.refresh_mail();
            }
            "page" => {
                view.set_page((view.get_page() + index).clamp(1, view.get_total_pages().max(1)));
                self.refresh_mail();
            }
            "settings" => {
                self.settings_category = "communication".into();
                self.navigate("settings");
            }
            "source" => self.mail.rich_source = !self.mail.rich_source,
            "plain" => {
                self.mail.compose.html = None;
                self.mail.rich_source = false;
            }
            "clear" => {
                self.mail.compose = Default::default();
                self.mail.sent = Default::default();
                self.mail.pending = None;
                self.mail.delivery_key = None;
                self.mail.rich_source = false;
            }
            "attachment" => {
                if !self.can(SEND_EMAIL) {
                    return;
                }
                if self.mail.compose.attachments.len() >= 10 {
                    self.error("附件最多 10 个。");
                    return;
                }
                if let Some(path) = self.platform.choose_source(
                    ui.window(),
                    "邮件附件",
                    &[
                        "pdf", "png", "jpg", "jpeg", "csv", "txt", "doc", "docx", "xls", "xlsx",
                        "zip",
                    ],
                ) {
                    if !self.mail.compose.attachments.contains(&path) {
                        self.mail.compose.attachments.push(path);
                    }
                }
            }
            "remove-attachment" => {
                if index >= 0 && (index as usize) < self.mail.compose.attachments.len() {
                    self.mail.compose.attachments.remove(index as usize);
                }
            }
            "send" => {
                if !self.can(SEND_EMAIL) {
                    return;
                }
                let body = self.mail.compose.request();
                let key = if let Some((previous, key)) = &self.mail.delivery_key {
                    if previous == &body {
                        Some(key.clone())
                    } else {
                        None
                    }
                } else {
                    None
                };
                let key = match key {
                    Some(key) => key,
                    None => match export_doc_engine::paths::nonce() {
                        Ok(key) => key,
                        Err(cause) => {
                            self.error(cause);
                            return;
                        }
                    },
                };
                self.mail.delivery_key = Some((body.clone(), key.clone()));
                self.mail.pending = Some(self.mail.compose.clone());
                self.start(Work::Request {
                    operation: SEND_EMAIL,
                    parameters: vec![("Idempotency-Key", key)],
                    query: vec![],
                    body: Some(body),
                    reply: "mail:sent".into(),
                });
            }
            "delivery" => {
                if let Some(row) = index
                    .checked_sub(1)
                    .filter(|i| *i >= 0)
                    .and_then(|i| self.mail.deliveries.get(i as usize))
                {
                    self.mail.selected_delivery = index as usize - 1;
                    view.set_selected(index);
                    view.set_details(
                        [
                            ("deliveryId", "投递编号"),
                            ("recipient", "收件人"),
                            ("subject", "主题"),
                            ("status", "投递状态"),
                            ("createdAt", "申请时间"),
                            ("sentAt", "发送时间"),
                            ("errorMessage", "结果说明"),
                        ]
                        .iter()
                        .map(|(key, label)| {
                            format!(
                                "{label}：{}",
                                if *key == "status" {
                                    mail_model::label(&text(row, key))
                                } else {
                                    text(row, key)
                                }
                            )
                        })
                        .collect::<Vec<_>>()
                        .join("\n")
                        .into(),
                    );
                }
            }
            "test" => {
                if self.can(TEST_EMAIL_CONNECTION) {
                    self.request(TEST_EMAIL_CONNECTION, 0, vec![], None, "mail:test");
                }
            }
            "suggestion" => {
                if self.can(SUGGEST_EMAIL_SERVER_CONFIG) {
                    if let Some(form) = &self.form {
                        self.request(
                            SUGGEST_EMAIL_SERVER_CONFIG,
                            0,
                            vec![],
                            Some(json!({"emailAddress":form.value["email"]["userName"]})),
                            "mail:suggestion",
                        );
                    }
                }
            }
            _ => {
                self.mail_template_action(action, index);
                return;
            }
        }
        self.sync_mail();
    }
    pub fn mail_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Mail>();
        match reply {
            "mail:status" | "mail:status-refresh" => {
                view.set_configured(value["isConfigured"] == true);
                view.set_status(
                    if value["isConfigured"] == true {
                        format!("发件人：{}", text(&value, "fromAddress"))
                    } else {
                        "请先保存 SMTP 和发件人设置".into()
                    }
                    .into(),
                );
                if reply == "mail:status" && view.get_tab() != 0 {
                    self.refresh_mail();
                }
            }
            "mail:deliveries" => {
                self.mail.deliveries = value["items"].as_array().cloned().unwrap_or_default();
                view.set_total_pages(value["totalPages"].as_i64().unwrap_or(1).max(1) as i32);
                view.set_selected(0);
                view.set_details("选择记录查看完整投递结果。".into());
            }
            "mail:sent" => {
                if let Some(draft) = self.mail.pending.take() {
                    self.mail.sent = draft;
                }
                self.status(text(&value, "message"));
            }
            "mail:test" => self.status(text(&value, "message")),
            "mail:suggestion" => {
                if let Some(form) = &mut self.form {
                    for key in ["smtpHost", "smtpPort", "enableSsl"] {
                        form.value["email"][key] = value[key].clone();
                        form.buffers.remove(&format!("email.{key}"));
                    }
                }
                self.sync_form();
                self.status("SMTP 建议已填入草稿，请核对后保存。");
            }
            _ => {
                self.mail_template_loaded(reply, value);
                return;
            }
        }
        self.sync_mail();
    }
}
