use super::*;
use crate::DocumentPackage;
use serde_json::json;

impl Desktop {
    pub fn document_package_toggle(&mut self, path: &str, selected: bool) {
        if let Some(template) = self
            .document_package
            .templates
            .iter_mut()
            .find(|template| template.template_path == path)
        {
            template.selected = selected;
        }
        self.sync_document_package();
    }
    pub fn document_package_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<DocumentPackage>();
        let invoice = self.grid.draft.header.id;
        if invoice <= 0 || self.grid.dirty() {
            self.error("请先保存发票,再生成组合单据。");
            return;
        }
        let items = self.document_package.selected_items();
        if items.is_empty() {
            self.error("请至少选择一份单据模板。");
            return;
        }
        match action {
            "preview" => {
                if !self.can(START_INVOICE_DOCUMENT_PACKAGE_DOWNLOAD_JOB) {
                    self.error("当前账号没有组合单据输出权限。");
                    return;
                }
                self.start(Work::DocumentPackagePreview { invoice, items });
            }
            "download" => {
                if !self.can(START_INVOICE_DOCUMENT_PACKAGE_DOWNLOAD_JOB) {
                    self.error("当前账号没有组合单据输出权限。");
                    return;
                }
                let name = format!(
                    "单据组合-{}-{}.zip",
                    self.grid.draft.header.invoice_no,
                    chrono::Local::now().format("%Y%m%d%H%M%S")
                );
                let Some(destination) =
                    self.platform
                        .choose_destination(ui.window(), &name, &["zip"])
                else {
                    self.status("已取消下载");
                    return;
                };
                self.start(Work::FileJob {
                    operation: START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB,
                    parameters: vec![("invoiceId", invoice.to_string())],
                    body: json!({
                        "items": items,
                        "includeMergedPdf": view.get_include_merged_pdf(),
                        "createZip": true,
                        "destinationPath": destination.to_string_lossy()
                    }),
                    destination,
                });
            }
            "folder" => {
                if !self.can(START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB) {
                    self.error("当前账号没有组合单据输出权限。");
                    return;
                }
                let Some(destination) = self.platform.choose_directory(ui.window()) else {
                    self.status("已取消导出");
                    return;
                };
                self.start(Work::FileJob {
                    operation: START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB,
                    parameters: vec![("invoiceId", invoice.to_string())],
                    body: json!({
                        "items": items,
                        "includeMergedPdf": view.get_include_merged_pdf(),
                        "createZip": false,
                        "destinationPath": destination.to_string_lossy()
                    }),
                    destination,
                });
            }
            _ => {}
        }
    }

    pub fn sync_document_package(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<DocumentPackage>();
        view.set_rows(model(
            self.document_package
                .templates
                .iter()
                .map(|template| crate::DocumentPackageRow {
                    name: template.name.clone().into(),
                    template_path: template.template_path.clone().into(),
                    selected: template.selected,
                    with_seal: template.with_seal,
                })
                .collect(),
        ));
        view.set_selected_count(self.document_package.selected_count() as i32);
        view.set_include_merged_pdf(self.document_package.include_merged_pdf);
        view.set_can_preview(
            self.document_package.preview_ready
                && self.can(START_INVOICE_DOCUMENT_PACKAGE_DOWNLOAD_JOB),
        );
        view.set_can_export(self.can(START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB));
    }
}
