use super::*;
use serde_json::{Value, json};
impl Desktop {
    pub fn credit_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        if action == "credit-import" {
            if !app.get_invoice_editable() || !self.can(UPLOAD_LETTER_OF_CREDIT_DOCUMENT) {
                return;
            }
            if let Some(source) = self.platform.choose_source(
                ui.window(),
                "导入信用证",
                &[
                    "pdf", "txt", "md", "png", "jpg", "jpeg", "bmp", "gif", "tif", "tiff", "webp",
                    "csv", "json", "xml",
                ],
            ) {
                self.start(Work::Upload {
                    operation: UPLOAD_LETTER_OF_CREDIT_DOCUMENT,
                    parameters: vec![],
                    metadata: json!({}),
                    source,
                    limit: 25 * 1024 * 1024,
                    reply: "credit:import".into(),
                });
            }
        } else if action == "credit-review" {
            if !self.can(REVIEW_LETTER_OF_CREDIT_COMPLIANCE) {
                return;
            }
            match self.grid.draft.build() {
                Ok(invoice) => self.request(
                    REVIEW_LETTER_OF_CREDIT_COMPLIANCE,
                    0,
                    vec![],
                    Some(json!({"invoice":invoice})),
                    "credit:review",
                ),
                Err(cause) => self.error(cause),
            }
        } else if action == "credit-copy" {
            if let Err(cause) = self
                .platform
                .copy_text(ui.window(), app.get_credit_report().to_string())
            {
                self.error(cause);
            }
        }
    }
    pub fn credit_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        if reply == "credit:import" {
            let before = self.grid.draft.clone();
            self.grid.draft.header.letter_of_credit_content =
                value["extractedText"].as_str().unwrap_or("").into();
            self.grid.draft.header.letter_of_credit_source_path =
                value["sourcePath"].as_str().unwrap_or("").into();
            self.grid.history.record(before, &self.grid.draft, None);
            self.invoice_form.value = json!(self.grid.draft.header);
            self.invoice_form.buffers.clear();
            app.set_credit_report("".into());
            self.status("信用证文本已导入草稿，请核对后保存发票。");
            self.sync_invoice();
            self.sync_invoice_fields();
        } else {
            app.set_credit_report(value["reportText"].as_str().unwrap_or("").into());
            app.set_credit_summary(
                format!(
                    "{}{}",
                    value["contextSummary"].as_str().unwrap_or(""),
                    if value["letterOfCreditContentTruncated"] == true {
                        " · 原文超过请求上限，已截断"
                    } else {
                        ""
                    }
                )
                .into(),
            );
        }
    }
}
