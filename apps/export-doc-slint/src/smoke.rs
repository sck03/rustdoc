//! Runs only with an explicitly isolated validation DataRoot.
use crate::{App, AppWindow, controller::Desktop};
use export_doc_engine::{invoice::InvoiceDraft, paths};
use slint::{
    ComponentHandle, Model,
    platform::{Key, WindowEvent},
};
use std::{
    cell::RefCell,
    path::{Path, PathBuf},
    rc::Rc,
    time::{Duration, Instant},
};
mod administration;
mod business;
mod maintenance;
mod office;
mod pages;
mod single_window;
mod tools;

pub struct Smoke {
    stage: usize,
    batch: usize,
    started: Instant,
    output: PathBuf,
    checks: Vec<String>,
    saved_id: i64,
    scale_visible_rows: usize,
    pages: pages::Pages,
    office: office::OfficeSmoke,
    backup: maintenance::BackupSmoke,
    administration: administration::AdministrationSmoke,
    business: business::BusinessSmoke,
    tools: tools::ToolsSmoke,
    single_window: single_window::SingleWindowSmoke,
    last_diagnostic: Instant,
}
impl Smoke {
    pub fn new(output: PathBuf) -> Result<Self, String> {
        paths::ensure_safe_absolute(&output)?;
        std::fs::create_dir_all(&output).map_err(|e| e.to_string())?;
        Ok(Self {
            stage: 0,
            batch: 0,
            started: Instant::now(),
            output,
            checks: vec![],
            saved_id: 0,
            scale_visible_rows: 0,
            pages: Default::default(),
            office: Default::default(),
            backup: Default::default(),
            administration: Default::default(),
            business: Default::default(),
            tools: Default::default(),
            single_window: Default::default(),
            last_diagnostic: Instant::now(),
        })
    }
    pub fn tick(&mut self, ui: &AppWindow, state: &Rc<RefCell<Desktop>>) -> Result<bool, String> {
        if self.started.elapsed() > Duration::from_secs(900) {
            return Err(format!("窗口验收在阶段 {} 超时。", self.stage));
        }
        let app = ui.global::<App>();
        if self.last_diagnostic.elapsed() > Duration::from_secs(30) {
            eprintln!(
                "native-ui-smoke diagnostic: reportedStage={} batch={} busy={} error={}",
                self.stage,
                self.batch,
                app.get_busy(),
                app.get_error()
            );
            self.last_diagnostic = Instant::now();
        }
        if app.get_busy() {
            return Ok(false);
        }
        if self.stage > 0 && state.borrow().load_lookups {
            return Ok(false);
        }
        if app.get_error() == "请等待当前操作完成。" {
            app.set_error("".into());
            return Ok(false);
        }
        if !app.get_error().is_empty() {
            return Err(format!("阶段 {}:{}", self.stage, app.get_error()));
        }
        if self.stage > 0 && self.stage < 3 && !app.get_logged_in() {
            return Ok(false);
        }
        if self.stage >= 36 {
            let complete = match self.batch {
                0 => self.pages.tick(ui, state, &self.output)?,
                1 => self.office.tick(ui, state, &self.output)?,
                2 => self.administration.tick(ui, state, &self.output)?,
                3 => self.business.tick(ui, state, &self.output)?,
                4 => self.tools.tick(ui, state, &self.output)?,
                5 => self
                    .single_window
                    .tick(ui, state, &self.output, self.saved_id)?,
                6 => self.backup.tick(ui, state, &self.output)?,
                _ => true,
            };
            if self.batch < 7 {
                if complete {
                    self.checks.append(match self.batch {
                        0 => &mut self.pages.checks,
                        1 => &mut self.office.checks,
                        2 => &mut self.administration.checks,
                        3 => &mut self.business.checks,
                        4 => &mut self.tools.checks,
                        5 => &mut self.single_window.checks,
                        _ => &mut self.backup.checks,
                    });
                    self.batch += 1;
                }
                return Ok(false);
            }
            let report = serde_json::json!({"passed":self.checks,"invoiceId":self.saved_id,"visibleRows":self.scale_visible_rows,"totalRows":5000,"systemIme":"not-validated","nativeFileDialog":"not-validated","renderer":"Slint winit software","elapsedSeconds":self.started.elapsed().as_secs_f64()});
            paths::atomic_write(
                &self.output.join("results.json"),
                &serde_json::to_vec_pretty(&report).map_err(|cause| cause.to_string())?,
            )?;
            return Ok(true);
        }
        match self.stage {
            0 => {
                snapshot(ui, &self.output.join("01-login.png"))?;
                app.invoke_login("admin".into(), "".into());
            }
            1 => {
                if state.borrow().load_lookups {
                    return Ok(false);
                }
                snapshot(ui, &self.output.join("02-invoices.png"))?;
                state
                    .borrow_mut()
                    .open_invoice(InvoiceDraft::demo("2026-09-16", "SLINT-UI-001"));
            }
            2 => {
                app.invoke_invoice_tab_changed(1);
                app.invoke_grid_select(0, 3, false);
            }
            3 => {
                snapshot(ui, &self.output.join("03-items.png"))?;
                chord(ui, Key::Control, "a");
                text(ui, "原生中文商品");
                key(ui, Key::Return);
            }
            4 => {
                let desktop = state.borrow();
                if desktop.grid.draft.rows[0].cells[3] != "原生中文商品" {
                    snapshot(ui, &self.output.join("failed-input.png"))?;
                    return Err(format!(
                        "原生输入事件未提交到商品行：buffer={}, stored={}, active={:?}",
                        app.get_cell_value(),
                        desktop.grid.draft.rows[0].cells[3],
                        desktop.grid.selection
                    ));
                }
                if desktop.grid.selection.active.row != 1 {
                    return Err("Enter 没有沿用同列下一行。".into());
                }
                drop(desktop);
                self.checks.push("native-text-input-and-enter".into());
                app.invoke_grid_command("undo".into(), false);
            }
            5 => {
                if state.borrow().grid.draft.rows[0].cells[3] == "原生中文商品" {
                    return Err("撤销未恢复原商品。".into());
                }
                app.invoke_grid_command("redo".into(), false);
            }
            6 => {
                if state.borrow().grid.draft.rows[0].cells[3] != "原生中文商品" {
                    return Err("重做未恢复修改。".into());
                }
                self.checks.push("undo-redo".into());
                app.invoke_invoice_action("save".into());
            }
            7 => {
                let desktop = state.borrow();
                self.saved_id = desktop.grid.draft.header.id;
                if self.saved_id <= 0 || desktop.grid.dirty() {
                    return Err("发票保存没有返回有效版本。".into());
                }
                let saved = desktop
                    .client
                    .get_invoice(self.saved_id)
                    .map_err(|e| e.to_string())?;
                if saved.items[0].style_name_cn != "原生中文商品" {
                    return Err("商品中文没有从数据库回读。".into());
                }
                drop(desktop);
                self.checks.push("save-and-readback".into());
                app.invoke_invoice_tab_changed(4);
                app.invoke_pdf_action("preview".into());
            }
            8 => {
                if app.get_pdf_pages() < 1 {
                    return Err("PDF 未生成页面。".into());
                }
                snapshot(ui, &self.output.join("04-pdf.png"))?;
                if let Some(pdf) = &state.borrow().pdf {
                    paths::save_pdf(&self.output.join("invoice.pdf"), pdf)?;
                }
                self.checks.push("native-pdf".into());
                state.borrow_mut().open_designer(None);
            }
            9 => {
                snapshot(ui, &self.output.join("05-designer.png"))?;
                state.borrow_mut().designer_move("title", 500., 100.);
                app.invoke_designer_action("save".into());
            }
            10 => {
                if !app.get_template_saved() {
                    return Err("模板未持久化。".into());
                }
                app.invoke_designer_action("publish".into());
            }
            11 => {
                self.checks.push("designer-save-publish".into());
                state.borrow_mut().navigate_now("crm-customers");
            }
            12 => {
                snapshot(ui, &self.output.join("06-crm.png"))?;
                app.invoke_new_record();
            }
            13 => {
                app.invoke_field_edited("name".into(), "Slint 验收客户".into());
                app.invoke_field_edited("countryRegion".into(), "中国".into());
                app.invoke_save_form();
            }
            14 => {
                if state.borrow().load_lookups {
                    return Ok(false);
                }
                if app.get_records().row_count() == 0 {
                    return Err("客户保存后列表为空。".into());
                }
                self.checks.push("crm-create-readback".into());
                state.borrow_mut().navigate_now("people");
            }
            15 => {
                snapshot(ui, &self.output.join("07-personnel.png"))?;
                state.borrow_mut().navigate_now("companies");
            }
            16 => {
                snapshot(ui, &self.output.join("08-organization.png"))?;
                state.borrow_mut().navigate_now("settings");
            }
            17 => {
                snapshot(ui, &self.output.join("09-settings.png"))?;
                state.borrow_mut().navigate_now("about");
            }
            18 => {
                snapshot(ui, &self.output.join("10-about.png"))?;
                self.checks.push("navigation-and-about-attribution".into());
                let mut desktop = state.borrow_mut();
                let mut draft = InvoiceDraft::demo("2026-09-16", "SCALE");
                draft.rows = vec![draft.rows[0].clone(); 5000];
                desktop.open_invoice(draft);
                drop(desktop);
                app.invoke_invoice_tab_changed(1);
            }
            19 => {
                let rendered = app.get_grid_rows().row_count();
                if app.get_grid_count() != 5000 || rendered > 100 {
                    return Err(format!("虚拟表格行数异常：{rendered}"));
                }
                app.invoke_grid_scroll(4970, 30);
                app.invoke_grid_select(4999, 23, false);
            }
            20 => {
                snapshot(ui, &self.output.join("11-5000-rows.png"))?;
                if app.get_grid_rows().row_count() > 100 {
                    return Err("滚动后创建了全部行控件。".into());
                }
                self.checks.push("5000-row-virtualization".into());
                self.scale_visible_rows = app.get_grid_rows().row_count();
                state.borrow_mut().navigate_now("excel");
            }
            21 => {
                snapshot(ui, &self.output.join("12-excel-tools.png"))?;
                let destination = self.output.join("native-booking.xlsx");
                state.borrow_mut().start(crate::worker::Work::FileJob{
                    operation:export_doc_engine::generated_api::START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
                    parameters:vec![("invoiceId",self.saved_id.to_string())],
                    body:serde_json::json!({"destinationPath":destination}),destination,
                });
            }
            22 => {
                let source = self.output.join("native-booking.xlsx");
                if !std::fs::read(&source)
                    .map_err(|e| e.to_string())?
                    .starts_with(b"PK")
                {
                    return Err("原生托单未输出有效工作簿。".into());
                }
                self.checks.push("native-invoice-booking-export".into());
                state.borrow_mut().request(
                    export_doc_engine::generated_api::PREVIEW_EXCEL_IMPORT,
                    0,
                    vec![],
                    Some(serde_json::json!({"filePath":source})),
                    "excel-preview",
                );
            }
            23 => {
                if !app.get_import_open()
                    || !app.get_import_success()
                    || app.get_import_rows().row_count() != 3
                {
                    return Err("Excel 预览没有展示完整的三条样例明细。".into());
                }
                snapshot(ui, &self.output.join("13-excel-import-preview.png"))?;
                app.invoke_import_confirmed(true);
            }
            24 => {
                if !state.borrow().grid.dirty() || state.borrow().grid.draft.header.id != 0 {
                    return Err("导入结果没有保留为待保存的新草稿。".into());
                }
                state
                    .borrow_mut()
                    .invoice_field_edit("invoiceNo", "SLINT-IMPORTED-001");
                app.invoke_invoice_action("save".into());
            }
            25 => {
                if state.borrow().grid.draft.header.id <= 0
                    || state.borrow().grid.draft.header.invoice_no != "SLINT-IMPORTED-001"
                {
                    return Err("导入草稿没有保存为独立发票。".into());
                }
                self.checks.push("native-excel-preview-confirm-save".into());
                state.borrow_mut().navigate_now("jobs");
            }
            26 => {
                if app.get_records().row_count() == 0 {
                    return Err("文件任务没有显示已完成的托单。".into());
                }
                app.invoke_select_record(1);
                if !app.get_job_can_download() {
                    return Err("完成的文件任务不能保存结果。".into());
                }
                snapshot(ui, &self.output.join("14-file-tasks.png"))?;
                self.checks.push("native-file-task-selection".into());
                state.borrow_mut().navigate_now("payments");
            }
            27 => {
                app.invoke_new_record();
            }
            28 => {
                if app.get_page() != "payment-edit" {
                    return Err("付款没有使用专用编辑页面。".into());
                }
                app.invoke_payment_field_edited("voucherNo".into(), "SLINT-PAY-001".into());
                app.invoke_payment_field_edited("payeeName".into(), "原生付款测试单位".into());
                app.invoke_payment_tab_changed(1);
                app.invoke_toggle_form_section("payment.spares".into());
                app.invoke_payment_field_edited("spare7".into(), "跨页签保留的备用内容".into());
                snapshot(ui, &self.output.join("15-payment-business.png"))?;
                app.invoke_payment_tab_changed(2);
            }
            29 => {
                app.invoke_payment_field_edited("travelExpense".into(), "0.1".into());
                app.invoke_payment_field_edited("otherExpense".into(), "0.2".into());
                let total: rust_decimal::Decimal = serde_json::from_value(
                    state.borrow().payment_form.as_ref().unwrap().value["cnyAmount"].clone(),
                )
                .map_err(|cause| cause.to_string())?;
                if total != "0.3".parse::<rust_decimal::Decimal>().unwrap() {
                    return Err(format!("付款费用合计不精确：{total}"));
                }
                snapshot(ui, &self.output.join("16-payment-amounts.png"))?;
                app.invoke_payment_tab_changed(0);
                app.invoke_payment_field_edited("receiptDate".into(), "2026-02-29".into());
                app.invoke_payment_tab_changed(2);
                app.invoke_payment_action("save".into());
            }
            30 => {
                if app.get_payment_tab() != 0
                    || app.get_payment_invalid_field() != "receiptDate"
                    || app.get_payment_error().is_empty()
                {
                    return Err("付款跨页签校验没有定位收汇日期。".into());
                }
                app.invoke_payment_field_edited("receiptDate".into(), "".into());
                app.invoke_payment_tab_changed(3);
            }
            31 => {
                app.invoke_pdf_action("preview".into());
            }
            32 => {
                if app.get_pdf_pages() == 0
                    || app.get_payment_saved()
                    || app.get_report_export_ready()
                {
                    return Err("付款草稿预览错误地保存了正式数据或允许导出。".into());
                }
                snapshot(ui, &self.output.join("17-payment-draft-preview.png"))?;
                self.checks
                    .push("payment-tabs-exact-expenses-validation-draft-preview".into());
                app.invoke_payment_action("save".into());
            }
            33 => {
                if !app.get_payment_saved() || app.get_payment_dirty() {
                    return Err("付款保存没有更新草稿基线。".into());
                }
                if state.borrow().payment_form.as_ref().unwrap().value["spare7"]
                    != "跨页签保留的备用内容"
                {
                    return Err("付款备用字段丢失。".into());
                }
                app.invoke_payment_tab_changed(0);
                app.set_payment_new_method("国内汇款".into());
                app.invoke_payment_action("add-method".into());
            }
            34 => {
                if state.borrow().payment_form.as_ref().unwrap().value["paymentMethod"]
                    != "国内汇款"
                {
                    return Err("新增付款方式未被选中。".into());
                }
                app.invoke_payment_action("reload".into());
                if !app.get_confirm_open() {
                    return Err("重新加载付款没有保护未保存草稿。".into());
                }
                app.invoke_confirm(false);
                if state.borrow().payment_form.as_ref().unwrap().value["paymentMethod"]
                    != "国内汇款"
                {
                    return Err("取消重新加载后付款草稿丢失。".into());
                }
                app.invoke_payment_action("save".into());
            }
            35 => {
                self.checks
                    .push("payment-custom-method-save-reload-cancel".into());
                snapshot(ui, &self.output.join("18-payment-saved.png"))?;
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
fn snapshot(ui: &AppWindow, path: &Path) -> Result<(), String> {
    let buffer = ui.window().take_snapshot().map_err(|e| e.to_string())?;
    image::save_buffer(
        path,
        buffer.as_bytes(),
        buffer.width(),
        buffer.height(),
        image::ColorType::Rgba8,
    )
    .map_err(|e| e.to_string())
}
fn text(ui: &AppWindow, text: &str) {
    ui.window()
        .dispatch_event(WindowEvent::KeyPressed { text: text.into() });
    ui.window()
        .dispatch_event(WindowEvent::KeyReleased { text: text.into() });
}
fn key(ui: &AppWindow, key: Key) {
    let text: slint::SharedString = key.into();
    ui.window()
        .dispatch_event(WindowEvent::KeyPressed { text: text.clone() });
    ui.window()
        .dispatch_event(WindowEvent::KeyReleased { text });
}
fn chord(ui: &AppWindow, modifier: Key, value: &str) {
    let m: slint::SharedString = modifier.into();
    ui.window()
        .dispatch_event(WindowEvent::KeyPressed { text: m.clone() });
    text(ui, value);
    ui.window()
        .dispatch_event(WindowEvent::KeyReleased { text: m });
}
