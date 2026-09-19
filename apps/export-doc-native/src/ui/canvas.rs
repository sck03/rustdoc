use super::{Desktop, theme};
use eframe::egui::{
    self, Align2, Color32, FontFamily, FontId, Pos2, Rect, Sense, Stroke, StrokeKind, Vec2,
};
use export_doc_native::{
    designer::{Design, Element, Kind},
    invoice::{InvoiceDraft, number, parse_number},
};

pub(super) struct Drag {
    pub before: Design,
    pub pointer: Pos2,
    pub kind: DragKind,
}
pub(super) enum DragKind {
    Move,
    Resize { i: i32, j: i32, id: String },
}

impl Desktop {
    pub(super) fn canvas_ui(&mut self, ui: &mut egui::Ui) {
        let page_size = Vec2::new(
            self.design.page.width_hundredth_mm as f32,
            self.design.page.height_hundredth_mm as f32,
        );
        let auto_scale = ((ui.available_width() - 48.) / page_size.x)
            .min((ui.available_height() - 48.) / page_size.y)
            .max(0.008);
        let scale = self
            .zoom
            .map(|zoom| zoom * 96. / 2540.)
            .unwrap_or(auto_scale);
        let size = page_size * scale;
        let viewport_width = ui.available_width();
        egui::ScrollArea::both()
            .id_salt("canvas-scroll")
            .auto_shrink([false, false])
            .show(ui, |ui| {
                let width = (size.x + 48.).max(viewport_width);
                let (outer, response) =
                    ui.allocate_exact_size(Vec2::new(width, size.y + 48.), Sense::click());
                let origin = outer.min + Vec2::new(((width - size.x) / 2.).max(24.), 24.);
                let paper = Rect::from_min_size(origin, size);
                self.probes.insert("canvas-paper".into(), paper);
                let painter = ui.painter().with_clip_rect(ui.clip_rect());
                painter.rect_filled(
                    paper.translate(Vec2::new(4., 5.)),
                    0.,
                    Color32::from_black_alpha(14),
                );
                painter.rect_filled(paper, 0., Color32::WHITE);
                painter.rect_stroke(
                    paper,
                    0.,
                    Stroke::new(1., theme::BORDER),
                    StrokeKind::Outside,
                );
                if self.design.grid.enabled {
                    let step = self.design.grid.size_hundredth_mm.max(100) as f32 * scale;
                    for index in 1..(size.x / step) as i32 {
                        let x = origin.x + index as f32 * step;
                        painter.line_segment(
                            [Pos2::new(x, origin.y), Pos2::new(x, origin.y + size.y)],
                            Stroke::new(0.5, Color32::from_gray(239)),
                        );
                    }
                    for index in 1..(size.y / step) as i32 {
                        let y = origin.y + index as f32 * step;
                        painter.line_segment(
                            [Pos2::new(origin.x, y), Pos2::new(origin.x + size.x, y)],
                            Stroke::new(0.5, Color32::from_gray(239)),
                        );
                    }
                }
                for index in (0..=self.design.page.width_hundredth_mm / 100).step_by(20) {
                    let x = origin.x + index as f32 * 100. * scale;
                    painter.text(
                        Pos2::new(x, origin.y - 12.),
                        Align2::CENTER_CENTER,
                        format!("{index}"),
                        FontId::proportional(10.),
                        theme::MUTED,
                    );
                }
                let elements: Vec<_> = self
                    .design
                    .layers
                    .iter()
                    .filter(|layer| layer.visible)
                    .flat_map(|layer| {
                        layer
                            .elements
                            .iter()
                            .map(move |element| (layer.locked, element.clone()))
                    })
                    .filter(|(_, element)| element.visible)
                    .collect();
                let mut clicked = false;
                for (layer_locked, element) in elements {
                    let rect = bounds(&element, origin, scale);
                    self.probes.insert(format!("element-{}", element.id), rect);
                    paint_element(&painter, &element, rect, scale, &self.invoice);
                    if self.selected.contains(&element.id) {
                        painter.rect_stroke(
                            rect,
                            0.,
                            Stroke::new(1.5, theme::PRIMARY),
                            StrokeKind::Outside,
                        );
                    }
                    let enabled = !self.busy() && !layer_locked && !element.locked;
                    let response = ui.interact(
                        rect,
                        egui::Id::new(("canvas-element", &element.id)),
                        if enabled {
                            Sense::click_and_drag()
                        } else {
                            Sense::click()
                        },
                    );
                    if response.clicked() {
                        clicked = true;
                        let multi =
                            ui.input(|input| input.modifiers.command || input.modifiers.shift);
                        if !multi {
                            self.selected.clear();
                        }
                        if !self.selected.insert(element.id.clone()) && multi {
                            self.selected.remove(&element.id);
                        }
                    }
                    if enabled && response.drag_started() {
                        clicked = true;
                        if !self.selected.contains(&element.id) {
                            self.selected.clear();
                            self.selected.insert(element.id.clone());
                        }
                        let pointer = ui
                            .input(|input| input.pointer.press_origin())
                            .unwrap_or(response.interact_pointer_pos().unwrap_or(rect.min));
                        self.drag = Some(Drag {
                            before: self.design.clone(),
                            pointer,
                            kind: DragKind::Move,
                        });
                    }
                }
                if self.selected.len() == 1 && !self.busy() {
                    let id = self.selected.first().unwrap().clone();
                    if self.design.editable(&id) {
                        if let Some(element) = self.design.element(&id) {
                            let rect = bounds(element, origin, scale);
                            for (i, j) in [
                                (-1, -1),
                                (0, -1),
                                (1, -1),
                                (-1, 0),
                                (1, 0),
                                (-1, 1),
                                (0, 1),
                                (1, 1),
                            ] {
                                let center = Pos2::new(
                                    match i {
                                        -1 => rect.left(),
                                        1 => rect.right(),
                                        _ => rect.center().x,
                                    },
                                    match j {
                                        -1 => rect.top(),
                                        1 => rect.bottom(),
                                        _ => rect.center().y,
                                    },
                                );
                                let handle = Rect::from_center_size(center, Vec2::splat(8.));
                                painter.rect_filled(handle, 1., Color32::WHITE);
                                painter.rect_stroke(
                                    handle,
                                    1.,
                                    Stroke::new(1., theme::PRIMARY),
                                    StrokeKind::Inside,
                                );
                                let response = ui.interact(
                                    handle,
                                    egui::Id::new(("resize", &id, i, j)),
                                    Sense::drag(),
                                );
                                if response.drag_started() {
                                    self.drag = Some(Drag {
                                        before: self.design.clone(),
                                        pointer: ui
                                            .input(|input| input.pointer.press_origin())
                                            .unwrap_or(center),
                                        kind: DragKind::Resize {
                                            i,
                                            j,
                                            id: id.clone(),
                                        },
                                    });
                                }
                            }
                        }
                    }
                }
                if response.clicked()
                    && !clicked
                    && !elements_under_pointer(
                        &self.design,
                        ui.input(|input| input.pointer.interact_pos()),
                        origin,
                        scale,
                    )
                {
                    self.selected.clear();
                }
                if let Some(drag) = &self.drag {
                    if ui.input(|input| input.key_pressed(egui::Key::Escape)) {
                        self.design = drag.before.clone();
                        self.drag = None;
                    } else if let Some(pointer) = ui.input(|input| input.pointer.interact_pos()) {
                        let delta = (pointer - drag.pointer) / scale;
                        self.design = drag.before.clone();
                        match &drag.kind {
                            DragKind::Move => self.design.move_selection(
                                &self.selected,
                                delta.x.round() as i32,
                                delta.y.round() as i32,
                            ),
                            DragKind::Resize { i, j, id } => {
                                let page_width = self.design.page.width_hundredth_mm;
                                let page_height = self.design.page.height_hundredth_mm;
                                let element = self.design.element_mut(id).unwrap();
                                let mut left = element.x_hundredth_mm;
                                let mut top = element.y_hundredth_mm;
                                let mut right = left + element.width_hundredth_mm;
                                let mut bottom = top + element.height_hundredth_mm;
                                if *i < 0 {
                                    left = (left + delta.x as i32).clamp(0, right - 100);
                                }
                                if *i > 0 {
                                    right = (right + delta.x as i32).clamp(left + 100, page_width);
                                }
                                if *j < 0 {
                                    top = (top + delta.y as i32).clamp(0, bottom - 100);
                                }
                                if *j > 0 {
                                    bottom =
                                        (bottom + delta.y as i32).clamp(top + 100, page_height);
                                }
                                element.x_hundredth_mm = left;
                                element.y_hundredth_mm = top;
                                element.width_hundredth_mm = right - left;
                                element.height_hundredth_mm = bottom - top;
                            }
                        }
                        if ui.input(|input| input.pointer.any_released()) {
                            self.drag = None;
                            self.design_changed(None);
                        }
                    }
                }
            });
    }
}
fn bounds(element: &Element, origin: Pos2, scale: f32) -> Rect {
    Rect::from_min_size(
        origin + Vec2::new(element.x_hundredth_mm as f32, element.y_hundredth_mm as f32) * scale,
        Vec2::new(
            element.width_hundredth_mm as f32,
            element.height_hundredth_mm as f32,
        ) * scale,
    )
}
fn elements_under_pointer(
    design: &Design,
    pointer: Option<Pos2>,
    origin: Pos2,
    scale: f32,
) -> bool {
    pointer.is_some_and(|pointer| {
        design
            .layers
            .iter()
            .filter(|layer| layer.visible)
            .flat_map(|layer| &layer.elements)
            .any(|element| element.visible && bounds(element, origin, scale).contains(pointer))
    })
}
pub(super) fn color(value: &str) -> Color32 {
    if value.len() != 7 {
        return theme::INK;
    }
    let value = u32::from_str_radix(&value[1..], 16).unwrap_or(0x173f3b);
    Color32::from_rgb((value >> 16) as u8, (value >> 8) as u8, value as u8)
}
fn paint_element(
    painter: &egui::Painter,
    element: &Element,
    rect: Rect,
    scale: f32,
    invoice: &InvoiceDraft,
) {
    let style = &element.style;
    let painter = painter.with_clip_rect(rect.intersect(painter.clip_rect()));
    if style.background_color != "#ffffff" {
        painter.rect_filled(rect, 0., color(&style.background_color));
    }
    if style.border_width_px > 0. && style.border_style != "None" {
        painter.rect_stroke(
            rect,
            0.,
            Stroke::new(
                style.border_width_px * scale * 2540. / 96.,
                color(&style.border_color),
            ),
            StrokeKind::Inside,
        );
    }
    let font = FontId::new(
        (style.font_size_pt * 2540. / 72. * scale).max(5.),
        if style.bold {
            FontFamily::Name("bold".into())
        } else {
            FontFamily::Proportional
        },
    );
    let inner = rect.shrink(style.padding_hundredth_mm as f32 * scale);
    match &element.kind {
        Kind::Text { text } => paint_text(
            &painter,
            text,
            inner,
            font,
            &style.align,
            color(&style.color),
        ),
        Kind::Field { field_path, .. } => paint_text(
            &painter,
            &sample(field_path, invoice, None),
            inner,
            font,
            &style.align,
            color(&style.color),
        ),
        Kind::Line { direction } => {
            let end = if direction == "Vertical" {
                rect.left_bottom()
            } else {
                rect.right_top()
            };
            painter.line_segment([rect.left_top(), end], Stroke::new(1., color(&style.color)));
        }
        Kind::Rectangle => {}
        Kind::Flow { block, .. } => {
            let total: f32 = block.columns.iter().map(|column| column.width_mm).sum();
            let row_height = (1300. * scale).max(14.);
            let mut x = rect.left();
            for column in &block.columns {
                let width = rect.width() * column.width_mm / total;
                let header =
                    Rect::from_min_size(Pos2::new(x, rect.top()), Vec2::new(width, row_height));
                painter.rect_filled(header, 0., theme::PALE);
                painter.rect_stroke(
                    header,
                    0.,
                    Stroke::new(0.5, theme::BORDER),
                    StrokeKind::Inside,
                );
                paint_text(
                    &painter,
                    &column.title,
                    header.shrink(2.),
                    font.clone(),
                    &column.align,
                    theme::INK,
                );
                let visible = invoice
                    .rows
                    .len()
                    .min(((rect.height() / row_height) as usize).saturating_sub(1));
                for index in 0..visible {
                    let cell = Rect::from_min_size(
                        Pos2::new(x, rect.top() + (index + 1) as f32 * row_height),
                        Vec2::new(width, row_height),
                    );
                    painter.rect_stroke(
                        cell,
                        0.,
                        Stroke::new(0.5, theme::BORDER),
                        StrokeKind::Inside,
                    );
                    paint_text(
                        &painter,
                        &sample(&column.field_path, invoice, Some(index)),
                        cell.shrink(2.),
                        font.clone(),
                        &column.align,
                        theme::INK,
                    );
                }
                x += width;
            }
            if invoice.rows.len() > ((rect.height() / row_height) as usize).saturating_sub(1) {
                painter.text(
                    rect.center_bottom() - Vec2::new(0., 8.),
                    Align2::CENTER_BOTTOM,
                    "其余商品在 PDF 中自动分页",
                    FontId::proportional(10.),
                    theme::MUTED,
                );
            }
        }
    }
}
fn paint_text(
    painter: &egui::Painter,
    text: &str,
    rect: Rect,
    font: FontId,
    align: &str,
    color: Color32,
) {
    let galley = painter.layout(text.into(), font, color, rect.width().max(1.));
    let x = match align {
        "Right" => rect.right() - galley.size().x,
        "Center" => rect.center().x - galley.size().x / 2.,
        _ => rect.left(),
    };
    painter.galley(Pos2::new(x, rect.top()), galley, color);
}
fn sample(path: &str, invoice: &InvoiceDraft, row: Option<usize>) -> String {
    if let Some(index) = row {
        let map = [
            ("item.PoNumber", 0),
            ("item.StyleNo", 1),
            ("item.StyleName", 2),
            ("item.StyleNameCN", 3),
            ("item.FabricComposition", 4),
            ("item.Brand", 5),
            ("item.HsCode", 6),
            ("item.Quantity", 7),
            ("item.UnitEN", 8),
            ("item.UnitPrice", 9),
            ("item.TotalPrice", 10),
            ("item.Cartons", 11),
            ("item.GwTotal", 12),
            ("item.NwTotal", 13),
            ("item.Volume", 14),
        ];
        if let Some((_, column)) = map.iter().find(|(name, _)| *name == path) {
            return invoice.rows[index].cells[*column].clone();
        }
        if let Some(number) = path
            .strip_prefix("item.Spare")
            .and_then(|value| value.parse::<usize>().ok())
            .filter(|value| (1..=10).contains(value))
        {
            return invoice.rows[index].cells[14 + number].clone();
        }
    }
    let header = &invoice.header;
    match path {
        "Invoice.InvoiceNo" => header.invoice_no.clone(),
        "Invoice.InvoiceDate" => header.invoice_date.clone(),
        "Invoice.PaymentTerms" => header.payment_terms.clone(),
        "Invoice.ContractNo" => header.contract_no.clone(),
        "Invoice.Currency" => header.currency.clone(),
        "Invoice.ShippingMarks" => header.shipping_marks.clone(),
        "Exporter.ExporterNameEN" => header.exporter_name_en.clone(),
        "Exporter.ExporterNameCN" => header.exporter_name_cn.clone(),
        "Exporter.AddressEN" => header.exporter_address_en.clone(),
        "Customer.CustomerNameEN" => header.customer_name_en.clone(),
        "Customer.AddressEN" => header.customer_address_en.clone(),
        "Invoice.TotalAmount" => number(
            invoice
                .rows
                .iter()
                .filter_map(|row| parse_number(&row.cells[10]).ok())
                .sum(),
        ),
        _ => format!("«{}»", path.split('.').next_back().unwrap_or(path)),
    }
}
