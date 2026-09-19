use super::{Desktop, theme, worker::Work};
use eframe::egui::{self, RichText};
use export_doc_native::designer::{DetailColumn, Kind};

impl Desktop {
    pub(super) fn designer_ui(&mut self, ui: &mut egui::Ui) {
        let mut changed = false;
        theme::card().inner_margin(12).show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label("模板名称");
                let response = ui.add_enabled(
                    !self.busy(),
                    egui::TextEdit::singleline(&mut self.template_name).desired_width(220.),
                );
                if response.changed() {
                    self.design_dirty = true;
                }
                let save = ui.add_enabled(!self.busy(), theme::primary("保存并应用模板"));
                self.probe("save-design", &save);
                if save.clicked() {
                    self.save_design(ui.ctx());
                }
                let preview = ui.button("预览与导出");
                self.probe("design-preview", &preview);
                if preview.clicked() {
                    self.view = super::View::Pdf;
                }
                if let Some(record) = &self.template_record {
                    ui.label(
                        RichText::new(format!(
                            "v{} · {}",
                            record.version_number,
                            if self.design_dirty {
                                "有未保存修改"
                            } else {
                                "已保存"
                            }
                        ))
                        .color(theme::MUTED)
                        .size(12.),
                    );
                }
                if ui
                    .add_enabled(
                        !self.busy() && self.template_record.is_some() && !self.design_dirty,
                        egui::Button::new("重新读取"),
                    )
                    .clicked()
                {
                    self.start(
                        ui.ctx(),
                        Work::LoadTemplate(self.template_record.as_ref().unwrap().id),
                        "正在读取模板",
                    );
                }
            });
            ui.horizontal(|ui| {
                let undo = ui.add_enabled(
                    !self.busy() && self.design_history.can_undo(),
                    egui::Button::new("撤销"),
                );
                self.probe("design-undo", &undo);
                if undo.clicked() {
                    self.design_history.undo(&mut self.design);
                    self.design_checkpoint = self.design.clone();
                    self.design_dirty = self.design_saved.as_ref() != Some(&self.design);
                }
                let redo = ui.add_enabled(
                    !self.busy() && self.design_history.can_redo(),
                    egui::Button::new("重做"),
                );
                self.probe("design-redo", &redo);
                if redo.clicked() {
                    self.design_history.redo(&mut self.design);
                    self.design_checkpoint = self.design.clone();
                    self.design_dirty = self.design_saved.as_ref() != Some(&self.design);
                }
                if ui
                    .add_enabled(
                        !self.busy() && !self.selected.is_empty(),
                        egui::Button::new("删除组件"),
                    )
                    .clicked()
                {
                    self.design.remove_selection(&self.selected);
                    self.selected.retain(|id| self.design.element(id).is_some());
                    changed = true;
                }
                ui.separator();
                ui.label("缩放");
                if ui.button("适合窗口").clicked() {
                    self.zoom = None;
                }
                for (label, zoom) in [("75%", 0.75), ("100%", 1.), ("125%", 1.25)] {
                    if ui
                        .selectable_label(self.zoom == Some(zoom), label)
                        .clicked()
                    {
                        self.zoom = Some(zoom);
                    }
                }
                ui.separator();
                ui.checkbox(&mut self.design.grid.enabled, "网格");
                ui.checkbox(&mut self.design.grid.snap, "吸附");
            });
        });
        ui.add_space(12.);
        if !self.busy() && !self.ime_composing && !ui.ctx().egui_wants_keyboard_input() {
            if ui.input_mut(|input| input.consume_key(egui::Modifiers::COMMAND, egui::Key::Z)) {
                self.design_history.undo(&mut self.design);
                self.design_checkpoint = self.design.clone();
                self.design_dirty = self.design_saved.as_ref() != Some(&self.design);
            }
            if ui.input_mut(|input| input.consume_key(egui::Modifiers::COMMAND, egui::Key::Y)) {
                self.design_history.redo(&mut self.design);
                self.design_checkpoint = self.design.clone();
                self.design_dirty = self.design_saved.as_ref() != Some(&self.design);
            }
            if ui.input(|input| input.key_pressed(egui::Key::Delete)) {
                self.design.remove_selection(&self.selected);
                changed = true;
            }
            let step = if self.design.grid.snap {
                self.design.grid.size_hundredth_mm
            } else {
                100
            };
            let (dx, dy) = ui.input(|input| {
                (
                    (i32::from(input.key_pressed(egui::Key::ArrowRight))
                        - i32::from(input.key_pressed(egui::Key::ArrowLeft)))
                        * step,
                    (i32::from(input.key_pressed(egui::Key::ArrowDown))
                        - i32::from(input.key_pressed(egui::Key::ArrowUp)))
                        * step,
                )
            });
            if dx != 0 || dy != 0 {
                self.design.move_selection(&self.selected, dx, dy);
                changed = true;
            }
        }
        egui::Panel::left("designer-palette")
            .exact_size(188.)
            .resizable(false)
            .frame(theme::card().inner_margin(12))
            .show(ui, |ui| {
                theme::title(ui, "组件与字段");
                ui.add_enabled_ui(!self.busy(), |ui| {
                    egui::ComboBox::from_id_salt("active-layer")
                        .selected_text(&self.design.layers[self.active_layer].name)
                        .show_ui(ui, |ui| {
                            for (index, layer) in self
                                .design
                                .layers
                                .iter()
                                .enumerate()
                                .filter(|(_, layer)| layer.role != "Body")
                            {
                                ui.selectable_value(&mut self.active_layer, index, &layer.name);
                            }
                        });
                    for (label, kind) in [
                        (
                            "+ 文字",
                            Kind::Text {
                                text: "新文字".into(),
                            },
                        ),
                        (
                            "+ 分隔线",
                            Kind::Line {
                                direction: "Horizontal".into(),
                            },
                        ),
                        ("+ 矩形", Kind::Rectangle),
                    ] {
                        if ui
                            .add_sized([160., 34.], egui::Button::new(label))
                            .clicked()
                        {
                            match self.design.add(kind, self.active_layer) {
                                Ok(id) => {
                                    self.selected.clear();
                                    self.selected.insert(id);
                                    changed = true;
                                }
                                Err(error) => self.error = Some(error),
                            }
                        }
                    }
                });
                ui.add_space(8.);
                ui.label(RichText::new("字段目录").strong());
                ui.add(
                    egui::TextEdit::singleline(&mut self.field_filter)
                        .hint_text("搜索字段")
                        .desired_width(160.),
                );
                let filter = self.field_filter.to_lowercase();
                egui::ScrollArea::vertical()
                    .id_salt("field-catalog")
                    .max_height((ui.available_height() - 110.).max(90.))
                    .show(ui, |ui| {
                        let mut category = String::new();
                        let fields: Vec<_> = self
                            .fields
                            .iter()
                            .filter(|field| {
                                !field.path.starts_with("item.")
                                    && !matches!(
                                        field.path.as_str(),
                                        "doc_seal_path" | "customs_seal_path"
                                    )
                                    && (filter.is_empty()
                                        || field.label.to_lowercase().contains(&filter)
                                        || field.path.to_lowercase().contains(&filter))
                            })
                            .cloned()
                            .collect();
                        for field in fields {
                            if category != field.category {
                                category = field.category.clone();
                                ui.add_space(6.);
                                ui.label(RichText::new(&category).color(theme::MUTED).size(11.));
                            }
                            let label = field.label.split(" (").next().unwrap_or(&field.label);
                            if ui
                                .add_enabled(
                                    !self.busy(),
                                    egui::Button::new(RichText::new(label).size(12.))
                                        .min_size(egui::vec2(155., 26.)),
                                )
                                .on_hover_text(&field.path)
                                .clicked()
                            {
                                match self.design.add(
                                    Kind::Field {
                                        field_path: field.path,
                                        fallback_text: String::new(),
                                    },
                                    self.active_layer,
                                ) {
                                    Ok(id) => {
                                        self.selected.clear();
                                        self.selected.insert(id);
                                        changed = true;
                                    }
                                    Err(error) => self.error = Some(error),
                                }
                            }
                        }
                    });
                ui.add_space(8.);
                ui.label(
                    RichText::new("拖动调整位置\nShift 单击多选\n方向键微调 / Delete 删除")
                        .color(theme::MUTED)
                        .size(11.),
                );
            });
        egui::Panel::right("designer-inspector")
            .exact_size(246.)
            .resizable(false)
            .frame(theme::card().inner_margin(12))
            .show(ui, |ui| {
                theme::title(ui, "组件属性");
                egui::ScrollArea::vertical()
                    .id_salt("properties")
                    .show(ui, |ui| {
                        ui.add_enabled_ui(!self.busy(), |ui| {
                            if self.selected.len() == 1 {
                                let id = self.selected.first().unwrap().clone();
                                let page_width = self.design.page.width_hundredth_mm;
                                let page_height = self.design.page.height_hundredth_mm;
                                if let Some(element) = self.design.element_mut(&id) {
                                    ui.label(
                                        RichText::new(element.kind.name()).color(theme::PRIMARY),
                                    );
                                    changed |=
                                        ui.checkbox(&mut element.locked, "锁定组件").changed();
                                    changed |= ui
                                        .checkbox(&mut element.output_enabled, "参与输出")
                                        .changed();
                                    ui.add_enabled_ui(!element.locked, |ui| {
                                        ui.separator();
                                        ui.label(RichText::new("位置与尺寸 · mm").strong());
                                        // Values are persisted in integer hundredths of a millimetre.
                                        for (label, value, max) in [
                                            ("X", &mut element.x_hundredth_mm, page_width),
                                            ("Y", &mut element.y_hundredth_mm, page_height),
                                            ("宽", &mut element.width_hundredth_mm, page_width),
                                            ("高", &mut element.height_hundredth_mm, page_height),
                                        ] {
                                            ui.horizontal(|ui| {
                                                ui.label(label);
                                                let mut mm = *value as f64 / 100.;
                                                if ui
                                                    .add(
                                                        egui::DragValue::new(&mut mm)
                                                            .range(0. ..=max as f64 / 100.)
                                                            .speed(0.1)
                                                            .suffix(" mm"),
                                                    )
                                                    .changed()
                                                {
                                                    *value = (mm * 100.).round() as i32;
                                                    changed = true;
                                                }
                                            });
                                        }
                                        ui.separator();
                                        match &mut element.kind {
                                            Kind::Text { text } => {
                                                ui.label("文字内容");
                                                let response = ui.add_sized(
                                                    [ui.available_width(), 90.],
                                                    egui::TextEdit::multiline(text)
                                                        .id_salt("element-text"),
                                                );
                                                self.probes
                                                    .insert("element-text".into(), response.rect);
                                                changed |= response.changed();
                                            }
                                            Kind::Field { field_path, .. } => {
                                                ui.label("绑定字段");
                                                egui::ComboBox::from_id_salt("element-binding")
                                                    .selected_text(field_path.as_str())
                                                    .width(205.)
                                                    .show_ui(ui, |ui| {
                                                        for field in
                                                            self.fields.iter().filter(|field| {
                                                                !field.path.starts_with("item.")
                                                            })
                                                        {
                                                            changed |= ui
                                                                .selectable_value(
                                                                    field_path,
                                                                    field.path.clone(),
                                                                    &field.label,
                                                                )
                                                                .changed();
                                                        }
                                                    });
                                            }
                                            Kind::Flow { block, .. } => {
                                                ui.label(RichText::new("明细列").strong());
                                                let mut remove = None;
                                                for (index, column) in
                                                    block.columns.iter_mut().enumerate()
                                                {
                                                    ui.push_id(index, |ui| {
                                                        changed |= ui
                                                            .text_edit_singleline(&mut column.title)
                                                            .changed();
                                                        egui::ComboBox::from_id_salt(
                                                            "column-field",
                                                        )
                                                        .selected_text(column.field_path.as_str())
                                                        .width(205.)
                                                        .show_ui(ui, |ui| {
                                                            for field in
                                                                self.fields.iter().filter(|field| {
                                                                    field.path.starts_with("item.")
                                                                })
                                                            {
                                                                changed |= ui
                                                                    .selectable_value(
                                                                        &mut column.field_path,
                                                                        field.path.clone(),
                                                                        &field.label,
                                                                    )
                                                                    .changed();
                                                            }
                                                        });
                                                        ui.horizontal(|ui| {
                                                            changed |= ui
                                                                .add(
                                                                    egui::DragValue::new(
                                                                        &mut column.width_mm,
                                                                    )
                                                                    .range(5. ..=190.)
                                                                    .speed(0.5)
                                                                    .suffix(" mm"),
                                                                )
                                                                .changed();
                                                            if ui.small_button("移除").clicked() {
                                                                remove = Some(index);
                                                            }
                                                        });
                                                        ui.separator();
                                                    });
                                                }
                                                if let Some(index) = remove {
                                                    if block.columns.len() > 1 {
                                                        block.columns.remove(index);
                                                        changed = true;
                                                    }
                                                }
                                                if block.columns.len() < 20
                                                    && ui.button("+ 增加明细列").clicked()
                                                {
                                                    let id = export_doc_native::paths::nonce()
                                                        .unwrap_or_else(|_| {
                                                            format!("col-{}", block.columns.len())
                                                        });
                                                    block.columns.push(DetailColumn {
                                                        id,
                                                        title: "品名（中文）".into(),
                                                        field_path: "item.StyleNameCN".into(),
                                                        width_mm: 30.,
                                                        align: "Left".into(),
                                                    });
                                                    changed = true;
                                                }
                                            }
                                            _ => {}
                                        }
                                        ui.separator();
                                        ui.label(RichText::new("外观").strong());
                                        ui.horizontal(|ui| {
                                            ui.label("字号");
                                            changed |= ui
                                                .add(
                                                    egui::DragValue::new(
                                                        &mut element.style.font_size_pt,
                                                    )
                                                    .range(5. ..=48.)
                                                    .speed(0.5)
                                                    .suffix(" pt"),
                                                )
                                                .changed();
                                        });
                                        changed |=
                                            ui.checkbox(&mut element.style.bold, "加粗").changed();
                                        egui::ComboBox::from_id_salt("text-align")
                                            .selected_text(&element.style.align)
                                            .show_ui(ui, |ui| {
                                                for (key, label) in [
                                                    ("Left", "左对齐"),
                                                    ("Center", "居中"),
                                                    ("Right", "右对齐"),
                                                ] {
                                                    changed |= ui
                                                        .selectable_value(
                                                            &mut element.style.align,
                                                            key.into(),
                                                            label,
                                                        )
                                                        .changed();
                                                }
                                            });
                                        ui.horizontal(|ui| {
                                            ui.label("文字颜色");
                                            for hex in ["#173f3b", "#111827", "#0f7f72", "#64748b"]
                                            {
                                                if ui
                                                    .add(
                                                        egui::Button::new("  ")
                                                            .fill(super::canvas::color(hex)),
                                                    )
                                                    .clicked()
                                                {
                                                    element.style.color = hex.into();
                                                    changed = true;
                                                }
                                            }
                                        });
                                        ui.horizontal(|ui| {
                                            ui.label("边框");
                                            changed |= ui
                                                .add(
                                                    egui::DragValue::new(
                                                        &mut element.style.border_width_px,
                                                    )
                                                    .range(0. ..=8.)
                                                    .speed(0.5)
                                                    .suffix(" px"),
                                                )
                                                .changed();
                                        });
                                    });
                                }
                            } else {
                                ui.label(
                                    RichText::new(if self.selected.is_empty() {
                                        "点击画布中的组件查看属性。"
                                    } else {
                                        "已多选，可一起拖动、微调或删除。"
                                    })
                                    .color(theme::MUTED),
                                );
                            }
                            ui.add_space(16.);
                            ui.separator();
                            ui.label(RichText::new("图层").strong());
                            for layer in &mut self.design.layers {
                                ui.horizontal(|ui| {
                                    changed |=
                                        ui.checkbox(&mut layer.visible, &layer.name).changed();
                                    changed |= ui.checkbox(&mut layer.locked, "锁定").changed();
                                });
                            }
                        });
                    });
            });
        egui::CentralPanel::default()
            .frame(
                egui::Frame::new()
                    .fill(egui::Color32::from_rgb(232, 237, 233))
                    .inner_margin(0),
            )
            .show(ui, |ui| {
                self.canvas_ui(ui);
            });
        if changed {
            let group = ui
                .ctx()
                .memory(|memory| memory.focused())
                .map(|id| format!("{id:?}"));
            self.design_changed(group);
        }
    }
}
