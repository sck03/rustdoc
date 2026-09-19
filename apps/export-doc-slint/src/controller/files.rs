use super::*;
use crate::{DataRow, Metric};
use serde_json::{Value, json};

impl Desktop {
    pub fn cancel_operation(&mut self) {
        if let Some(task) = &self.task {
            task.cancel();
            self.status("正在取消并清理本次操作");
        }
    }
    pub fn excel_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let operation = match action {
            "import" => PREVIEW_EXCEL_IMPORT,
            "template" => START_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
            "blank-booking" => START_BLANK_BOOKING_SHEET_SAVE_TO_PATH_JOB,
            "booking" => START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
            "convert" => START_BOOKING_SHEET_CONVERT_SAVE_TO_PATH_JOB,
            _ => return,
        };
        if !self.can(operation) {
            self.error("当前账号没有执行此 Excel 操作的权限。");
            return;
        }
        if action == "import" {
            if !self.can(CREATE_INVOICE) {
                self.error("当前账号没有创建发票的权限。");
                return;
            }
            if self.has_unsaved() {
                self.error("请先保存当前草稿，再导入新的发票。");
                return;
            }
            if let Some(path) = self.platform.choose_excel_source(ui.window()) {
                self.request(
                    operation,
                    0,
                    vec![],
                    Some(json!({"filePath":path})),
                    "excel-preview",
                );
            }
            return;
        }
        if action == "booking" && (self.grid.draft.header.id <= 0 || self.grid.dirty()) {
            self.error("请先保存发票，再导出订舱托单。");
            return;
        }
        let mut body = json!({});
        if action == "convert" {
            let Some(source) = self.platform.choose_excel_source(ui.window()) else {
                return;
            };
            body["sourcePath"] = json!(source);
        }
        let name = match action {
            "template" => "导入数据模板.xlsx",
            "blank-booking" => "空白托单模板.xlsx",
            _ => "订舱托单.xlsx",
        };
        let Some(destination) = self
            .platform
            .choose_destination(ui.window(), name, &["xlsx"])
        else {
            return;
        };
        body["destinationPath"] = json!(destination);
        self.start(Work::FileJob {
            operation,
            parameters: crate::worker::parameters(operation, self.grid.draft.header.id),
            body,
            destination,
        });
        self.status("正在生成 Excel 文件，可取消或在文件任务中查看结果");
    }
    pub fn show_import(&mut self, value: Value) {
        match serde_json::from_value::<ApiExcelImportPreviewResponse>(value) {
            Ok(preview) => {
                let Some(ui) = self.ui.upgrade() else {
                    return;
                };
                let app = ui.global::<App>();
                let Some(invoice) = preview.invoice.as_ref() else {
                    self.error(preview.errors.join("\n"));
                    return;
                };
                app.set_import_source(preview.source_path.clone().into());
                app.set_import_success(preview.success);
                app.set_import_notice(if preview.errors.is_empty() {
                    "请核对抬头、商品明细与金额，确认后继续编辑；保存发票前不会写入业务数据。"
                        .into()
                } else {
                    preview.errors.join("\n").into()
                });
                app.set_import_metrics(model(vec![
                    Metric {
                        label: "发票号".into(),
                        value: invoice.invoice_no.clone().into(),
                    },
                    Metric {
                        label: "客户".into(),
                        value: invoice.customer_name_en.clone().into(),
                    },
                    Metric {
                        label: "商品行数".into(),
                        value: invoice.items.len().to_string().into(),
                    },
                    Metric {
                        label: "总金额".into(),
                        value: format!("{} {}", invoice.currency, invoice.total_amount).into(),
                    },
                ]));
                app.set_import_rows(model(
                    invoice
                        .items
                        .iter()
                        .enumerate()
                        .take(200)
                        .map(|(index, item)| DataRow {
                            id: index as i32 + 1,
                            cells: model(vec![
                                item.style_no.clone().into(),
                                item.style_name.clone().into(),
                                item.quantity.to_string().into(),
                                item.unit_price.to_string().into(),
                                item.total_price.to_string().into(),
                            ]),
                        })
                        .collect(),
                ));
                app.set_import_open(true);
                self.import_preview = Some(preview);
            }
            Err(error) => self.error(error.to_string()),
        }
    }
    pub fn finish_import(&mut self, accepted: bool) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_import_open(false);
        }
        let Some(preview) = self.import_preview.take() else {
            return;
        };
        if !accepted || !preview.success || !self.can(CREATE_INVOICE) {
            return;
        }
        if let Some(invoice) = preview.invoice {
            let blank = InvoiceDraft::new(&invoice.invoice_date);
            self.open_invoice(InvoiceDraft::from_dto(invoice));
            let imported = self.grid.draft.clone();
            self.grid = InvoiceGrid::new(blank, 0);
            self.grid.replace(imported);
            self.sync_invoice();
            self.sync_grid();
            self.sync_invoice_fields();
            self.status("已导入待保存草稿，请核对后保存发票");
        }
    }
    pub fn file_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        if action == "clear-finished" {
            self.request(
                CLEAR_FINISHED_JOBS,
                0,
                vec![],
                Some(json!({})),
                "task-action",
            );
            return;
        }
        let Some(selected) = self.workspace.selected.as_ref() else {
            return;
        };
        let Some(id) = selected["jobId"].as_str().map(str::to_owned) else {
            return;
        };
        if action == "download" {
            let Some(ui) = self.ui.upgrade() else {
                return;
            };
            let name = selected["outputPath"].as_str().unwrap_or("");
            if !export_doc_engine::paths::valid_file_name(name) {
                self.error("此任务没有可保存的输出文件。");
                return;
            }
            let extension = std::path::Path::new(name)
                .extension()
                .and_then(|s| s.to_str())
                .unwrap_or("bin");
            if let Some(destination) =
                self.platform
                    .choose_destination(ui.window(), name, &[extension])
            {
                self.start(Work::SaveJobOutput {
                    job_id: id,
                    destination,
                });
            }
        } else {
            let operation = match action {
                "cancel" => CANCEL_JOB,
                "retry" => RETRY_JOB,
                "delete" => DELETE_JOB,
                _ => return,
            };
            self.start(Work::Request {
                operation,
                parameters: vec![("jobId", id)],
                query: vec![],
                body: None,
                reply: "task-action".into(),
            });
        }
    }
}
