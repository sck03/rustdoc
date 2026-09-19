use super::*;
use crate::{
    Hs,
    hs_model::{self, text},
};
use serde_json::{Value, json};

impl Desktop {
    pub fn setup_hs(&mut self) {
        self.hs = Default::default();
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Hs>();
            view.set_tab(0);
            view.set_page(1);
            view.set_query("".into());
            view.set_year(chrono::Local::now().format("%Y").to_string().into());
            view.set_import_summary("先预检文件，再核对新增、变更、冲突和疑似作废记录。".into());
        }
        self.sync_hs();
        self.refresh_hs();
    }

    pub fn sync_hs(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Hs>();
        let tab = view.get_tab();
        view.set_can_manage(self.can(if tab == 2 || tab == 3 {
            SAVE_HS_CODE_KNOWLEDGE_EXAMPLE
        } else {
            CREATE_HS_CODE
        }));
        view.set_can_feedback(self.can(RECORD_HS_CODE_KNOWLEDGE_FEEDBACK));
        view.set_can_remote(self.can(SEARCH_REMOTE_HS_CODES));
        view.set_editor(self.hs.form.is_some());
        view.set_editing_code(self.hs.editing_code);
        view.set_sections(model(self.hs.sections(&mut self.disclosure_state)));
        view.set_form_error(
            self.hs
                .form
                .as_ref()
                .map(|f| f.error.clone())
                .unwrap_or_default()
                .into(),
        );
        view.set_invalid_field(
            self.hs
                .form
                .as_ref()
                .map(|f| f.invalid_field.clone())
                .unwrap_or_default()
                .into(),
        );
        view.set_columns(model(
            hs_model::columns(tab).iter().map(|s| (*s).into()).collect(),
        ));
        view.set_rows(model(self.hs.data_rows(tab)));
        view.set_selected(if self.hs.row().is_some() {
            self.hs.selected as i32 + 1
        } else {
            0
        });
        view.set_details(
            self.hs
                .row()
                .map(|r| hs_model::details(r, tab))
                .unwrap_or_else(|| "选择一条记录查看详情。".into())
                .into(),
        );
        view.set_can_use(self.hs.row().is_some_and(|r| r["canUse"] == true));
        view.set_can_confirm(self.hs.row().is_some_and(|r| r["canConfirm"] == true));
        view.set_checked_count(self.hs.checked.len() as i32);
        view.set_preview_ready(self.hs.preview.is_some());
    }

    pub fn refresh_hs(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Hs>();
        let tab = view.get_tab();
        if tab == 5 {
            let items = self.hs.preview.as_ref().and_then(|p| p["items"].as_array());
            let total = items.map_or(0, Vec::len);
            let pages = total.div_ceil(50).max(1) as i32;
            let page = view.get_page().clamp(1, pages);
            view.set_page(page);
            view.set_total_pages(pages);
            self.hs.rows = items
                .into_iter()
                .flatten()
                .skip((page as usize - 1) * 50)
                .take(50)
                .cloned()
                .collect();
            self.hs.selected = 0;
            self.sync_hs();
            return;
        }
        let keyword = view.get_query().to_string();
        let operation = match tab {
            0 => SEARCH_HS_CODE_KNOWLEDGE,
            1 => LIST_HS_CODES,
            2 => LIST_HS_CODE_KNOWLEDGE_EXAMPLES,
            3 => DISCOVER_HS_CODE_HISTORY_CANDIDATES,
            4 => LIST_HS_CODE_REMOTE_CANDIDATES,
            6 if self.can(CAPTURE_REMOTE_HS_CODES) => CAPTURE_REMOTE_HS_CODES,
            6 => SEARCH_REMOTE_HS_CODES,
            _ => return,
        };
        if tab == 6 && keyword.trim().is_empty() {
            view.set_summary("输入条件后查询联网参考资料。".into());
            self.sync_hs();
            return;
        }
        if !self.can(operation) {
            self.error("当前账号或能力模块不支持此操作。");
            return;
        }
        let query = if tab == 0 {
            vec![("query", keyword), ("maxResults", "50".into())]
        } else if operation == CAPTURE_REMOTE_HS_CODES {
            vec![]
        } else {
            vec![
                ("keyword", keyword.clone()),
                ("pageNumber", view.get_page().max(1).to_string()),
                ("pageSize", "50".into()),
                (
                    "status",
                    ["Pending", "Confirmed", "Ignored"]
                        [view.get_candidate_status().clamp(0, 2) as usize]
                        .into(),
                ),
            ]
        };
        let body = (operation == CAPTURE_REMOTE_HS_CODES)
            .then(|| json!({"keyword":view.get_query().to_string()}));
        self.request(operation, 0, query, body, "hs:list");
    }

    pub fn hs_edit(&mut self, key: &str, value: &str, choice: Option<i32>) {
        if self.task.is_some() || !self.can(CREATE_HS_CODE) {
            return;
        }
        if let Some(form) = &mut self.hs.form {
            if self.hs.editing_code
                && key == "code"
                && form.baseline["id"].as_i64().unwrap_or(0) > 0
            {
                return;
            }
            let result = match choice {
                Some(index) => form.choose(key, index.max(0) as usize),
                None => form.edit(key, value),
            };
            if let Err(cause) = result {
                form.error = cause;
            }
        }
        self.sync_hs();
    }

    pub fn hs_action(&mut self, action: &str, index: i32) {
        if self.task.is_some() {
            return;
        }
        if action.starts_with("hs-") {
            let expanded = self
                .disclosure_state
                .get(action)
                .copied()
                .unwrap_or(action != "hs-code-evidence");
            self.disclosure_state.insert(action.into(), !expanded);
            self.sync_hs();
            return;
        }
        if (["tab", "cancel-editor", "new", "open"].contains(&action) && self.hs.dirty())
            || ["delete-batch", "ignore-batch", "reset-batch"].contains(&action)
        {
            self.confirm(
                Pending::HsAction(action.into(), index),
                if self.hs.dirty() {
                    "当前知识资料尚未保存，确认放弃修改？"
                } else {
                    "确认处理所选记录？重置已确认候选会移除由它建立且尚未独立修改的案例。"
                },
            );
            return;
        }
        self.hs_confirmed(action, index);
    }

    pub fn hs_confirmed(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Hs>();
        let tab = view.get_tab();
        match action {
            "tab" => {
                if !(0..=6).contains(&index) {
                    return;
                }
                self.hs.form = None;
                self.hs.rows.clear();
                self.hs.checked.clear();
                self.hs.selected = 0;
                view.set_tab(index);
                view.set_page(1);
                view.set_total_pages(1);
                view.set_summary("".into());
                view.set_query("".into());
                view.set_multi(false);
                self.refresh_hs();
                self.sync_hs();
                return;
            }
            "search" => {
                view.set_page(1);
                self.refresh_hs();
                return;
            }
            "page" => {
                view.set_page((view.get_page() + index).clamp(1, view.get_total_pages().max(1)));
                self.refresh_hs();
                return;
            }
            "select" | "open" => {
                let Some(selected) = index
                    .checked_sub(1)
                    .filter(|i| *i >= 0)
                    .map(|i| i as usize)
                    .filter(|i| *i < self.hs.rows.len())
                else {
                    return;
                };
                self.hs.selected = selected;
                if action == "select" && view.get_multi() {
                    if let Some(id) = self.hs.row().and_then(|r| r["id"].as_i64()) {
                        if !self.hs.checked.remove(&id) {
                            self.hs.checked.insert(id);
                        }
                    }
                } else if action == "open" && [1, 2].contains(&tab) {
                    self.hs.open(self.hs.row().cloned(), tab == 1);
                }
                if tab == 4 {
                    view.set_current_code(
                        self.hs
                            .row()
                            .map(|r| text(r, "suggestedCurrentHsCode"))
                            .unwrap_or_default()
                            .into(),
                    );
                }
            }
            "new" if self.can(CREATE_HS_CODE) && [1, 2].contains(&tab) => {
                self.hs.open(None, tab == 1)
            }
            "cancel-editor" => self.hs.form = None,
            "save" => {
                self.save_hs();
                return;
            }
            "select-page" => self
                .hs
                .checked
                .extend(self.hs.rows.iter().filter_map(|r| r["id"].as_i64())),
            "select-none" => self.hs.checked.clear(),
            "delete-batch" | "ignore-batch" | "reset-batch" => {
                self.hs_batch(action, tab);
                return;
            }
            "accept" | "reject" => {
                if !self.can(RECORD_HS_CODE_KNOWLEDGE_FEEDBACK) {
                    return;
                }
                if let Some(row) = self.hs.row() {
                    if action == "accept" && row["canUse"] != true {
                        self.error("此候选尚不能确认适用，请先核实有效税则。");
                        return;
                    }
                    let code = if text(row, "currentCode").is_empty() {
                        &row["rawCode"]
                    } else {
                        &row["currentCode"]
                    };
                    self.request(RECORD_HS_CODE_KNOWLEDGE_FEEDBACK, 0, vec![], Some(json!({"queryText":view.get_query().to_string(),"productName":row["name"],"specification":row["specification"],"candidateCode":code,"accepted":action=="accept"})), "hs:feedback");
                }
                return;
            }
            "learn" => {
                if !self.can(SAVE_HS_CODE_KNOWLEDGE_EXAMPLE) {
                    return;
                }
                if let Some(row) = self.hs.row() {
                    if row["canConfirm"] != true {
                        self.error("历史编码未关联到已验证的当前税则，请先核实后维护案例。");
                        return;
                    }
                    self.request(SAVE_HS_CODE_KNOWLEDGE_EXAMPLE, 0, vec![], Some(json!({"rawReportedHsCode":row["rawCode"],"resolvedCurrentHsCode":row["currentCode"],"productName":row["productName"],"specification":row["specification"],"source":"HistoryConfirmed","isManuallyVerified":true,"resolutionStatus":"ManuallyVerified"})), "hs:changed");
                }
                return;
            }
            "confirm-candidate" => {
                if !self.can(REVIEW_HS_CODE_REMOTE_CANDIDATE) {
                    return;
                }
                if let Some(row) = self.hs.row() {
                    self.request(REVIEW_HS_CODE_REMOTE_CANDIDATE, 0, vec![], Some(json!({"id":row["id"],"currentCode":view.get_current_code().to_string(),"confirmed":true})), "hs:changed");
                }
                return;
            }
            "remote-detail" => {
                if !self.can(FETCH_REMOTE_HS_CODE_DETAIL) {
                    return;
                }
                if let Some(row) = self.hs.row().cloned() {
                    self.request(
                        FETCH_REMOTE_HS_CODE_DETAIL,
                        0,
                        vec![],
                        Some(row),
                        "hs:remote-detail",
                    );
                }
                return;
            }
            "reference" => {
                if !self.can(CREATE_HS_CODE) {
                    return;
                }
                if let Some(mut row) = self.hs.row().cloned() {
                    row["id"] = json!(0);
                    row["status"] = json!("ReferenceOnly");
                    row["rowVersion"] = Value::Null;
                    self.request(CREATE_HS_CODE, 0, vec![], Some(row), "hs:reference");
                }
                return;
            }
            "preview-tariff" | "commit-tariff" | "export-package" | "import-package" => {
                self.hs_file_action(action);
                return;
            }
            _ => return,
        }
        self.sync_hs();
    }

    fn save_hs(&mut self) {
        let Some(form) = &mut self.hs.form else {
            return;
        };
        if let Err(cause) = form.validate() {
            form.error = cause;
            self.sync_hs();
            return;
        }
        let body = form.value.clone();
        let operation = if !self.hs.editing_code {
            SAVE_HS_CODE_KNOWLEDGE_EXAMPLE
        } else if body["id"].as_i64().unwrap_or(0) > 0 {
            UPDATE_HS_CODE
        } else {
            CREATE_HS_CODE
        };
        if !self.can(operation) {
            return;
        }
        self.start(Work::Request {
            operation,
            parameters: if operation == UPDATE_HS_CODE {
                vec![("code", text(&body, "code"))]
            } else {
                vec![]
            },
            query: vec![],
            body: Some(body),
            reply: "hs:saved".into(),
        });
    }

    fn hs_batch(&mut self, action: &str, tab: i32) {
        if self.hs.checked.is_empty() {
            return;
        }
        let operation = match (action, tab) {
            ("delete-batch", 1) => DELETE_HS_CODES_BATCH,
            ("delete-batch", 2) => DELETE_HS_CODE_KNOWLEDGE_EXAMPLES_BATCH,
            ("ignore-batch", 4) => REVIEW_HS_CODE_REMOTE_CANDIDATES_BATCH,
            ("reset-batch", 4) => RESET_HS_CODE_REMOTE_CANDIDATES,
            _ => return,
        };
        if !self.can(operation) {
            return;
        }
        let body = if action == "ignore-batch" {
            json!({"items":self.hs.checked.iter().map(|id|json!({"id":id,"confirmed":false})).collect::<Vec<_>>()})
        } else {
            json!({"ids":self.hs.checked})
        };
        self.request(operation, 0, vec![], Some(body), "hs:changed");
    }

    pub fn hs_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Hs>();
        match reply {
            "hs:list" => {
                self.hs.rows = value["items"].as_array().cloned().unwrap_or_default();
                self.hs.selected = 0;
                view.set_total_pages(value["totalPages"].as_i64().unwrap_or(1).max(1) as i32);
                view.set_summary(
                    format!(
                        "{} 条 · {}",
                        value["totalCount"]
                            .as_u64()
                            .unwrap_or(self.hs.rows.len() as u64),
                        ["message", "notice", "storagePolicy"]
                            .iter()
                            .filter_map(|key| value[*key].as_str())
                            .collect::<Vec<_>>()
                            .join(" ")
                    )
                    .into(),
                );
            }
            "hs:remote-detail" => {
                if let Some(row) = self.hs.rows.get_mut(self.hs.selected) {
                    *row = value;
                }
            }
            "hs:saved" | "hs:changed" => {
                self.hs.form = None;
                self.hs.checked.clear();
                self.load_lookups = true;
                self.sync_hs();
                self.refresh_hs();
                self.status("知识资料已保存。");
                return;
            }
            "hs:feedback" => {
                self.refresh_hs();
                self.status("反馈已记录。");
                return;
            }
            "hs:reference" => self.status("本地参考资料已保存；已验证税则保留原有状态。"),
            _ => {
                self.hs_file_loaded(reply, value);
                return;
            }
        }
        self.sync_hs();
    }
}
