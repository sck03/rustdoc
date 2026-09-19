use super::*;
use crate::{DataRow, PartyFiles};
use serde_json::{Value, json};

impl Desktop {
    pub fn sync_party_files(&self) {
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<PartyFiles>();
            let supplier = self.workspace.resource == "suppliers";
            let enabled = supplier || self.workspace.resource == "crm-customers";
            view.set_can_import(
                enabled
                    && self.can(if supplier {
                        PREVIEW_SUPPLIER_IMPORT
                    } else {
                        PREVIEW_CRM_CUSTOMER_IMPORT
                    }),
            );
            view.set_can_export(
                enabled
                    && self.can(if supplier {
                        EXPORT_SUPPLIERS
                    } else {
                        EXPORT_CRM_CUSTOMERS
                    }),
            );
        }
    }
    pub fn party_file_action(&mut self, action: &str, index: i32) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<PartyFiles>();
        let supplier = self.workspace.resource == "suppliers";
        if !supplier && self.workspace.resource != "crm-customers" {
            return;
        }
        match action {
            "choose" => {
                if self.has_unsaved() {
                    self.error("请先保存当前资料，再开始导入。");
                    return;
                }
                let operation = if supplier {
                    PREVIEW_SUPPLIER_IMPORT
                } else {
                    PREVIEW_CRM_CUSTOMER_IMPORT
                };
                if !self.can(operation) {
                    return;
                }
                if let Some(source) = self.platform.choose_source(
                    ui.window(),
                    "客户与供应商文件",
                    &["csv", "xlsx", "xlsm"],
                ) {
                    self.start(Work::Upload {
                        operation,
                        parameters: vec![],
                        metadata: json!({}),
                        source,
                        limit: 25 * 1024 * 1024,
                        reply: "party:preview".into(),
                    });
                }
            }
            "confirm" => {
                let operation = if supplier {
                    IMPORT_SUPPLIERS
                } else {
                    IMPORT_CRM_CUSTOMERS
                };
                if !self.can(operation) {
                    return;
                }
                if let Some(preview) = &self.party_preview {
                    self.request(
                        operation,
                        0,
                        vec![],
                        Some(json!({"previewId":preview["previewId"]})),
                        "party:imported",
                    );
                }
            }
            "export" => {
                let operation = if supplier {
                    EXPORT_SUPPLIERS
                } else {
                    EXPORT_CRM_CUSTOMERS
                };
                if !self.can(operation) {
                    return;
                }
                if let Some(destination) = self.platform.choose_destination(
                    ui.window(),
                    if supplier {
                        "供应商.xlsx"
                    } else {
                        "CRM客户.xlsx"
                    },
                    &["xlsx"],
                ) {
                    let mut query = vec![("keyword", ui.global::<App>().get_search().to_string())];
                    let statuses = if supplier {
                        export_doc_domain::party::SUPPLIER_STATUSES
                    } else {
                        export_doc_domain::crm::CUSTOMER_STATUSES
                    };
                    if let Some(status) = ui
                        .global::<crate::Crm>()
                        .get_status_index()
                        .checked_sub(1)
                        .and_then(|index| statuses.get(index as usize))
                    {
                        query.push(("status", (*status).into()));
                    }
                    self.start(Work::BinarySave {
                        operation,
                        parameters: vec![],
                        query,
                        destination,
                        limit: 32 * 1024 * 1024,
                    });
                }
            }
            "select" => {
                if let Some(row) = self
                    .party_preview
                    .as_ref()
                    .and_then(|preview| preview["rows"].as_array())
                    .and_then(|rows| rows.get(index.saturating_sub(1) as usize))
                {
                    view.set_selected_id(index);
                    view.set_detail(
                        [
                            ("name", "名称"),
                            ("countryRegion", "国家/地区"),
                            ("website", "网站"),
                            ("status", "状态"),
                            ("source", "来源"),
                            ("category", "分类"),
                            ("mainProducts", "主要产品"),
                            ("notes", "备注"),
                            ("contactName", "联系人"),
                            ("contactTitle", "职位"),
                            ("contactEmail", "邮箱"),
                            ("contactPhone", "电话"),
                            ("error", "问题"),
                        ]
                        .iter()
                        .filter_map(|(key, label)| {
                            row[*key]
                                .as_str()
                                .filter(|value| !value.is_empty())
                                .map(|value| format!("{label}：{value}"))
                        })
                        .collect::<Vec<_>>()
                        .join("\n")
                        .into(),
                    );
                }
            }
            "back" => {
                self.party_preview = None;
                ui.global::<App>().set_page("records".into());
                self.refresh();
            }
            _ => {}
        }
    }
    pub fn party_file_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<PartyFiles>();
        if reply == "party:preview" {
            let rows = value["rows"].as_array().cloned().unwrap_or_default();
            view.set_title(
                if self.workspace.resource == "suppliers" {
                    "供应商导入预检"
                } else {
                    "客户导入预检"
                }
                .into(),
            );
            view.set_summary(format!("共 {} 行，可导入 {} 行，重复 {} 行。预检保留 30 分钟，确认时重新校验并跳过无效或重复记录。",value["totalRows"],value["validRows"],value["duplicateRows"]).into());
            view.set_can_confirm(value["validRows"].as_i64().unwrap_or(0) > 0);
            view.set_selected_id(0);
            view.set_detail("".into());
            view.set_rows(model(
                rows.iter()
                    .enumerate()
                    .map(|(index, row)| DataRow {
                        id: index as i32 + 1,
                        cells: model(
                            [
                                "rowNumber",
                                "name",
                                "countryRegion",
                                "status",
                                "contactName",
                                "contactEmail",
                            ]
                            .iter()
                            .map(|key| export_doc_engine::workspace::display(&row[*key]).into())
                            .chain(std::iter::once(if row["error"] != "" {
                                row["error"].as_str().unwrap_or("格式错误").into()
                            } else if row["isDuplicate"] == true {
                                "重复，跳过".into()
                            } else {
                                "可导入".into()
                            }))
                            .collect(),
                        ),
                    })
                    .collect(),
            ));
            self.party_preview = Some(value);
            ui.global::<App>().set_page("party-import".into());
        } else {
            let count = value["createdCustomers"]
                .as_i64()
                .or_else(|| value["createdSuppliers"].as_i64())
                .unwrap_or(0);
            let skipped = value["skippedDuplicates"]
                .as_i64()
                .or_else(|| value["skippedRows"].as_i64())
                .unwrap_or(0);
            self.party_preview = None;
            self.load_lookups = true;
            ui.global::<App>().set_page("records".into());
            self.refresh();
            self.status(format!(
                "已导入 {count} 条资料、{} 位联系人，跳过 {skipped} 行。",
                value["createdContacts"]
            ));
        }
    }
}
