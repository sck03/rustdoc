use super::*;
use crate::{SingleWindow, single_window_model::text};
use export_doc_domain::single_window::{self as rules, Business};
use serde_json::Value;

impl Desktop {
    pub fn single_window_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<SingleWindow>();
        match reply {
            "sw:batches" => {
                self.single_window.batches = value["rows"].as_array().cloned().unwrap_or_default();
                view.set_total_pages(value["totalPages"].as_i64().unwrap_or(1).max(1) as i32);
                if [1, 2].contains(&view.get_tab())
                    && self.single_window.invoice_id > 0
                    && self.single_window.form.is_none()
                {
                    self.refresh_single_window();
                }
            }
            "sw:batch" => {
                self.single_window.detail = Some(value);
                self.single_window.receipt_files.clear();
            }
            "sw:invoices" => {
                self.single_window.invoices = value["items"]
                    .as_array()
                    .or_else(|| value["rows"].as_array())
                    .cloned()
                    .unwrap_or_default();
            }
            "sw:document" | "sw:saved" | "sw:unlocked" => {
                let item = self.single_window.selected_item;
                self.single_window.open(if reply == "sw:document" {
                    value
                } else {
                    value["document"].clone()
                });
                if let Some(form) = &self.single_window.form {
                    self.single_window.selected_item = item.min(
                        form.value["items"]
                            .as_array()
                            .map_or(0, |r| r.len().saturating_sub(1)),
                    );
                }
                if reply == "sw:saved" {
                    self.status("申报草稿已保存。");
                }
            }
            "sw:defaults" => {
                if let Some(form) = &mut self.single_window.form {
                    let before = form.value.clone();
                    for (key, default) in value.as_object().into_iter().flatten() {
                        if form.value[key]
                            .as_str()
                            .is_some_and(|s| s.trim().is_empty())
                            && default.is_string()
                        {
                            form.value[key] = default.clone();
                        }
                    }
                    if let Some(items) = form.value["items"].as_array_mut() {
                        for row in items {
                            let identity = rules::draft::identity(row);
                            if let Some(default) = value["items"]
                                .as_array()
                                .into_iter()
                                .flatten()
                                .find(|r| rules::draft::identity(r) == identity)
                            {
                                for (key, v) in row.as_object_mut().into_iter().flatten() {
                                    if v.as_str().is_some_and(|s| s.trim().is_empty())
                                        && default[key].is_string()
                                    {
                                        *v = default[key].clone();
                                    }
                                }
                            }
                        }
                    }
                    self.single_window.history.record(before, &form.value, None);
                    form.buffers.clear();
                }
                self.status("空白字段已补齐，核对后保存。");
            }
            "sw:locks" => {
                self.single_window.locks = value["fields"].as_array().cloned().unwrap_or_default();
                view.set_selected_lock(0);
            }
            "sw:review" => self.single_window.review = value,
            "sw:repaired" => {
                self.status(text(&value, "message"));
                self.single_window.review = value["review"].clone();
                self.refresh_single_window();
            }
            "sw:profiles" => {
                self.single_window.profiles =
                    value["profiles"].as_array().cloned().unwrap_or_default();
                if let Some(profile) = self
                    .single_window
                    .profiles
                    .iter()
                    .find(|p| p["isActive"] == true)
                {
                    let selected = self
                        .single_window
                        .profiles
                        .iter()
                        .position(|r| r["id"] == profile["id"])
                        .unwrap_or(0);
                    view.set_selected_profile(selected as i32 + 1);
                    self.single_window.profile = Some(FormModel::new(
                        export_doc_engine::contracts::schema(
                            "ApiSingleWindowClientProfileSaveRequest",
                        ),
                        profile.clone(),
                        Lookups::new(),
                    ));
                }
            }
            "sw:dictionary" | "sw:dictionary-saved" => {
                self.single_window.catalog = value["catalog"].clone();
                self.single_window.catalog_baseline = self.single_window.catalog.clone();
                self.single_window.dictionary_form = None;
                view.set_selected_dictionary_row(0);
            }
            "sw:producers" => {
                self.single_window.producers =
                    value["items"].as_array().cloned().unwrap_or_default();
                view.set_selected_producer(0);
                view.set_producers_open(true);
            }
            "sw:producer-saved" => self.status(text(&value, "message")),
            "sw:collect" => {
                self.single_window.receipt_files = value["receiptFiles"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .filter_map(Value::as_str)
                    .map(std::path::PathBuf::from)
                    .collect();
                self.status(format!(
                    "已匹配 {} 份回执。",
                    self.single_window.receipt_files.len()
                ));
            }
            "sw:package" | "sw:imported" | "sw:dispatched" => {
                self.status(value["message"].as_str().unwrap_or("批次操作已完成。"));
                view.set_tab(0);
                self.refresh_single_window();
            }
            _ => {}
        }
        self.sync_single_window();
    }
    pub fn open_single_window_invoice(&mut self, id: i64, business: Business) {
        self.navigate_now("single-window");
        // The initial list request must finish before the source document request.
        self.single_window.invoice_id = id;
        self.single_window.business = business;
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<SingleWindow>()
                .set_tab(if business == Business::Coo { 1 } else { 2 });
        }
    }
}
