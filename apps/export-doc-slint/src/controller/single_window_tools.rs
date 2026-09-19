use super::*;
use crate::{
    SingleWindow,
    single_window_model::{self as sw, text},
};
use export_doc_engine::contracts;
use serde_json::json;

impl Desktop {
    pub fn single_window_tools(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<SingleWindow>();
        match action {
            "new-profile" => {
                let mut value = contracts::initial(contracts::schema(
                    "ApiSingleWindowClientProfileSaveRequest",
                ));
                value["companyScope"] = json!(
                    self.user
                        .as_ref()
                        .map(|u| u.company_scope.as_str())
                        .unwrap_or("")
                );
                value["canSubmitCustomsCoo"] = json!(true);
                value["canSubmitAgentConsignment"] = json!(true);
                self.single_window.profile = Some(FormModel::new(
                    contracts::schema("ApiSingleWindowClientProfileSaveRequest"),
                    value,
                    Lookups::new(),
                ));
                view.set_selected_profile(0);
            }
            "profile" => {
                if let Some(row) = index
                    .checked_sub(1)
                    .and_then(|i| self.single_window.profiles.get(i as usize))
                {
                    view.set_selected_profile(index);
                    self.single_window.profile = Some(FormModel::new(
                        contracts::schema("ApiSingleWindowClientProfileSaveRequest"),
                        row.clone(),
                        Lookups::new(),
                    ));
                }
            }
            "save-profile" => {
                if let Some(form) = &mut self.single_window.profile {
                    if let Err(cause) = form.validate() {
                        self.error(cause);
                        return;
                    }
                    let value = contracts::project(
                        contracts::schema("ApiSingleWindowClientProfileSaveRequest"),
                        form.value.clone(),
                    );
                    self.request(
                        SAVE_SINGLE_WINDOW_CLIENT_PROFILE,
                        0,
                        vec![],
                        Some(value),
                        "sw:profiles",
                    );
                }
            }
            "activate-profile" | "copy-assignment" => {
                if let Some(row) = index
                    .checked_sub(1)
                    .and_then(|i| self.single_window.profiles.get(i as usize))
                {
                    if action == "activate-profile" {
                        self.start(Work::Request {
                            operation: ACTIVATE_SINGLE_WINDOW_CLIENT_PROFILE,
                            parameters: vec![("profileKey", text(row, "profileKey"))],
                            query: vec![],
                            body: None,
                            reply: "sw:profiles".into(),
                        });
                    } else {
                        let result = self
                            .platform
                            .copy_text(ui.window(), text(row, "stationAssignmentCode"));
                        if let Err(cause) = result {
                            self.error(cause);
                        } else {
                            self.status("持卡机授权码已复制，可用于该公司申报交接。");
                        }
                    }
                }
            }
            "dictionary" => {
                self.single_window.dictionary = index.clamp(0, 5) as usize;
                self.single_window.dictionary_form = None;
                view.set_selected_dictionary_row(0);
            }
            "dictionary-row" | "new-dictionary-row" => {
                view.set_selected_dictionary_row(if action == "new-dictionary-row" {
                    0
                } else {
                    index
                });
                self.single_window
                    .select_dictionary(if action == "new-dictionary-row" {
                        None
                    } else {
                        index.checked_sub(1).map(|i| i as usize)
                    });
            }
            "apply-dictionary-row" => {
                if let Err(cause) = self.single_window.save_dictionary_entry() {
                    self.error(cause);
                }
            }
            "remove-dictionary-row" => {
                if let Some(rows) = self.single_window.catalog
                    [sw::DICTIONARIES[self.single_window.dictionary].0]
                    .as_array_mut()
                {
                    if let Some(i) = index
                        .checked_sub(1)
                        .filter(|i| *i >= 0 && (*i as usize) < rows.len())
                    {
                        rows.remove(i as usize);
                    }
                }
                self.single_window.dictionary_form = None;
                view.set_selected_dictionary_row(0);
            }
            "save-dictionary" => {
                if self
                    .single_window
                    .dictionary_form
                    .as_ref()
                    .is_some_and(|f| f.value != f.baseline)
                {
                    if let Err(cause) = self.single_window.save_dictionary_entry() {
                        self.error(cause);
                        return;
                    }
                }
                self.request(
                    UPDATE_SINGLE_WINDOW_REFERENCE_CATALOG,
                    0,
                    vec![],
                    Some(json!({"catalog":self.single_window.catalog})),
                    "sw:dictionary-saved",
                );
            }
            "reset-dictionary" => self.request(
                RESET_SINGLE_WINDOW_REFERENCE_CATALOG,
                0,
                vec![],
                None,
                "sw:dictionary-saved",
            ),
            "export-submit" => {
                if self.single_window.form.as_ref().is_none_or(|f| {
                    f.value != f.baseline || f.value["id"].as_i64().unwrap_or(0) <= 0
                }) {
                    self.error("请先保存当前申报草稿，再生成提交包。");
                    return;
                }
                if let Some(path) =
                    self.platform
                        .choose_destination(ui.window(), "单一窗口提交包.zip", &["zip"])
                {
                    self.request(self.single_window_operation(SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH),self.single_window.invoice_id,vec![],Some(json!({"packagePath":path,"stationAssignmentCode":view.get_assignment_code().as_str()})),"sw:package");
                }
            }
            "import-submit" | "import-receipt" => {
                if let Some(path) =
                    self.platform
                        .choose_source(ui.window(), "单一窗口交接包", &["zip"])
                {
                    self.start(Work::Upload {
                        operation: if action == "import-submit" {
                            UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE
                        } else {
                            UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE
                        },
                        parameters: vec![],
                        metadata: json!({"workingDirectory":"","keepWorkingDirectory":false}),
                        source: path,
                        limit: 100 * 1024 * 1024,
                        reply: "sw:imported".into(),
                    });
                }
            }
            "dispatch" | "collect" => {
                if let Some(detail) = &self.single_window.detail {
                    let body = json!({"batchId":detail["batchId"]});
                    self.request(
                        if action == "dispatch" {
                            DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT
                        } else {
                            COLLECT_SINGLE_WINDOW_CLIENT_RECEIPTS
                        },
                        0,
                        vec![],
                        Some(body),
                        if action == "dispatch" {
                            "sw:dispatched"
                        } else {
                            "sw:collect"
                        },
                    );
                }
            }
            "add-receipt" => {
                if self.single_window.receipt_files.len() >= 200 {
                    self.error("一次最多选择 200 个回执文件。");
                    return;
                }
                if let Some(path) =
                    self.platform
                        .choose_source(ui.window(), "官方 XML 回执", &["xml"])
                {
                    if !self.single_window.receipt_files.contains(&path) {
                        self.single_window.receipt_files.push(path);
                    }
                }
            }
            "clear-receipts" => self.single_window.receipt_files.clear(),
            "export-receipt" => {
                if let Some(detail) = &self.single_window.detail {
                    if let Some(path) = self.platform.choose_destination(
                        ui.window(),
                        "单一窗口回执包.zip",
                        &["zip"],
                    ) {
                        self.request(SAVE_SINGLE_WINDOW_RECEIPT_PACKAGE_TO_PATH,0,vec![],Some(json!({"businessType":detail["businessType"],"batchReference":detail["batchReference"],"invoiceNo":detail["invoiceNo"],"receiptFiles":self.single_window.receipt_files,"packagePath":path})),"sw:package");
                    }
                }
            }
            _ => {}
        }
    }
}
