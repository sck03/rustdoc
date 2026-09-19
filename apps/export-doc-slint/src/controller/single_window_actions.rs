use super::*;
use crate::{SingleWindow, single_window_model::text};
use export_doc_domain::single_window::{self as rules, Business};
use export_doc_engine::contracts;
use serde_json::{Value, json};

impl Desktop {
    pub fn single_window_action(&mut self, action: &str, index: i32) {
        if self.task.is_some() {
            return;
        }
        let changes_document = action == "invoice"
            || action == "reload-document"
            || (action == "tab"
                && [1, 2].contains(&index)
                && self.single_window.business
                    != if index == 1 {
                        Business::Coo
                    } else {
                        Business::Acd
                    });
        let dirty = self
            .single_window
            .form
            .as_ref()
            .is_some_and(|f| f.value != f.baseline);
        let destructive = [
            "remove-good",
            "remove-attachment",
            "remove-corp",
            "reset-dictionary",
            "remove-dictionary-row",
            "unlock",
            "dispatch",
        ]
        .contains(&action);
        if destructive
            || changes_document && dirty
            || action == "refresh" && self.single_window.dirty()
        {
            self.confirm(
                Pending::SingleWindowAction(action.into(), index),
                if action == "dispatch" {
                    "确认将所选批次送入当前档案的官方客户端交接目录？"
                } else if action == "reset-dictionary" {
                    "确认恢复内置申报词典？自定义词典修改将被替换。"
                } else if changes_document && dirty {
                    "当前申报草稿尚未保存，确认放弃修改并重新读取？"
                } else {
                    "确认执行当前操作？"
                },
            );
            return;
        }
        self.single_window_confirmed(action, index);
    }
    pub fn single_window_confirmed(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<SingleWindow>();
        let id = self.single_window.invoice_id;
        match action {
            "tab" => {
                if !(0..=4).contains(&index) {
                    return;
                }
                if index == 1 || index == 2 {
                    let business = if index == 1 {
                        Business::Coo
                    } else {
                        Business::Acd
                    };
                    if self.single_window.business != business {
                        self.single_window.business = business;
                        self.single_window.form = None;
                        self.single_window.history.clear();
                    }
                }
                view.set_tab(index);
                view.set_document_tab(0);
                if (index == 1 || index == 2) && self.single_window.form.is_some() {
                    self.sync_single_window();
                    return;
                }
                self.refresh_single_window();
            }
            "refresh" | "reload-document" => self.refresh_single_window(),
            "search" => {
                view.set_page(1);
                self.refresh_single_window();
            }
            "page" => {
                view.set_page((view.get_page() + index).clamp(1, view.get_total_pages().max(1)));
                self.refresh_single_window();
            }
            "batch" => {
                if let Some(row) = index
                    .checked_sub(1)
                    .and_then(|i| self.single_window.batches.get(i as usize))
                {
                    view.set_selected_batch(index);
                    self.request(
                        GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL,
                        row["batchId"].as_i64().unwrap_or(0),
                        vec![],
                        None,
                        "sw:batch",
                    );
                }
            }
            "find-invoices" => self.request(
                LIST_INVOICES,
                0,
                vec![
                    ("keyword", view.get_invoice_keyword().into()),
                    ("pageSize", "100".into()),
                ],
                None,
                "sw:invoices",
            ),
            "invoice" => {
                if let Some(row) = self.single_window.invoices.get(index as usize) {
                    self.single_window.invoice_id = row["id"].as_i64().unwrap_or(0);
                    self.refresh_single_window();
                }
            }
            "source" => {
                if id > 0 {
                    self.pending_open = Some(("invoices", id));
                    self.navigate("invoices");
                }
            }
            "document-tab" => {
                view.set_document_tab(index.clamp(0, 4));
            }
            "save-document" => {
                let operation = self.single_window_operation(
                    SAVE_CUSTOMS_COO_DOCUMENT,
                    SAVE_AGENT_CONSIGNMENT_DOCUMENT,
                );
                if let Some(form) = &mut self.single_window.form {
                    if let Err(cause) = form.validate() {
                        self.error(cause);
                        return;
                    }
                    let body = form.value.clone();
                    self.request(operation, id, vec![], Some(body), "sw:saved");
                }
            }
            "fill-defaults" => self.request(
                self.single_window_operation(
                    BUILD_CUSTOMS_COO_DEFAULTS,
                    BUILD_AGENT_CONSIGNMENT_DEFAULTS,
                ),
                id,
                vec![],
                None,
                "sw:defaults",
            ),
            "undo" | "redo" => {
                if let Some(form) = &mut self.single_window.form {
                    if action == "undo" {
                        self.single_window.history.undo(&mut form.value);
                    } else {
                        self.single_window.history.redo(&mut form.value);
                    }
                    form.buffers.clear();
                    self.single_window.review = Value::Null;
                }
            }
            "good" => self.single_window.selected_item = index.saturating_sub(1) as usize,
            "attachment" => {
                self.single_window.selected_attachment = index.saturating_sub(1) as usize
            }
            "corp" => self.single_window.selected_corp = index.saturating_sub(1) as usize,
            "review" => {
                if let Some(form) = &self.single_window.form {
                    let mut groups = std::collections::BTreeMap::<String, Vec<Value>>::new();
                    for issue in rules::validation::review(self.single_window.business, &form.value)
                    {
                        let group = if issue.scope == "goods" {
                            "明细项目"
                        } else {
                            crate::single_window_sections::document(self.single_window.business)
                                .iter()
                                .find(|g| g.fields.contains(&issue.field.as_str()))
                                .map(|g| g.title)
                                .unwrap_or("补充与特殊项")
                        };
                        groups.entry(group.into()).or_default().push(json!({"groupKey":group,"groupDisplayName":group,"message":issue.message,"navigationTarget":{"propertyKey":rules::pascal(&issue.field),"goodsLineNo":issue.row.map(|i|i+1)}}));
                    }
                    self.single_window.review = json!({"groups":groups.into_iter().map(|(group,issues)|json!({"groupKey":group,"issues":issues})).collect::<Vec<_>>()});
                    if self.single_window_issues().is_empty() {
                        self.status("当前草稿已通过申报字段校验。");
                    }
                }
            }
            "locate-issue" => {
                if let Some(issue) = index
                    .checked_sub(1)
                    .and_then(|i| self.single_window_issues().get(i as usize).cloned())
                {
                    if let Some(row) = issue["navigationTarget"]["goodsLineNo"].as_u64() {
                        view.set_document_tab(1);
                        self.single_window.selected_item = row.saturating_sub(1) as usize;
                        for section in crate::single_window_sections::goods() {
                            self.single_window
                                .disclosures
                                .insert(section.key.into(), true);
                        }
                    } else {
                        view.set_document_tab(0);
                        for section in
                            crate::single_window_sections::document(self.single_window.business)
                        {
                            if section.fields.iter().any(|k| {
                                rules::pascal(k) == text(&issue["navigationTarget"], "propertyKey")
                            }) {
                                self.single_window
                                    .disclosures
                                    .insert(section.key.into(), true);
                            }
                        }
                    }
                }
            }
            "locks" => self.request(
                self.single_window_operation(
                    GET_CUSTOMS_COO_LOCKED_FIELDS,
                    GET_AGENT_CONSIGNMENT_LOCKED_FIELDS,
                ),
                id,
                vec![],
                None,
                "sw:locks",
            ),
            "unlock" | "repair-group" => {
                if self
                    .single_window
                    .form
                    .as_ref()
                    .is_some_and(|f| f.value != f.baseline)
                {
                    self.error("请先保存当前草稿，再执行解锁或自动修复。");
                    return;
                }
                if action == "unlock" {
                    if let Some(lock) = index
                        .checked_sub(1)
                        .and_then(|i| self.single_window.locks.get(i as usize))
                    {
                        self.request(
                            self.single_window_operation(
                                UNLOCK_CUSTOMS_COO_FIELDS,
                                UNLOCK_AGENT_CONSIGNMENT_FIELDS,
                            ),
                            id,
                            vec![],
                            Some(json!({"fieldKeys":[lock["key"]]})),
                            "sw:unlocked",
                        );
                    }
                } else if let Some(issue) = index
                    .checked_sub(1)
                    .and_then(|i| self.single_window_issues().get(i as usize).cloned())
                {
                    self.start(Work::Request {
                        operation: REPAIR_SINGLE_WINDOW_EXPORT_REVIEW_GROUPS,
                        parameters: vec![
                            ("businessType", self.single_window.business.name().into()),
                            ("invoiceId", id.to_string()),
                        ],
                        query: vec![],
                        body: Some(json!({"groupKeys":[issue["groupKey"]]})),
                        reply: "sw:repaired".into(),
                    });
                }
            }
            "producers" | "find-producers" => self.request(
                LIST_CUSTOMS_COO_PRODUCER_PROFILES,
                0,
                vec![("keyword", view.get_producer_keyword().to_string())],
                None,
                "sw:producers",
            ),
            "remember-producer" => {
                if let Some(row) = self
                    .single_window
                    .form
                    .as_ref()
                    .and_then(|f| f.value["items"].get(self.single_window.selected_item))
                {
                    let input = contracts::project(
                        contracts::schema("ApiCustomsCooProducerProfileInputDto"),
                        row.clone(),
                    );
                    self.request(
                        CREATE_CUSTOMS_COO_PRODUCER_PROFILE,
                        0,
                        vec![],
                        Some(json!({"profile":input})),
                        "sw:producer-saved",
                    );
                }
            }
            "add-good" | "remove-good" | "add-attachment" | "remove-attachment" | "add-corp"
            | "remove-corp" | "copy-origin" | "goods-description" | "apply-producer" => {
                self.single_window_rows(action, index)
            }
            _ => self.single_window_tools(action, index),
        }
        self.sync_single_window();
    }
    fn single_window_rows(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let source = if action == "add-attachment" {
            self.platform
                .choose_source(
                    ui.window(),
                    "单证附件",
                    &["pdf", "png", "jpg", "jpeg", "docx", "xlsx"],
                )
                .map(|p| p.to_string_lossy().into_owned())
        } else {
            None
        };
        if action == "add-attachment" && source.is_none() {
            return;
        }
        let Some(form) = &mut self.single_window.form else {
            return;
        };
        let before = form.value.clone();
        let group = if action.contains("attachment") {
            "attachments"
        } else if action.contains("corp") {
            "nonpartyCorps"
        } else {
            "items"
        };
        let schema =
            contracts::resolve(&contracts::schema("ApiCustomsCooDocumentDto")["properties"][group])
                ["items"]
                .clone();
        let Some(rows) = form.value[group].as_array_mut() else {
            return;
        };
        if action.starts_with("add-") {
            if rows.len() >= if group == "items" { 5000 } else { 20 } {
                self.error("条目已达到数量上限。");
                return;
            }
            let mut row = contracts::initial(&schema);
            if let Some(path) = source {
                row["fileName"] = json!(
                    std::path::Path::new(&path)
                        .file_name()
                        .unwrap_or_default()
                        .to_string_lossy()
                );
                row["filePath"] = json!(path);
                row["mediaType"] = json!("application/octet-stream");
            }
            rows.push(row);
            match group {
                "items" => self.single_window.selected_item = rows.len() - 1,
                "attachments" => self.single_window.selected_attachment = rows.len() - 1,
                _ => self.single_window.selected_corp = rows.len() - 1,
            }
        } else if action.starts_with("remove-") {
            if let Some(i) = index
                .checked_sub(1)
                .filter(|i| *i >= 0 && (*i as usize) < rows.len())
            {
                rows.remove(i as usize);
            }
            self.single_window.selected_item = 0;
            self.single_window.selected_attachment = 0;
            self.single_window.selected_corp = 0;
        } else if action == "apply-producer" {
            if let (Some(producer), Some(row)) = (
                index
                    .checked_sub(1)
                    .and_then(|i| self.single_window.producers.get(i as usize)),
                rows.get_mut(self.single_window.selected_item),
            ) {
                for key in [
                    "ciqRegNo",
                    "prdcEtpsName",
                    "prdcEtpsConcEr",
                    "prdcEtpsTel",
                    "producer",
                    "producerTel",
                    "producerFax",
                    "producerEmail",
                    "producerSertFlag",
                ] {
                    row[key] = producer[key].clone();
                }
                ui.global::<SingleWindow>().set_producers_open(false);
            }
        } else if let Some(source) = rows.get(self.single_window.selected_item).cloned() {
            if action == "copy-origin" {
                for row in rows.iter_mut().skip(self.single_window.selected_item + 1) {
                    for key in [
                        "oriCriteria",
                        "oriCriteriaRef",
                        "oriCriteriaSub",
                        "ciqRegNo",
                        "prdcEtpsName",
                        "prdcEtpsConcEr",
                        "prdcEtpsTel",
                    ] {
                        if !rules::text(&source, key).is_empty() {
                            row[key] = source[key].clone();
                        }
                    }
                }
            } else {
                let description = rules::mapping::goods_description(&source);
                if description.is_empty() {
                    self.error("请先填写包装件数、包装单位和英文品名，再生成货物描述。");
                    return;
                }
                rows[self.single_window.selected_item]["goodsDesc"] = json!(description);
            }
        }
        for (i, row) in rows.iter_mut().enumerate() {
            row[if group == "items" {
                "gNo"
            } else if group == "nonpartyCorps" {
                "sortNo"
            } else {
                "sortOrder"
            }] = json!(i + 1);
        }
        form.buffers.clear();
        self.single_window.history.record(before, &form.value, None);
        self.single_window.review = Value::Null;
    }
}
