use super::{Desktop, Replace, View, theme, worker::Work};
use eframe::egui::{self, RichText};
use egui_extras::{Column, TableBuilder};
use export_doc_native::invoice::{
    COLUMN_COUNT, COLUMNS, ItemRow, MAX_ROWS, NUMERIC_COLUMNS, parse_number,
};

impl Desktop {
    pub(super) fn invoice_toolbar(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.label(
                RichText::new(if self.invoice.header.id == 0 {
                    "新建发票"
                } else {
                    "编辑发票"
                })
                .size(13.),
            );
            if self.invoice_dirty {
                egui::Frame::new()
                    .fill(egui::Color32::from_rgb(255, 250, 238))
                    .stroke(egui::Stroke::new(
                        1.,
                        egui::Color32::from_rgb(239, 196, 109),
                    ))
                    .corner_radius(12)
                    .inner_margin(egui::Margin::symmetric(8, 2))
                    .show(ui, |ui| {
                        ui.label(
                            RichText::new("有未保存修改")
                                .size(12.)
                                .color(egui::Color32::from_rgb(166, 91, 15)),
                        );
                    });
            }
        });
        ui.add_space(10.);
        theme::card().inner_margin(13).show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.vertical(|ui| {
                    ui.label(
                        RichText::new(if self.invoice.header.invoice_no.is_empty() {
                            "新建发票"
                        } else {
                            &self.invoice.header.invoice_no
                        })
                        .strong()
                        .size(14.),
                    );
                    let total: rust_decimal::Decimal = self
                        .invoice
                        .rows
                        .iter()
                        .filter_map(|row| parse_number(&row.cells[10]).ok())
                        .sum();
                    ui.label(
                        RichText::new(format!(
                            "{} 项商品 · {} {:.2} · {}",
                            self.invoice.rows.len(),
                            self.invoice.header.currency,
                            total,
                            if self.invoice_dirty {
                                "有未保存修改"
                            } else {
                                "已保存"
                            }
                        ))
                        .color(theme::MUTED)
                        .size(12.),
                    );
                });
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let save = ui.add_enabled(
                        !self.busy(),
                        theme::primary("▣ 保存发票").min_size(egui::vec2(108., 40.)),
                    );
                    self.probe("save-invoice", &save);
                    if save.clicked() {
                        self.save_invoice(ui.ctx());
                    }
                    if self.smoke.is_some() {
                        let demo = ui.add_enabled(!self.busy(), egui::Button::new("载入样例"));
                        self.probe("demo", &demo);
                        if demo.clicked() {
                            self.request_replace(Replace::Demo, ui.ctx());
                        }
                    }
                    if ui
                        .add_enabled(!self.busy(), egui::Button::new("打开"))
                        .clicked()
                    {
                        self.show_records = true;
                        self.start(
                            ui.ctx(),
                            Work::ListInvoices(1, String::new()),
                            "正在读取发票",
                        );
                    }
                });
            });
            ui.add_space(3.);
            ui.horizontal(|ui| {
                for (title, view, tab) in [
                    ("基本信息", View::Invoice, 0),
                    ("商品明细", View::Items, 0),
                    ("运输与报关", View::Invoice, 1),
                    ("利润与信用证", View::Invoice, 2),
                    ("预览与导出", View::Pdf, 0),
                ] {
                    let selected =
                        self.view == view && (view != View::Invoice || self.invoice_tab == tab);
                    let label = if view == View::Items {
                        format!("{title}  {}", self.invoice.rows.len())
                    } else {
                        title.into()
                    };
                    let response = ui
                        .push_id(title, |ui| {
                            ui.add(
                                egui::Button::new(
                                    RichText::new(label).size(13.).color(if selected {
                                        theme::PRIMARY
                                    } else {
                                        theme::INK
                                    }),
                                )
                                .fill(if selected {
                                    theme::PALE
                                } else {
                                    egui::Color32::WHITE
                                })
                                .stroke(egui::Stroke::new(
                                    1.,
                                    if selected {
                                        theme::PRIMARY
                                    } else {
                                        theme::BORDER
                                    },
                                ))
                                .min_size(egui::vec2(72., 34.)),
                            )
                        })
                        .inner;
                    self.probe(format!("tab-{view:?}-{tab}"), &response);
                    if view != View::Invoice || tab == 0 {
                        self.probe(format!("tab-{view:?}"), &response);
                    }
                    if response.clicked() {
                        self.view = view;
                        self.invoice_tab = tab;
                    }
                }
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.button("AB 英文转大写").clicked() {
                        for row in &mut self.invoice.rows {
                            for index in [0, 1, 2, 4, 5, 8] {
                                row.cells[index] = row.cells[index].to_uppercase();
                            }
                        }
                        self.invoice_changed(ui.ctx());
                    }
                });
            });
            ui.separator();
            ui.label(
                RichText::new(match self.view {
                    View::Items => "录入商品与价格，金额自动汇总；较多明细可打开明细工作台。",
                    View::Pdf => "核对单据、选择模板并输出 PDF。",
                    _ => "维护发票资料，保存后可核对和输出单据。",
                })
                .size(12.)
                .color(theme::MUTED),
            );
        });
        ui.add_space(14.);
    }
    pub(super) fn invoice_ui(&mut self, ui: &mut egui::Ui) {
        self.invoice_toolbar(ui);
        let enabled = !self.busy();
        let mut changed = false;
        egui::ScrollArea::vertical()
            .id_salt("invoice-form-scroll")
            .show(ui, |ui| {
                theme::card().show(ui, |ui| {
                    theme::title(
                        ui,
                        match self.invoice_tab {
                            0 => "基本信息",
                            1 => "运输与报关",
                            _ => "利润与信用证",
                        },
                    );
                    ui.add_enabled_ui(enabled, |ui| {
                        let h = &mut self.invoice.header;
                        match self.invoice_tab {
                            0 => {
                                ui.columns(2, |columns| {
                                    let response = theme::field(
                                        &mut columns[0],
                                        "发票号 *",
                                        &mut h.invoice_no,
                                        "invoiceNo",
                                    );
                                    self.probes.insert("invoice-no".into(), response.rect);
                                    changed |= response.changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "发票日期 * · YYYY-MM-DD",
                                        &mut h.invoice_date,
                                        "invoiceDate",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "合同号",
                                        &mut h.contract_no,
                                        "contractNo",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "币种",
                                        &mut h.currency,
                                        "currency",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "出口商英文名称",
                                        &mut h.exporter_name_en,
                                        "exporterName",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "出口商中文名称",
                                        &mut h.exporter_name_cn,
                                        "exporterNameCN",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "出口商英文地址",
                                        &mut h.exporter_address_en,
                                        "exporterAddress",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "客户英文名称",
                                        &mut h.customer_name_en,
                                        "customerName",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "客户英文地址",
                                        &mut h.customer_address_en,
                                        "customerAddress",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "付款条件",
                                        &mut h.payment_terms,
                                        "paymentTerms",
                                    )
                                    .changed();
                                });
                            }
                            1 => {
                                ui.columns(2, |columns| {
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "贸易条款",
                                        &mut h.trade_terms,
                                        "tradeTerms",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "运输方式",
                                        &mut h.transport_mode,
                                        "transportMode",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "起运港",
                                        &mut h.port_of_loading,
                                        "portLoading",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "目的港",
                                        &mut h.port_of_destination,
                                        "portDestination",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "目的国",
                                        &mut h.destination_country,
                                        "country",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "装运日期 · YYYY-MM-DD",
                                        &mut h.shipment_date,
                                        "shipmentDate",
                                    )
                                    .changed();
                                    columns[0].label("文字唛头");
                                    changed |= columns[0]
                                        .add_sized(
                                            [columns[0].available_width(), 110.],
                                            egui::TextEdit::multiline(&mut h.shipping_marks)
                                                .id_salt("marks"),
                                        )
                                        .changed();
                                    columns[1].label("特殊条款");
                                    changed |= columns[1]
                                        .add_sized(
                                            [columns[1].available_width(), 110.],
                                            egui::TextEdit::multiline(&mut h.special_terms)
                                                .id_salt("specialTerms"),
                                        )
                                        .changed();
                                });
                            }
                            _ => {
                                ui.columns(2, |columns| {
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "银行名称",
                                        &mut h.bank_name,
                                        "bankName",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "银行账号",
                                        &mut h.bank_account,
                                        "bankAccount",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[0],
                                        "SWIFT",
                                        &mut h.swift_code,
                                        "swiftCode",
                                    )
                                    .changed();
                                    changed |= theme::field(
                                        &mut columns[1],
                                        "信用证号",
                                        &mut h.letter_of_credit_no,
                                        "lcNo",
                                    )
                                    .changed();
                                });
                                for (index, value) in [
                                    &mut h.spare1,
                                    &mut h.spare2,
                                    &mut h.spare3,
                                    &mut h.spare4,
                                    &mut h.spare5,
                                    &mut h.spare6,
                                    &mut h.spare7,
                                    &mut h.spare8,
                                    &mut h.spare9,
                                    &mut h.spare10,
                                ]
                                .into_iter()
                                .enumerate()
                                {
                                    changed |= theme::field(
                                        ui,
                                        &format!("备用 {}", index + 1),
                                        value,
                                        &format!("spare{index}"),
                                    )
                                    .changed();
                                }
                            }
                        }
                    });
                    self.additional_invoice_fields(ui);
                });
            });
        if changed {
            self.invoice_changed(ui.ctx());
        }
    }
    pub(super) fn items_ui(&mut self, ui: &mut egui::Ui) {
        self.invoice_toolbar(ui);
        let mut changed = false;
        let enabled = !self.busy();
        if enabled && !self.ime_composing {
            let paste = ui.ctx().input_mut(|input| {
                let position = input.events.iter().position(
                    |event| matches!(event,egui::Event::Paste(text) if text.contains(['\t','\n'])),
                );
                position.map(|position| input.events.remove(position))
            });
            if let Some(egui::Event::Paste(text)) = paste {
                let (row, column) = self.active_cell.unwrap_or((0, 0));
                match self.invoice.paste(row, column, &text) {
                    Ok(count) => {
                        self.status = format!("已粘贴 {count} 行商品");
                        changed = true;
                    }
                    Err(error) => self.error = Some(error),
                }
            }
            self.grid_keyboard(ui);
        }
        theme::card().show(ui, |ui| {
            ui.horizontal(|ui| {
                theme::title(ui, "商品明细");
                let add = ui.add_enabled(
                    enabled && self.invoice.rows.len() < MAX_ROWS,
                    egui::Button::new("+ 新增商品"),
                );
                self.probe("add-row", &add);
                if add.clicked() {
                    self.invoice.rows.push(ItemRow::blank());
                    changed = true;
                    self.focus_cell = Some((self.invoice.rows.len() - 1, 0));
                }
                if ui
                    .add_enabled(
                        enabled && self.invoice_history.can_undo(),
                        egui::Button::new("撤销"),
                    )
                    .clicked()
                {
                    self.invoice_history.undo(&mut self.invoice);
                    self.invoice_checkpoint = self.invoice.clone();
                    self.invoice_dirty = self.invoice != self.invoice_saved;
                }
                if ui
                    .add_enabled(
                        enabled && self.invoice_history.can_redo(),
                        egui::Button::new("重做"),
                    )
                    .clicked()
                {
                    self.invoice_history.redo(&mut self.invoice);
                    self.invoice_checkpoint = self.invoice.clone();
                    self.invoice_dirty = self.invoice != self.invoice_saved;
                }
                ui.checkbox(&mut self.show_spares, "显示备用列");
            });
            ui.label(
                RichText::new(
                    "视宽量准展示真实编辑表的主要字段；其余单位、体积和备用字段可通过列设置显示。",
                )
                .color(theme::MUTED)
                .size(12.),
            );
            ui.add_space(8.);
            let count = if self.show_spares { COLUMN_COUNT } else { 15 };
            let widths: Vec<f32> = (0..count)
                .map(|index| match index {
                    2 | 3 => 126.,
                    4 => 126.,
                    7 | 9 | 10 | 11 | 12 | 13 | 14 => 90.,
                    8 => 70.,
                    _ => 126.,
                })
                .collect();
            let min_width = widths.iter().sum::<f32>() + 80.;
            let height = (ui.available_height() - 80.).max(180.);
            let mut row_action = None;
            let mut detail_row = None;
            let mut focus_next = None;
            egui::ScrollArea::horizontal()
                .id_salt("items-horizontal")
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    ui.set_min_width(min_width);
                    let mut table = TableBuilder::new(ui)
                        .id_salt("items-table")
                        .striped(true)
                        .resizable(true)
                        .cell_layout(egui::Layout::left_to_right(egui::Align::Center))
                        .column(Column::exact(126.))
                        .max_scroll_height(height)
                        .min_scrolled_height(height);
                    for width in &widths {
                        table = table.column(Column::initial(*width).at_least(60.).clip(true));
                    }
                    if let Some((row, _)) = self.focus_cell {
                        table = table.scroll_to_row(row, Some(egui::Align::Center));
                    }
                    table
                        .header(34., |mut header| {
                            header.col(|ui| {
                                ui.strong("行操作");
                            });
                            for title in COLUMNS.iter().take(count) {
                                header.col(|ui| {
                                    ui.label(RichText::new(*title).strong().color(theme::MUTED));
                                });
                            }
                        })
                        .body(|body| {
                            body.rows(40., self.invoice.rows.len(), |mut row| {
                                let index = row.index();
                                row.col(|ui| {
                                    ui.menu_button("行操作", |ui| {
                                        if ui
                                            .add_enabled(enabled, egui::Button::new("编辑详细信息"))
                                            .clicked()
                                        {
                                            detail_row = Some(index);
                                            ui.close();
                                        }
                                        if ui
                                            .add_enabled(enabled, egui::Button::new("复制此行"))
                                            .clicked()
                                        {
                                            row_action = Some((index, true));
                                            ui.close();
                                        }
                                        if ui
                                            .add_enabled(enabled, egui::Button::new("删除此行"))
                                            .clicked()
                                        {
                                            row_action = Some((index, false));
                                            ui.close();
                                        }
                                    });
                                });
                                for column in 0..count {
                                    row.col(|ui| {
                                        let invalid = NUMERIC_COLUMNS.contains(&column)
                                            && parse_number(
                                                &self.invoice.rows[index].cells[column],
                                            )
                                            .is_err();
                                        let text = egui::TextEdit::singleline(
                                            &mut self.invoice.rows[index].cells[column],
                                        )
                                        .id(egui::Id::new(("cell", index, column)))
                                        .desired_width(f32::INFINITY)
                                        .margin(egui::vec2(5., 7.));
                                        let response = ui.add_enabled(enabled, text);
                                        self.probes.insert(
                                            format!("cell-{index}-{column}"),
                                            response.rect,
                                        );
                                        if invalid {
                                            ui.painter().rect_stroke(
                                                response.rect,
                                                4.,
                                                egui::Stroke::new(1., egui::Color32::RED),
                                                egui::StrokeKind::Inside,
                                            );
                                        }
                                        if self.focus_cell == Some((index, column)) {
                                            response.request_focus();
                                            response.scroll_to_me(Some(egui::Align::Center));
                                            focus_next = Some((index, column));
                                        }
                                        if response.has_focus() || response.clicked() {
                                            self.active_cell = Some((index, column));
                                        }
                                        if response.changed() {
                                            self.invoice.rows[index].edit_finished(column);
                                            changed = true;
                                        }
                                    });
                                }
                            });
                        });
                });
            if focus_next.is_some() {
                self.focus_cell = None;
            }
            if let Some(index) = detail_row {
                self.edit_item_details(index);
            }
            if let Some((index, copy)) = row_action {
                if copy && self.invoice.rows.len() < MAX_ROWS {
                    let mut row = self.invoice.rows[index].clone();
                    row.original.id = 0;
                    row.original.invoice_id = 0;
                    self.invoice.rows.insert(index + 1, row);
                } else if !copy {
                    self.invoice.rows.remove(index);
                }
                self.active_cell = None;
                changed = true;
            }
            ui.add_space(8.);
            ui.horizontal(|ui| {
                ui.label(format!("{} 行", self.invoice.rows.len()));
                for (column, label) in [(7, "数量"), (11, "箱数"), (12, "毛重"), (10, "金额")]
                {
                    let total: rust_decimal::Decimal = self
                        .invoice
                        .rows
                        .iter()
                        .filter_map(|row| parse_number(&row.cells[column]).ok())
                        .sum();
                    ui.label(
                        RichText::new(format!(
                            "{label}  {}",
                            export_doc_native::invoice::number(total)
                        ))
                        .color(theme::MUTED),
                    );
                }
            });
        });
        if changed {
            self.invoice_changed(ui.ctx());
        }
    }
    fn grid_keyboard(&mut self, ui: &mut egui::Ui) {
        let Some((mut row, mut column)) = self.active_cell else {
            return;
        };
        let focused = ui.ctx().memory(|memory| memory.focused())
            == Some(egui::Id::new(("cell", row, column)));
        if !focused {
            return;
        }
        let mut moved = false;
        let count = if self.show_spares { COLUMN_COUNT } else { 15 };
        ui.ctx().input_mut(|input| {
            if input.consume_key(egui::Modifiers::NONE, egui::Key::Tab) {
                column += 1;
                if column >= count {
                    column = 0;
                    row += 1;
                }
                moved = true;
            }
            if input.consume_key(egui::Modifiers::SHIFT, egui::Key::Tab) {
                if column > 0 {
                    column -= 1;
                } else if row > 0 {
                    row -= 1;
                    column = count - 1;
                }
                moved = true;
            }
            if input.consume_key(egui::Modifiers::NONE, egui::Key::Enter) {
                row += 1;
                moved = true;
            }
            if input.consume_key(egui::Modifiers::SHIFT, egui::Key::Enter) {
                row = row.saturating_sub(1);
                moved = true;
            }
        });
        if moved && row < self.invoice.rows.len() {
            self.focus_cell = Some((row, column));
        }
    }
    pub(super) fn records_ui(&mut self, ctx: &egui::Context) {
        if !self.show_records {
            return;
        }
        let mut open = true;
        egui::Window::new("已保存发票")
            .open(&mut open)
            .default_width(750.)
            .default_height(430.)
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    ui.add(
                        egui::TextEdit::singleline(&mut self.record_search)
                            .hint_text("搜索发票号 / 客户"),
                    );
                    if ui
                        .add_enabled(!self.busy(), egui::Button::new("查询"))
                        .clicked()
                    {
                        self.record_page = 1;
                        self.start(
                            ctx,
                            Work::ListInvoices(1, self.record_search.clone()),
                            "正在查询发票",
                        );
                    }
                });
                if let Some(records) = self.records.clone() {
                    egui::ScrollArea::vertical()
                        .max_height(330.)
                        .show(ui, |ui| {
                            for record in &records.items {
                                ui.horizontal(|ui| {
                                    ui.label(&record.invoice_no);
                                    ui.label(&record.customer_name);
                                    ui.label(format!(
                                        "{} {}",
                                        record.currency, record.total_amount
                                    ));
                                    if ui
                                        .add_enabled(!self.busy(), egui::Button::new("打开"))
                                        .clicked()
                                    {
                                        self.request_replace(Replace::Load(record.id), ctx);
                                        self.show_records = false;
                                    }
                                });
                                ui.separator();
                            }
                        });
                    ui.horizontal(|ui| {
                        if ui
                            .add_enabled(
                                !self.busy() && records.has_previous_page,
                                egui::Button::new("上一页"),
                            )
                            .clicked()
                        {
                            self.record_page -= 1;
                            self.start(
                                ctx,
                                Work::ListInvoices(self.record_page, self.record_search.clone()),
                                "正在翻页",
                            );
                        }
                        ui.label(format!(
                            "第 {} / {} 页 · {} 条",
                            records.page_number, records.total_pages, records.total_count
                        ));
                        if ui
                            .add_enabled(
                                !self.busy() && records.has_next_page,
                                egui::Button::new("下一页"),
                            )
                            .clicked()
                        {
                            self.record_page += 1;
                            self.start(
                                ctx,
                                Work::ListInvoices(self.record_page, self.record_search.clone()),
                                "正在翻页",
                            );
                        }
                    });
                }
            });
        self.show_records &= open;
    }
}
