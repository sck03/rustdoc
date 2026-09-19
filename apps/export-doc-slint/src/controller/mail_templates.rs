use super::*;
use crate::{
    Mail,
    mail_model::{self, text},
};
use serde_json::{Value, json};

impl Desktop {
    pub fn mail_template_edit(&mut self, key: &str, value: &str) {
        let can_edit = self
            .mail
            .current
            .as_ref()
            .map_or(self.can(CREATE_EMAIL_TEMPLATE), |r| r["canEdit"] == true);
        if !can_edit {
            return;
        }
        let before = self.mail.template.clone();
        match key {
            "template-name" => self.mail.template.name = value.into(),
            "template-category" => self.mail.template.category = value.into(),
            "template-subject" => self.mail.template.subject = value.into(),
            "template-body" => self.mail.template.html = value.into(),
            "insert-token" => self.mail.template.html.push_str(value),
            _ => return,
        };
        self.mail
            .template_history
            .record(before, &self.mail.template, Some(key.into()));
        self.mail.preview = None;
        if key == "insert-token" {
            self.sync_mail();
        }
    }
    pub fn mail_template_action(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Mail>();
        match action {
            "template-tab" => {
                if !(0..=4).contains(&index) {
                    return;
                }
                view.set_template_tab(index);
                if index == 4 && self.mail.id() > 0 {
                    self.request(
                        LIST_EMAIL_TEMPLATE_VERSIONS,
                        self.mail.id(),
                        vec![],
                        None,
                        "mail:versions",
                    );
                } else if index == 3 {
                    self.mail_template_action("preview", 0);
                }
            }
            "filter" => {}
            "template" | "open-template" => {
                if let Some(row) = index
                    .checked_sub(1)
                    .filter(|i| *i >= 0)
                    .and_then(|i| self.mail.templates.get(i as usize))
                    .cloned()
                {
                    self.mail.open(Some(row));
                    if action == "open-template" {
                        view.set_template_tab(1);
                    }
                }
            }
            "new-template" => {
                if self.can(CREATE_EMAIL_TEMPLATE) {
                    self.mail.open(None);
                    view.set_template_tab(1);
                }
            }
            "copy-template" => {
                if !self.can(CREATE_EMAIL_TEMPLATE) {
                    return;
                }
                let mut draft = self.mail.template.clone();
                let prefix = if draft.name.is_empty() {
                    "未命名模板"
                } else {
                    &draft.name
                };
                let mut name = format!("{prefix} 副本");
                let mut count = 2;
                while self.mail.templates.iter().any(|r| text(r, "name") == name) {
                    name = format!("{prefix} 副本 {count}");
                    count += 1;
                }
                draft.name = name;
                self.mail.open(None);
                self.mail.template = draft;
                view.set_template_tab(1);
            }
            "undo" | "redo" => {
                if action == "undo" {
                    self.mail.template_history.undo(&mut self.mail.template);
                } else {
                    self.mail.template_history.redo(&mut self.mail.template);
                }
                self.mail.preview = None;
            }
            "save-template" => {
                let operation = if self.mail.id() == 0 {
                    CREATE_EMAIL_TEMPLATE
                } else {
                    SAVE_EMAIL_TEMPLATE_DRAFT
                };
                if !self.can(operation) {
                    return;
                }
                self.request(
                    operation,
                    self.mail.id(),
                    vec![],
                    Some(self.mail.template_body()),
                    "mail:template-saved",
                );
            }
            "publish" | "share" | "disable" | "restore" | "archive" | "restore-version" => {
                if self.mail.template != self.mail.baseline {
                    self.error("请先保存或撤销模板修改，再执行状态操作。");
                    return;
                }
                let Some(row) = &self.mail.current else {
                    return;
                };
                let operation = match action {
                    "publish" => PUBLISH_EMAIL_TEMPLATE,
                    "share" => SHARE_EMAIL_TEMPLATE,
                    "disable" => DISABLE_EMAIL_TEMPLATE,
                    "restore" => RESTORE_EMAIL_TEMPLATE,
                    "archive" => ARCHIVE_EMAIL_TEMPLATE,
                    _ => RESTORE_EMAIL_TEMPLATE_VERSION,
                };
                if !self.can(operation) {
                    return;
                }
                let mut body = json!({"expectedVersion":row["versionNumber"]});
                let mut parameters = vec![("id", self.mail.id().to_string())];
                if action == "share" {
                    body["shareScope"] = json!(
                        ["Private", "Department", "Company", "All"]
                            [view.get_share_scope().clamp(0, 3) as usize]
                    );
                }
                if action == "restore-version" {
                    let Some(row) = index
                        .checked_sub(1)
                        .filter(|i| *i >= 0)
                        .and_then(|i| self.mail.versions.get(i as usize))
                    else {
                        return;
                    };
                    if row["canRestore"] != true {
                        return;
                    }
                    parameters.push(("versionNumber", text(row, "versionNumber")));
                }
                self.start(Work::Request {
                    operation,
                    parameters,
                    query: vec![],
                    body: Some(body),
                    reply: "mail:template-saved".into(),
                });
            }
            "preview" => {
                if self.can(PREVIEW_EMAIL_TEMPLATE) {
                    self.request(PREVIEW_EMAIL_TEMPLATE,0,vec![],Some(json!({"subject":self.mail.template.subject,"bodyHtml":self.mail.template.html,"variables":self.mail.values})),"mail:preview");
                }
            }
            "compose-preview" => {
                if self.mail.compose != self.mail.sent && !self.mail.compose.empty() {
                    self.error("当前邮件草稿尚未发送，请先处理或清空草稿。");
                    return;
                }
                if let Some(preview) = &self.mail.preview {
                    if preview["unresolvedTokens"]
                        .as_array()
                        .is_some_and(|r| !r.is_empty())
                    {
                        return;
                    }
                    let html = text(preview, "bodyHtml");
                    self.mail.compose.subject = text(preview, "subject");
                    self.mail.compose.text = export_doc_mail::content::sanitize(&html)
                        .map(|(_, plain)| plain)
                        .unwrap_or_default();
                    self.mail.compose.html = Some(html);
                    self.mail.rich_source = false;
                    view.set_tab(0);
                }
            }
            "customers" => {
                if self.can(QUERY_CRM_CUSTOMERS) {
                    self.request(
                        QUERY_CRM_CUSTOMERS,
                        0,
                        vec![
                            ("keyword", view.get_customer_keyword().to_string()),
                            ("pageSize", "100".into()),
                        ],
                        None,
                        "mail:customers",
                    );
                }
            }
            "customer" => {
                if self.can(GET_CRM_EMAIL_VARIABLE_DRAFT) {
                    if let Some(id) = self
                        .mail
                        .customers
                        .get(index.max(0) as usize)
                        .and_then(|r| r["id"].as_i64())
                    {
                        self.request(
                            GET_CRM_EMAIL_VARIABLE_DRAFT,
                            id,
                            vec![],
                            None,
                            "mail:customer",
                        );
                    }
                }
            }
            "version" => {
                if let Some(row) = index
                    .checked_sub(1)
                    .filter(|i| *i >= 0)
                    .and_then(|i| self.mail.versions.get(i as usize))
                {
                    self.mail.selected_version = index as usize - 1;
                    view.set_version_selected(index);
                    view.set_version_detail(
                        format!(
                            "V{} · {} · {}\n主题：{}\n{}",
                            text(row, "versionNumber"),
                            mail_model::label(&text(row, "status")),
                            text(row, "changedBy"),
                            text(row, "subject"),
                            export_doc_mail::content::sanitize(&text(row, "bodyHtml"))
                                .map(|(_, plain)| plain)
                                .unwrap_or_default()
                        )
                        .into(),
                    );
                }
            }
            _ => return,
        }
        self.sync_mail();
    }
    pub fn mail_template_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Mail>();
        match reply {
            "mail:templates" => {
                self.mail.templates = value.as_array().cloned().unwrap_or_default();
                if self.mail.variables.is_empty() && self.can(LIST_EMAIL_TEMPLATE_VARIABLES) {
                    self.request(
                        LIST_EMAIL_TEMPLATE_VARIABLES,
                        0,
                        vec![],
                        None,
                        "mail:variables",
                    );
                }
            }
            "mail:variables" => {
                self.mail.variables = value.as_array().cloned().unwrap_or_default();
                for row in &self.mail.variables {
                    self.mail
                        .values
                        .entry(text(row, "key"))
                        .or_insert_with(|| text(row, "sampleValue"));
                }
                if let Some(user) = &self.user {
                    self.mail
                        .values
                        .insert("Today".into(), user.business_date.clone());
                    self.mail
                        .values
                        .insert("SenderName".into(), user.full_name.clone());
                }
            }
            "mail:template-saved" => {
                self.mail.open(Some(value));
                view.set_template_tab(1);
                self.refresh_mail();
                self.status("邮件模板已保存。");
            }
            "mail:preview" => {
                view.set_preview_subject(text(&value, "subject").into());
                view.set_preview_body(
                    export_doc_mail::content::sanitize(&text(&value, "bodyHtml"))
                        .map(|(_, plain)| plain)
                        .unwrap_or_default()
                        .into(),
                );
                view.set_unresolved(
                    value["unresolvedTokens"]
                        .as_array()
                        .map(|v| {
                            v.iter()
                                .filter_map(Value::as_str)
                                .collect::<Vec<_>>()
                                .join("、")
                        })
                        .unwrap_or_default()
                        .into(),
                );
                self.mail.preview = Some(value);
                view.set_template_tab(3);
            }
            "mail:versions" => {
                self.mail.versions = value.as_array().cloned().unwrap_or_default();
                view.set_version_selected(0);
                view.set_version_detail("选择版本查看历史内容。".into());
            }
            "mail:customers" => {
                self.mail.customers = value["items"].as_array().cloned().unwrap_or_default();
                view.set_customer_index(-1);
            }
            "mail:customer" => {
                if let Some(values) = value["variables"].as_object() {
                    for (key, value) in values {
                        if let Some(value) = value.as_str() {
                            self.mail.values.insert(key.clone(), value.into());
                        }
                    }
                }
                self.mail.compose.to = text(&value, "toAddress");
                self.mail.preview = None;
            }
            _ => return,
        }
        self.sync_mail();
    }
}
