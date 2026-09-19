use super::*;
use crate::Hs;
use serde_json::{Value, json};

impl Desktop {
    pub fn hs_file_action(&mut self, action: &str) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Hs>();
        let operation = match action {
            "preview-tariff" => PREVIEW_HS_CODES_IMPORT_UPLOAD,
            "commit-tariff" => COMMIT_HS_CODES_IMPORT,
            "export-package" => EXPORT_HS_CODE_KNOWLEDGE,
            "import-package" => IMPORT_HS_CODE_KNOWLEDGE,
            _ => return,
        };
        if !self.can(operation) {
            return;
        }
        if action == "commit-tariff" {
            if let Some(preview) = &self.hs.preview {
                self.request(
                    operation,
                    0,
                    vec![],
                    Some(json!({"token":preview["token"]})),
                    "hs:tariff-committed",
                );
            }
        } else if action == "export-package" {
            if let Some(destination) =
                self.platform
                    .choose_destination(ui.window(), "HS编码知识.zip", &["zip"])
            {
                let since = view.get_since().trim().to_owned();
                self.start(Work::BinarySave {
                    operation,
                    parameters: vec![],
                    query: if since.is_empty() {
                        vec![]
                    } else {
                        vec![("since", since)]
                    },
                    destination,
                    limit: 100 * 1024 * 1024,
                });
            }
        } else {
            let tariff = action == "preview-tariff";
            let metadata = if tariff {
                let Ok(year) = view.get_year().trim().parse::<i32>() else {
                    self.error("请输入有效的税则年度。");
                    return;
                };
                if !(2000..=2100).contains(&year) {
                    self.error("税则年度须为 2000 至 2100。");
                    return;
                }
                json!({"mode":if view.get_import_mode()==1 {"CompleteSnapshot"}else{"Incremental"},"sourceName":view.get_source_name().to_string(),"effectiveYear":year})
            } else {
                json!({})
            };
            if let Some(source) = self.platform.choose_source(
                ui.window(),
                if tariff {
                    "年度税则文件"
                } else {
                    "HS 知识包"
                },
                if tariff {
                    &["csv", "xlsx", "xlsm", "xls"]
                } else {
                    &["zip"]
                },
            ) {
                if tariff {
                    self.hs.preview = None;
                    self.sync_hs();
                }
                self.start(Work::Upload {
                    operation,
                    parameters: vec![],
                    metadata,
                    source,
                    limit: if tariff { 25 } else { 100 } * 1024 * 1024,
                    reply: if tariff {
                        "hs:tariff-preview"
                    } else {
                        "hs:package-imported"
                    }
                    .into(),
                });
            }
        }
    }

    pub fn hs_file_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Hs>();
        match reply {
            "hs:tariff-preview" => {
                view.set_import_summary(
                    format!(
                        "{} · 新增 {} · 更新 {} · 未变 {} · 疑似作废 {} · 冲突 {} · 无效 {}",
                        value["fileName"].as_str().unwrap_or(""),
                        value["addCount"],
                        value["updateCount"],
                        value["unchangedCount"],
                        value["suspectedObsoleteCount"],
                        value["conflictCount"],
                        value["invalidCount"]
                    )
                    .into(),
                );
                self.hs.preview = Some(value);
                view.set_page(1);
                view.set_total_pages(1);
                view.set_summary("预检未更改税则，确认后一次性提交；冲突和无效行不写入。".into());
                self.refresh_hs();
                return;
            }
            "hs:tariff-committed" | "hs:package-imported" => {
                self.hs.preview = None;
                self.hs.rows.clear();
                self.hs.selected = 0;
                self.load_lookups = true;
                let message = value["message"].as_str().unwrap_or("知识包已导入。");
                view.set_import_summary(message.into());
                view.set_summary(message.into());
                self.status(message);
            }
            _ => return,
        }
        self.sync_hs();
    }
}
