use super::*;
use crate::InvoiceFiles;
use serde_json::{Value, json};
impl Desktop {
    pub fn sync_invoice_files(&self) {
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<InvoiceFiles>();
            view.set_can_import(self.can(PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE));
            view.set_can_export(self.can(DOWNLOAD_INVOICE_TRANSFER_PACKAGE));
        }
    }
    pub fn invoice_file_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<InvoiceFiles>();
        match action {
            "cancel" => {
                view.set_open(false);
                self.transfer_source = None;
            }
            "choose" => {
                if !self.can(PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE) {
                    return;
                }
                if self.has_unsaved() {
                    self.error("请先保存当前草稿，再导入单据包。");
                    return;
                }
                if let Some(source) =
                    self.platform
                        .choose_source(ui.window(), "单据交换包", &["edpkg"])
                {
                    self.start(Work::InvoicePackage(source));
                }
            }
            "export" => {
                if !self.can(DOWNLOAD_INVOICE_TRANSFER_PACKAGE) {
                    return;
                }
                let id = ui.global::<App>().get_selected_id() as i64;
                if id <= 0 {
                    return;
                }
                if let Some(destination) =
                    self.platform
                        .choose_destination(ui.window(), "单据.edpkg", &["edpkg"])
                {
                    self.start(Work::BinarySave {
                        operation: DOWNLOAD_INVOICE_TRANSFER_PACKAGE,
                        parameters: vec![("id", id.to_string())],
                        query: vec![],
                        destination,
                        limit: 25 * 1024 * 1024,
                    });
                }
            }
            "import" => {
                if !view.get_valid() || !self.can(IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE) {
                    return;
                }
                if view.get_conflict() && matches!(view.get_action_index(), 1 | 3) {
                    self.confirm(
                        Pending::InvoicePackageImport,
                        "此操作将修改已有草稿。请确认已核对包内单据及冲突处理方式。",
                    );
                } else {
                    self.import_invoice_package();
                }
            }
            _ => {}
        }
    }
    pub fn invoice_package_loaded(&mut self, name: String, bytes: Arc<Vec<u8>>, preview: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<InvoiceFiles>();
        let p = &preview["preview"];
        view.set_name(name.clone().into());
        view.set_valid(preview["checksumValid"] == true);
        view.set_conflict(p["invoiceExists"] == true);
        view.set_action_index(0);
        view.set_new_number("".into());
        view.set_summary(
            format!(
                "{}\n发票号：{}  ·  {}  ·  {} 行商品\n{}\n客户：{}；出口商：{}",
                preview["checksumMessage"]
                    .as_str()
                    .unwrap_or("校验信息缺失"),
                p["invoiceNo"].as_str().unwrap_or(""),
                p["type"].as_str().unwrap_or(""),
                p["itemCount"],
                if p["invoiceMatches"] == true {
                    "已有内容相同的单据，可直接跳过。"
                } else if p["invoiceExists"] == true {
                    "已有同号同类型单据，请选择处理方式。"
                } else {
                    "将导入为新草稿，保存前会重新校验完整单据。"
                },
                if p["customerExists"] == true {
                    "沿用现有资料"
                } else {
                    "按包内资料匹配或新建"
                },
                if p["exporterExists"] == true {
                    "沿用现有资料"
                } else {
                    "按包内资料匹配或新建"
                }
            )
            .into(),
        );
        self.transfer_source = Some((name, bytes));
        view.set_open(true);
    }
    pub fn import_invoice_package(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<InvoiceFiles>();
        let Some((name, bytes)) = self.transfer_source.clone() else {
            return;
        };
        let action = ["Skip", "Overwrite", "NewInvoiceNo", "AppendItems"]
            .get(view.get_action_index() as usize)
            .copied()
            .unwrap_or("Skip");
        self.start(Work::ImportInvoicePackage {
            name,
            bytes,
            body: json!({"conflictAction":action,"newInvoiceNo":view.get_new_number().to_string()}),
        });
    }
    pub fn invoice_package_imported(&mut self, value: Value) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<InvoiceFiles>().set_open(false);
        }
        self.transfer_source = None;
        self.load_lookups = true;
        if let Some(id) = value["result"]["invoiceId"].as_i64().filter(|id| *id > 0) {
            self.pending_open = Some(("invoices", id));
        }
        self.navigate_now("invoices");
        self.status(value["message"].as_str().unwrap_or("单据包处理完成。"));
    }
}
