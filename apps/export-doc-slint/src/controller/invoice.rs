use super::*;
use crate::{
    GridColumn, GridRow,
    form_model::{FormModel, subset},
};
use export_doc_domain::invoice_columns::ITEM_COLUMNS;
use export_doc_engine::contracts;
use serde_json::{Value, json};

impl Desktop {
    pub fn open_invoice(&mut self, draft: InvoiceDraft) {
        self.grid = InvoiceGrid::new(draft, 0);
        self.first_row = 0;
        self.pdf = None;
        self.report_payment = None;
        self.pending_invoice = None;
        self.invoice_form = FormModel::new(
            contracts::schema("ApiInvoiceDetailDto"),
            json!(self.grid.draft.header),
            self.lookups.clone(),
        );
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_page("invoice-edit".into());
            app.set_invoice_tab(0);
            app.set_pdf_pages(0);
            app.set_pdf_image(Default::default());
            app.set_report_type("ExportDocument".into());
            app.set_report_with_seal(false);
            app.set_review_open(false);
            app.set_credit_report("".into());
            app.set_credit_summary("".into());
        }
        self.sync_invoice();
        self.sync_grid();
        self.sync_invoice_fields();
        {
            self.request(
                LIST_REPORT_TEMPLATES,
                0,
                vec![("reportType", "ExportDocument".into())],
                None,
                "templates",
            );
        }
    }
    pub fn sync_invoice(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let header = &self.grid.draft.header;
        app.set_invoice_title(if header.id == 0 {
            "新建发票".into()
        } else {
            format!("发票 · {}", header.invoice_no).into()
        });
        app.set_dirty(self.grid.dirty() || !self.invoice_form.error.is_empty());
        app.set_invoice_saved(header.id > 0);
        app.set_report_ready(self.can(PREVIEW_INVOICE_REPORT_DRAFT_HTML));
        app.set_report_export_ready(
            header.id > 0
                && !self.grid.dirty()
                && self.invoice_form.error.is_empty()
                && self.can(START_INVOICE_REPORT_PDF_SAVE_TO_PATH_JOB),
        );
        app.set_invoice_editable(
            (header.id == 0 || header.status == "Draft")
                && self.can(if header.id == 0 {
                    CREATE_INVOICE
                } else {
                    UPDATE_INVOICE
                }),
        );
        app.set_can_undo(self.grid.history.can_undo());
        app.set_can_save_product(self.can(CREATE_PRODUCT) || self.can(UPDATE_PRODUCT));
        app.set_can_redo(self.grid.history.can_redo());
        let count = self
            .grid
            .draft
            .rows
            .iter()
            .filter(|r| !r.is_blank())
            .count();
        let amount = self
            .grid
            .draft
            .rows
            .iter()
            .filter_map(|r| r.to_dto().ok())
            .map(|r| r.total_price)
            .sum::<rust_decimal::Decimal>();
        app.set_invoice_summary(
            format!(
                "{count} 项商品 · {} {amount:.2} · {}",
                header.currency,
                if self.grid.dirty() {
                    "尚未保存"
                } else if header.id > 0 {
                    "已保存"
                } else {
                    "新建"
                }
            )
            .into(),
        );
    }
    pub fn sync_invoice_fields(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let sections = crate::form_sections::build(
            &self.invoice_form,
            &crate::form_sections::invoice(app.get_invoice_tab()),
            &mut self.disclosure_state,
        );
        app.set_invoice_sections(model(sections));
        let marks = self.invoice_form.fields_for(&subset(
            &self.invoice_form.schema,
            &["shippingMarksType", "shippingMarks", "specialTerms"],
        ));
        app.set_marks_fields(model(marks));
    }
    pub fn invoice_field_edit(&mut self, key: &str, text: &str) {
        if self.task.is_some()
            || self
                .ui
                .upgrade()
                .is_none_or(|ui| !ui.global::<App>().get_invoice_editable())
        {
            return;
        }
        let before = self.grid.draft.clone();
        match self.invoice_form.edit(key, text).and_then(|_| {
            serde_json::from_value::<ApiInvoiceDetailDto>(self.invoice_form.value.clone())
                .map_err(|e| e.to_string())
        }) {
            Ok(header) => {
                self.grid.draft.header = header;
                self.grid
                    .history
                    .record(before, &self.grid.draft, Some(format!("header:{key}")));
                self.invoice_form.error.clear();
                self.sync_invoice();
            }
            Err(error) => {
                self.invoice_form.error = error.clone();
                self.error(error);
            }
        }
    }
    pub fn invoice_field_select(&mut self, key: &str, index: usize) {
        if self.task.is_some()
            || self
                .ui
                .upgrade()
                .is_none_or(|ui| !ui.global::<App>().get_invoice_editable())
        {
            return;
        }
        let before = self.grid.draft.clone();
        match self.invoice_form.choose(key, index).and_then(|_| {
            serde_json::from_value::<ApiInvoiceDetailDto>(self.invoice_form.value.clone())
                .map_err(|e| e.to_string())
        }) {
            Ok(header) => {
                self.grid.draft.header = header;
                self.grid.history.record(before, &self.grid.draft, None);
                self.sync_invoice_fields();
                self.sync_invoice();
                let id = self.invoice_form.value[key].as_i64().unwrap_or(0);
                if id > 0 {
                    match key {
                        "customerId" => {
                            self.request(GET_CUSTOMER, id, vec![], None, "invoice-customer")
                        }
                        "exporterId" => {
                            self.request(GET_EXPORTER, id, vec![], None, "invoice-exporter")
                        }
                        _ => {}
                    }
                }
            }
            Err(error) => self.error(error),
        }
    }
    pub fn apply_party(&mut self, key: &str, value: &Value) {
        let before = self.grid.draft.clone();
        let mappings: &[(&str, &str)] = if key == "invoice-customer" {
            &[
                ("customerNameEN", "customerNameEN"),
                ("customerNameCN", "customerNameCN"),
                ("addressEN", "customerAddressEN"),
                ("notifyPartyMode", "notifyPartyMode"),
                ("notifyPartyName", "notifyPartyName"),
                ("notifyPartyAddress", "notifyPartyAddress"),
            ]
        } else {
            &[
                ("exporterNameEN", "exporterNameEN"),
                ("exporterNameCN", "exporterNameCN"),
                ("addressEN", "exporterAddressEN"),
                ("addressCN", "exporterAddressCN"),
                ("creditCode", "exporterCreditCode"),
                ("customsCode", "exporterCustomsCode"),
            ]
        };
        for (source, target) in mappings {
            if !value[*source].is_null() {
                self.invoice_form.value[*target] = value[*source].clone();
            }
        }
        if let Ok(header) = serde_json::from_value(self.invoice_form.value.clone()) {
            self.grid.draft.header = header;
            self.grid.history.record(before, &self.grid.draft, None);
        }
        self.sync_invoice_fields();
        self.sync_invoice();
    }
    pub fn sync_grid(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let mut offset = 140f32;
        app.set_grid_columns(model(
            self.grid
                .visible
                .iter()
                .map(|&i| {
                    let column = &ITEM_COLUMNS[i];
                    let item = GridColumn {
                        key: column.key.into(),
                        label: column.label.into(),
                        width: column.width as f32,
                        offset,
                        visible: true,
                    };
                    offset += column.width as f32;
                    item
                })
                .collect(),
        ));
        app.set_all_columns(model(
            ITEM_COLUMNS
                .iter()
                .enumerate()
                .map(|(i, c)| GridColumn {
                    key: c.key.into(),
                    label: c.label.into(),
                    visible: self.grid.visible.contains(&i),
                    width: c.width as f32,
                    ..Default::default()
                })
                .collect(),
        ));
        app.set_grid_width(offset);
        let count =
            (self.grid.draft.rows.len().max(6) + 1).min(export_doc_engine::invoice::MAX_ROWS);
        app.set_grid_count(count as i32);
        app.set_grid_rows(model(
            (self.first_row.min(count)..(self.first_row + self.visible_rows).min(count))
                .map(|index| GridRow {
                    index: index as i32,
                    cells: model(
                        (0..self.grid.visible.len())
                            .map(|c| self.grid.display(index, c).into())
                            .collect(),
                    ),
                })
                .collect(),
        ));
        let selection = self.grid.selection;
        app.set_active_row(selection.active.row as i32);
        app.set_active_column(selection.active.column as i32);
        app.set_anchor_row(selection.anchor.row as i32);
        app.set_anchor_column(selection.anchor.column as i32);
    }
    pub fn sync_cell(&self, focus: bool) {
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            let active = self.grid.selection.active;
            app.set_cell_value(self.grid.display(active.row, active.column).into());
            if focus {
                app.set_cell_focus(app.get_cell_focus() + 1);
            }
        }
    }
    pub fn commit_cell(&mut self) -> Result<(), String> {
        let Some(ui) = self.ui.upgrade() else {
            return Ok(());
        };
        let app = ui.global::<App>();
        if app.get_invoice_tab() != 1 || app.get_page() != "invoice-edit" {
            return Ok(());
        }
        let active = self.grid.selection.active;
        let value = app.get_cell_value().to_string();
        if value != self.grid.display(active.row, active.column) {
            if !app.get_invoice_editable() {
                return Err("已核对的发票不能直接修改。".into());
            }
            self.grid.edit(active.row, active.column, &value)?;
            self.sync_invoice();
            self.sync_grid();
        }
        Ok(())
    }
    pub fn grid_select(&mut self, row: usize, column: usize, extend: bool) {
        if let Err(error) = self.commit_cell() {
            self.error(error);
            return;
        }
        self.grid.select(row, column, extend);
        self.sync_grid();
        self.sync_cell(true);
    }
    pub fn grid_scroll(&mut self, first: usize, count: usize) {
        self.first_row = first;
        self.visible_rows = count.clamp(1, 100);
        self.sync_grid();
    }
    pub fn grid_command(&mut self, command: &str, shift: bool) {
        if let Err(error) = self.commit_cell() {
            self.error(error);
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let editable = ui.global::<App>().get_invoice_editable();
        let result = match command {
            "next" => {
                self.grid
                    .move_selection(if shift { -1 } else { 1 }, 0, false);
                Ok(())
            }
            "up" | "down" | "left" | "right" => {
                self.grid.move_selection(
                    if command == "up" {
                        -1
                    } else if command == "down" {
                        1
                    } else {
                        0
                    },
                    if command == "left" {
                        -1
                    } else if command == "right" {
                        1
                    } else {
                        0
                    },
                    shift,
                );
                Ok(())
            }
            "extend-down" => {
                self.grid.select(
                    self.grid.draft.rows.len().saturating_sub(1),
                    self.grid.selection.active.column,
                    true,
                );
                Ok(())
            }
            "copy" => self.platform.copy_text(ui.window(), self.grid.copy()),
            _ if !editable => Err("已核对的发票不能直接修改。".into()),
            "paste" => self
                .platform
                .paste_text(ui.window())
                .and_then(|text| self.grid.paste(&text)),
            "clear" => self.grid.clear(),
            "fill" => self.grid.fill_down(),
            "undo" => {
                self.grid.undo();
                self.invoice_form.value = json!(self.grid.draft.header);
                self.invoice_form.buffers.clear();
                self.sync_invoice_fields();
                Ok(())
            }
            "redo" => {
                self.grid.redo();
                self.invoice_form.value = json!(self.grid.draft.header);
                self.invoice_form.buffers.clear();
                self.sync_invoice_fields();
                Ok(())
            }
            "insert" | "duplicate" => self
                .grid
                .row_action(command, self.grid.selection.active.row),
            "row-up" => self.grid.row_action("up", self.grid.selection.active.row),
            "row-down" => self.grid.row_action("down", self.grid.selection.active.row),
            _ => Ok(()),
        };
        if let Err(error) = result {
            self.error(error);
        }
        self.sync_grid();
        self.sync_cell(true);
        self.sync_invoice();
    }
    pub fn grid_row_action(&mut self, action: &str, row: usize) {
        if let Err(error) = self
            .commit_cell()
            .and_then(|_| self.grid.row_action(action, row))
        {
            self.error(error);
        }
        self.sync_grid();
        self.sync_cell(false);
        self.sync_invoice();
    }
    pub fn column_visible(&mut self, column: usize, visible: bool) {
        if let Err(error) = self
            .commit_cell()
            .and_then(|_| self.grid.set_visible(column, visible))
        {
            self.error(error);
        }
        self.sync_grid();
        self.sync_cell(false);
    }
    pub fn invoice_tab(&mut self, index: i32) {
        if let Err(error) = self.commit_cell() {
            self.error(error);
            return;
        }
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_invoice_tab(index);
        }
        self.grid.history.end_group();
        self.sync_invoice_fields();
        self.sync_grid();
        self.sync_cell(false);
    }
    pub fn invoice_action(&mut self, action: &str) {
        if action.starts_with("credit-") {
            self.credit_action(action);
            return;
        }
        if action == "save"
            && (self.task.is_some()
                || self
                    .ui
                    .upgrade()
                    .is_none_or(|ui| !ui.global::<App>().get_invoice_editable()))
        {
            return;
        }
        if let Err(error) = self.commit_cell() {
            self.error(error);
            return;
        }
        match action {
            "back" => self.navigate("invoices"),
            "attachments" => {
                if self.grid.draft.header.id <= 0 || self.grid.dirty() {
                    self.error("请先保存发票，再归档业务资料。");
                    return;
                }
                self.attachments.invoice_id = Some(self.grid.draft.header.id);
                self.navigate_now("attachments");
            }
            "save" => {
                if let Err(error) = self.invoice_form.validate() {
                    self.invoice_validation_error(error);
                    return;
                }
                match self.grid.draft.build() {
                    Ok(invoice) => {
                        self.pending_invoice = Some(self.grid.draft.clone());
                        self.start(Work::SaveInvoice(Box::new(invoice)));
                    }
                    Err(error) => self.invoice_validation_error(error),
                }
            }
            "review" => match self.grid.draft.build() {
                Ok(invoice) => {
                    self.request(REVIEW_INVOICE, 0, vec![], Some(json!(invoice)), "review")
                }
                Err(error) => {
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<App>().set_review_text(error.into());
                    }
                }
            },
            _ => {}
        }
    }
    fn invoice_validation_error(&mut self, message: String) {
        if self.invoice_form.invalid_field.is_empty() {
            if let Some((field, _)) = self.grid.draft.header_issue() {
                self.invoice_form.invalid_field = field.into();
            }
        }
        self.invoice_form.error = message.clone();
        let field = self.invoice_form.invalid_field.as_str();
        let tab = [0, 2, 3]
            .into_iter()
            .find(|tab| {
                crate::form_sections::invoice(*tab)
                    .iter()
                    .any(|section| section.fields.contains(&field))
            })
            .unwrap_or(1);
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_invoice_tab(tab);
        }
        self.sync_invoice_fields();
        self.error(message);
    }
    pub fn invoice_saved(&mut self, invoice: ApiInvoiceDetailDto) {
        let saved = InvoiceDraft::from_dto(invoice);
        let current = self.grid.draft.clone();
        let sent = self.pending_invoice.take();
        self.grid.saved(saved.clone());
        if sent.as_ref().is_some_and(|sent| sent != &current) {
            self.grid.draft = current;
            self.grid.draft.header.id = saved.header.id;
            self.grid.draft.header.row_version = saved.header.row_version;
        }
        self.invoice_form.value = json!(self.grid.draft.header);
        self.invoice_form.buffers.clear();
        self.sync_invoice_fields();
        self.sync_invoice();
        self.sync_grid();
        self.sync_cell(false);
        self.status("发票已保存");
    }
}
