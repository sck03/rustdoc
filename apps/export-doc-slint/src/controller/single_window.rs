use super::*;
use crate::{
    SingleWindow,
    single_window_model::{self as sw, rows, text},
};
use export_doc_domain::single_window::{self as rules, Business};
use serde_json::{Value, json};

impl Desktop {
    pub fn setup_single_window(&mut self) {
        self.single_window = Default::default();
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        ui.global::<App>().set_tabs(model(vec![]));
        ui.global::<App>().set_page("single-window".into());
        let view = ui.global::<SingleWindow>();
        view.set_tab(0);
        view.set_document_tab(0);
        view.set_page(1);
        view.set_keyword("".into());
        view.set_selected_batch(0);
        view.set_business_filter(0);
        view.set_status_filter(0);
        view.set_invoice_index(-1);
        view.set_assignment_code("".into());
        view.set_producers_open(false);
        self.sync_single_window();
        self.refresh_single_window();
    }
    pub fn single_window_operation(&self, coo: Operation, acd: Operation) -> Operation {
        if self.single_window.business == Business::Coo {
            coo
        } else {
            acd
        }
    }
    pub fn sync_single_window(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<SingleWindow>();
        view.set_can_edit(self.can(
            self.single_window_operation(
                SAVE_CUSTOMS_COO_DOCUMENT,
                SAVE_AGENT_CONSIGNMENT_DOCUMENT,
            ),
        ));
        view.set_can_export(self.can(self.single_window_operation(
            SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
            SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
        )));
        view.set_can_dictionary(self.can(GET_SINGLE_WINDOW_REFERENCE_CATALOG));
        view.set_can_profiles(self.can(GET_SINGLE_WINDOW_CLIENT_PROFILES));
        view.set_has_document(self.single_window.form.is_some());
        let value = self
            .single_window
            .form
            .as_ref()
            .map(|f| &f.value)
            .cloned()
            .unwrap_or(Value::Null);
        view.set_summary(
            format!(
                "发票：{}　合同：{}　状态：{}　草稿版本：{}　人工锁定：{}　来源差异：{}",
                text(&value, "invoiceNo"),
                text(&value, "contractNo"),
                sw::status(&text(&value, "status")),
                text(&value, "draftRevision"),
                text(&value, "manualLockedFieldCount"),
                text(&value, "sourceDiffCount")
            )
            .into(),
        );
        view.set_document_sections(model(self.single_window.sections(false)));
        view.set_goods_sections(model(self.single_window.sections(true)));
        view.set_goods(model(rows(
            value["items"]
                .as_array()
                .map(Vec::as_slice)
                .unwrap_or_default(),
            &[
                "gNo",
                "sourceStyleNo",
                "hsCode",
                "goodsName",
                "goodsQty",
                "oriCriteria",
            ],
        )));
        view.set_selected_good(self.single_window.selected_item as i32 + 1);
        view.set_attachments(model(rows(
            value["attachments"]
                .as_array()
                .map(Vec::as_slice)
                .unwrap_or_default(),
            &["fileName", "fileType", "description"],
        )));
        view.set_corps(model(rows(
            value["nonpartyCorps"]
                .as_array()
                .map(Vec::as_slice)
                .unwrap_or_default(),
            &["entName", "entAddr", "entCountryName"],
        )));
        view.set_attachment_fields(model(
            self.single_window
                .child_fields("attachments", self.single_window.selected_attachment),
        ));
        view.set_corp_fields(model(
            self.single_window
                .child_fields("nonpartyCorps", self.single_window.selected_corp),
        ));
        view.set_selected_attachment(self.single_window.selected_attachment as i32 + 1);
        view.set_selected_corp(self.single_window.selected_corp as i32 + 1);
        view.set_can_undo(self.single_window.history.can_undo());
        view.set_can_redo(self.single_window.history.can_redo());
        view.set_invoices(model(
            self.single_window
                .invoices
                .iter()
                .map(|r| format!("{} · {}", text(r, "invoiceNo"), text(r, "contractNo")).into())
                .collect(),
        ));
        let batches = self
            .single_window
            .batches
            .iter()
            .map(|r| {
                let mut r = r.clone();
                r["businessType"] = json!(if r["businessType"] == "CustomsCoo" {
                    "原产地证"
                } else {
                    "代理委托"
                });
                r
            })
            .collect::<Vec<_>>();
        view.set_batches(model(rows(
            &batches,
            &[
                "batchReference",
                "businessType",
                "invoiceNo",
                "submissionVersion",
                "status",
                "referenceNo",
                "updatedAt",
            ],
        )));
        if let Some(detail) = &self.single_window.detail {
            view.set_detail(
                format!(
                    "{} · {} · {} · {} · {}",
                    text(detail, "batchReference"),
                    text(detail, "companyScope"),
                    text(detail, "clientProfileName"),
                    text(detail, "assignedCardIdentifier"),
                    sw::status(&text(detail, "status"))
                )
                .into(),
            );
            view.set_packages(model(rows(
                detail["packageRecords"]
                    .as_array()
                    .map(Vec::as_slice)
                    .unwrap_or_default(),
                &[
                    "packageType",
                    "direction",
                    "payloadFileCount",
                    "attachmentFileCount",
                    "warningCount",
                    "createdAt",
                ],
            )));
            view.set_receipts(model(rows(
                detail["receiptRecords"]
                    .as_array()
                    .map(Vec::as_slice)
                    .unwrap_or_default(),
                &[
                    "receiptKind",
                    "referenceNo",
                    "receiptCode",
                    "receiptMessage",
                    "businessStatus",
                    "occurredAt",
                ],
            )));
        }
        view.set_receipt_files(model(
            self.single_window
                .receipt_files
                .iter()
                .map(|p| {
                    p.file_name()
                        .unwrap_or_default()
                        .to_string_lossy()
                        .into_owned()
                        .into()
                })
                .collect(),
        ));
        let issues = self.single_window_issues();
        view.set_issues(model(rows(
            &issues,
            &["groupDisplayName", "fieldLabel", "message"],
        )));
        view.set_locks(model(rows(
            &self.single_window.locks,
            &["displayName", "currentValue", "suggestedValue"],
        )));
        let profiles = self
            .single_window
            .profiles
            .iter()
            .map(|r| {
                let mut r = r.clone();
                r["isActive"] = json!(if r["isActive"] == true { "当前" } else { "" });
                r
            })
            .collect::<Vec<_>>();
        view.set_profiles(model(rows(
            &profiles,
            &["profileName", "companyScope", "cardIdentifier", "isActive"],
        )));
        view.set_profile_fields(model(
            self.single_window
                .profile
                .as_ref()
                .map(|f| sw::fields(f, sw::PROFILE_FIELDS))
                .unwrap_or_default(),
        ));
        let entries=self.single_window.dictionary_entries().iter().map(|r|{
            json!({"code":if r.get("code").is_some(){text(r,"code")}else{text(r,"value")},"name":if r.get("chineseName").is_some(){text(r,"chineseName")}else{text(r,"name")},"englishName":r["englishName"],"aliases":r["aliases"].as_array().into_iter().flatten().filter_map(Value::as_str).collect::<Vec<_>>().join("、")})
        }).collect::<Vec<_>>();
        view.set_dictionary_rows(model(rows(
            &entries,
            &["code", "name", "englishName", "aliases"],
        )));
        view.set_dictionary_fields(model(
            self.single_window
                .dictionary_form
                .as_ref()
                .map(|f| sw::fields(f, &[]))
                .unwrap_or_default(),
        ));
        view.set_producers(model(rows(
            &self.single_window.producers,
            &[
                "ciqRegNo",
                "prdcEtpsName",
                "prdcEtpsConcEr",
                "prdcEtpsTel",
                "lastSourceStyleNo",
            ],
        )));
    }
    pub fn single_window_issues(&self) -> Vec<Value> {
        self.single_window.review["groups"]
            .as_array()
            .into_iter()
            .flatten()
            .flat_map(|group| group["issues"].as_array().into_iter().flatten())
            .map(|i| {
                let mut i = i.clone();
                i["fieldLabel"] = json!(rules::draft::display(
                    self.single_window.business,
                    rules::text(&i["navigationTarget"], "propertyKey")
                ));
                i
            })
            .collect()
    }
    pub fn refresh_single_window(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<SingleWindow>();
        match view.get_tab() {
            0 => self.request(
                LIST_SINGLE_WINDOW_OPERATION_CENTER,
                0,
                vec![
                    ("keyword", view.get_keyword().into()),
                    (
                        "businessType",
                        ["", "CustomsCoo", "AgentConsignment"]
                            [view.get_business_filter().clamp(0, 2) as usize]
                            .into(),
                    ),
                    (
                        "status",
                        [
                            "",
                            "SubmitPackageExported",
                            "SubmitPackageImported",
                            "QueuedToClient",
                            "Accepted",
                            "PendingReview",
                            "Approved",
                            "Rejected",
                            "Failed",
                        ][view.get_status_filter().clamp(0, 8) as usize]
                            .into(),
                    ),
                    ("pageNumber", view.get_page().max(1).to_string()),
                    ("pageSize", "30".into()),
                ],
                None,
                "sw:batches",
            ),
            1 | 2 if self.single_window.invoice_id > 0 => self.request(
                self.single_window_operation(
                    GET_CUSTOMS_COO_DOCUMENT,
                    GET_AGENT_CONSIGNMENT_DOCUMENT,
                ),
                self.single_window.invoice_id,
                vec![],
                None,
                "sw:document",
            ),
            1 | 2 => self.request(
                LIST_INVOICES,
                0,
                vec![
                    ("keyword", view.get_invoice_keyword().to_string()),
                    ("pageSize", "100".into()),
                ],
                None,
                "sw:invoices",
            ),
            3 => self.request(
                GET_SINGLE_WINDOW_CLIENT_PROFILES,
                0,
                vec![],
                None,
                "sw:profiles",
            ),
            4 => self.request(
                GET_SINGLE_WINDOW_REFERENCE_CATALOG,
                0,
                vec![],
                None,
                "sw:dictionary",
            ),
            _ => {}
        }
    }
    pub fn single_window_edit(&mut self, key: &str, value: &str, selected: Option<i32>) {
        if self.task.is_some() {
            return;
        }
        let (form, path) = if let Some(path) = key.strip_prefix("profile.") {
            (self.single_window.profile.as_mut(), path)
        } else if let Some(path) = key.strip_prefix("dictionary.") {
            (self.single_window.dictionary_form.as_mut(), path)
        } else {
            (self.single_window.form.as_mut(), key)
        };
        let Some(form) = form else {
            return;
        };
        let before = form.value.clone();
        let result = if let Some(index) = selected {
            form.choose(path, index.max(0) as usize)
        } else {
            form.edit(path, value)
        };
        if let Err(cause) = result {
            form.error = cause;
            return;
        }
        if path == key {
            if key == "orgCode" {
                let code = text(&form.value, "orgCode");
                let authorities = rules::catalog::authorities();
                if let Some(entry) = authorities["options"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .find(|r| r["code"] == code)
                {
                    form.value["aplAdd"] = entry["applicationAddress"].clone();
                    form.value["fetchPlace"] = json!(code);
                }
            }
            self.single_window
                .history
                .record(before, &form.value, Some(key.into()));
            self.single_window.review = Value::Null;
            let lookups = self.single_window.lookups();
            if let Some(form) = &mut self.single_window.form {
                form.lookups = lookups;
            }
        }
        self.sync_single_window();
    }
    pub fn single_window_toggle(&mut self, key: &str) {
        let sections = self.single_window.sections(key.starts_with("goods."));
        if let Some(section) = sections.iter().find(|s| s.key == key) {
            self.single_window
                .disclosures
                .insert(key.into(), !section.expanded);
        }
        self.sync_single_window();
    }
}
