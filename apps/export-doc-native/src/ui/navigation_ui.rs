use super::{Desktop, View, theme};
use eframe::egui::{self, Color32, Pos2, RichText, Stroke, Vec2};
use export_doc_native::workspace;

pub(super) fn icon(ui: &egui::Ui, center: Pos2, key: &str, color: Color32) {
    let stroke = Stroke::new(1.25, color);
    let rect = egui::Rect::from_center_size(center, egui::vec2(12., 15.));
    let painter = ui.painter();
    match key {
        "search" => {
            painter.circle_stroke(center - egui::vec2(2., 2.), 4.7, stroke);
            painter.line_segment(
                [center + egui::vec2(1.7, 1.7), center + egui::vec2(6., 6.)],
                stroke,
            );
        }
        "workspace" | "dashboard" => {
            for x in [-4., 4.] {
                for y in [-4., 4.] {
                    painter.rect_stroke(
                        egui::Rect::from_center_size(center + egui::vec2(x, y), egui::vec2(5., 5.)),
                        0.7,
                        stroke,
                        egui::StrokeKind::Inside,
                    );
                }
            }
        }
        "office" | "bookings" => {
            painter.rect_stroke(rect, 1.5, stroke, egui::StrokeKind::Inside);
            painter.line_segment(
                [
                    rect.left_top() + egui::vec2(0., 4.),
                    rect.right_top() + egui::vec2(0., 4.),
                ],
                stroke,
            );
            for x in [-3., 3.] {
                painter.line_segment(
                    [center + egui::vec2(x, -10.), center + egui::vec2(x, -5.)],
                    stroke,
                );
            }
        }
        "customers" | "people" => {
            painter.circle_stroke(center - egui::vec2(0., 4.), 3.2, stroke);
            painter.rect_stroke(
                egui::Rect::from_center_size(center + egui::vec2(0., 4.), egui::vec2(12., 7.)),
                3.,
                stroke,
                egui::StrokeKind::Inside,
            );
        }
        "system" => {
            painter.circle_stroke(center, 5.8, stroke);
            painter.circle_stroke(center, 2., stroke);
            for delta in [
                egui::vec2(0., 8.),
                egui::vec2(8., 0.),
                egui::vec2(0., -8.),
                egui::vec2(-8., 0.),
            ] {
                painter.line_segment([center + delta * 0.65, center + delta], stroke);
            }
        }
        "resources" => {
            painter.rect_stroke(rect, 2., stroke, egui::StrokeKind::Inside);
            for y in [-3., 2.] {
                painter.line_segment(
                    [center + egui::vec2(-6., y), center + egui::vec2(6., y)],
                    stroke,
                );
            }
        }
        _ => {
            painter.rect_stroke(rect, 1.5, stroke, egui::StrokeKind::Inside);
            for y in [-3., 1., 5.] {
                painter.line_segment(
                    [center + egui::vec2(-3., y), center + egui::vec2(3., y)],
                    stroke,
                );
            }
        }
    }
}
impl Desktop {
    pub(super) fn sidebar(&mut self, ui: &mut egui::Ui) {
        let width = if self.navigation_collapsed { 68. } else { 264. };
        egui::Panel::left("navigation")
            .exact_size(width)
            .resizable(false)
            .frame(egui::Frame::new().fill(theme::NAVIGATION).inner_margin(14))
            .show(ui, |ui| {
                let available = ui.available_width();
                if !self.navigation_collapsed {
                    egui::Frame::new()
                        .fill(Color32::from_rgb(33, 66, 59))
                        .stroke(Stroke::new(1., Color32::from_rgb(50, 80, 73)))
                        .corner_radius(12)
                        .inner_margin(11)
                        .show(ui, |ui| {
                            ui.set_width(available - 24.);
                            ui.horizontal(|ui| {
                                let (rect, _) = ui.allocate_exact_size(
                                    egui::vec2(36., 38.),
                                    egui::Sense::hover(),
                                );
                                ui.painter().rect_filled(rect, 10., theme::PRIMARY);
                                icon(ui, rect.center(), "documents", Color32::WHITE);
                                ui.vertical(|ui| {
                                    ui.label(
                                        RichText::new("外贸业务综合管理系统")
                                            .size(13.)
                                            .strong()
                                            .color(Color32::WHITE),
                                    );
                                    ui.label(
                                        RichText::new("全功能版")
                                            .size(12.)
                                            .color(Color32::from_rgb(157, 198, 188)),
                                    );
                                });
                            });
                        });
                    ui.add_space(14.);
                    egui::Frame::new()
                        .fill(Color32::from_rgb(30, 64, 56))
                        .corner_radius(14)
                        .inner_margin(egui::Margin::symmetric(10, 4))
                        .show(ui, |ui| {
                            ui.label(
                                RichText::new("● 多人协作")
                                    .size(12.)
                                    .color(Color32::from_rgb(127, 214, 191)),
                            );
                        });
                    ui.add_space(14.);
                    ui.scope(|ui| {
                        ui.visuals_mut().extreme_bg_color = Color32::from_rgb(38, 64, 58);
                        ui.visuals_mut().override_text_color =
                            Some(Color32::from_rgb(221, 237, 230));
                        let response = ui.add_sized(
                            [available, 40.],
                            egui::TextEdit::singleline(&mut self.navigation_search)
                                .hint_text("查找功能")
                                .margin(Vec2::new(10., 10.)),
                        );
                        self.probe("navigation-search", &response);
                    });
                    ui.add_space(10.);
                }
                let current = match self.view {
                    View::Invoice | View::Items | View::Pdf => "invoices",
                    View::Designer => "report-templates",
                    View::Workspace(key) => key,
                };
                let search = self.navigation_search.trim().to_lowercase();
                let collapsed = self.navigation_collapsed;
                let mut next = None;
                egui::ScrollArea::vertical()
                    .id_salt("navigation-scroll")
                    .auto_shrink([false, false])
                    .max_height((ui.available_height() - 48.).max(100.))
                    .show(ui, |ui| {
                        for group in workspace::NAVIGATION {
                            let items: Vec<_> = group
                                .items
                                .iter()
                                .filter(|item| {
                                    search.is_empty()
                                        || format!(
                                            "{} {} {}",
                                            group.label, item.label, item.description
                                        )
                                        .to_lowercase()
                                        .contains(&search)
                                })
                                .collect();
                            if items.is_empty() {
                                continue;
                            }
                            let opened =
                                self.expanded_navigation.contains(group.key) || !search.is_empty();
                            let active = group.items.iter().any(|item| item.key == current);
                            ui.push_id(group.key, |ui| {
                                let (_, rect) =
                                    ui.allocate_space(egui::vec2(ui.available_width(), 40.));
                                let response = ui.interact(
                                    rect,
                                    egui::Id::new(("native-navigation-group", group.key)),
                                    egui::Sense::click(),
                                );
                                if active {
                                    ui.painter().rect(
                                        rect,
                                        10.,
                                        Color32::from_rgb(20, 58, 51),
                                        Stroke::new(1., Color32::from_rgb(33, 77, 67)),
                                        egui::StrokeKind::Inside,
                                    );
                                } else if response.hovered() {
                                    ui.painter().rect_filled(
                                        rect,
                                        8.,
                                        Color32::from_rgb(31, 65, 57),
                                    );
                                }
                                icon(
                                    ui,
                                    rect.left_center() + egui::vec2(20., 0.),
                                    group.key,
                                    Color32::from_rgb(190, 217, 207),
                                );
                                if !collapsed {
                                    ui.painter().text(
                                        rect.left_center() + egui::vec2(38., 0.),
                                        egui::Align2::LEFT_CENTER,
                                        group.label,
                                        egui::FontId::proportional(13.),
                                        Color32::from_rgb(212, 233, 224),
                                    );
                                    let center = rect.right_center() - egui::vec2(15., 0.);
                                    let points = if opened {
                                        [
                                            center + egui::vec2(-4., -2.),
                                            center + egui::vec2(0., 2.),
                                            center + egui::vec2(4., -2.),
                                        ]
                                    } else {
                                        [
                                            center + egui::vec2(-2., -4.),
                                            center + egui::vec2(2., 0.),
                                            center + egui::vec2(-2., 4.),
                                        ]
                                    };
                                    ui.painter().line_segment(
                                        [points[0], points[1]],
                                        Stroke::new(1.2, Color32::from_rgb(181, 211, 198)),
                                    );
                                    ui.painter().line_segment(
                                        [points[1], points[2]],
                                        Stroke::new(1.2, Color32::from_rgb(181, 211, 198)),
                                    );
                                }
                                response.widget_info(|| {
                                    egui::WidgetInfo::labeled(
                                        egui::WidgetType::Button,
                                        true,
                                        group.label,
                                    )
                                });
                                if response.clicked() {
                                    if opened {
                                        self.expanded_navigation.remove(group.key);
                                    } else {
                                        self.expanded_navigation.insert(group.key);
                                    }
                                }
                                self.probe(format!("group-{}", group.key), &response);
                            });
                            if opened && !collapsed {
                                for item in items {
                                    ui.push_id(item.key, |ui| {
                                        ui.horizontal(|ui| {
                                            ui.add_space(26.);
                                            let (_, rect) = ui.allocate_space(egui::vec2(
                                                (ui.available_width() - 2.).max(30.),
                                                36.,
                                            ));
                                            let response = ui.interact(
                                                rect,
                                                egui::Id::new(("native-navigation-item", item.key)),
                                                egui::Sense::click(),
                                            );
                                            if current == item.key {
                                                ui.painter().rect_filled(
                                                    rect,
                                                    9.,
                                                    Color32::from_rgb(16, 121, 108),
                                                );
                                            } else if response.hovered() {
                                                ui.painter().rect_filled(
                                                    rect,
                                                    8.,
                                                    Color32::from_rgb(31, 65, 57),
                                                );
                                            }
                                            icon(
                                                ui,
                                                rect.left_center() + egui::vec2(17., 0.),
                                                item.key,
                                                Color32::from_rgb(175, 209, 195),
                                            );
                                            ui.painter().text(
                                                rect.left_center() + egui::vec2(36., 0.),
                                                egui::Align2::LEFT_CENTER,
                                                item.label,
                                                egui::FontId::proportional(13.),
                                                if current == item.key {
                                                    Color32::WHITE
                                                } else {
                                                    Color32::from_rgb(182, 210, 198)
                                                },
                                            );
                                            if response.clicked() && self.user.is_some() {
                                                next = Some(item.key);
                                            }
                                            self.probe(format!("nav-{}", item.key), &response);
                                            response.widget_info(|| {
                                                egui::WidgetInfo::labeled(
                                                    egui::WidgetType::Button,
                                                    self.user.is_some(),
                                                    item.label,
                                                )
                                            });
                                        });
                                    });
                                }
                            }
                            ui.add_space(3.);
                        }
                    });
                ui.with_layout(egui::Layout::bottom_up(egui::Align::RIGHT), |ui| {
                    let response = ui.add(
                        egui::Button::new(
                            RichText::new(if collapsed { "展开" } else { "收起导航" })
                                .size(12.)
                                .color(Color32::from_rgb(181, 212, 198)),
                        )
                        .fill(Color32::from_rgb(24, 49, 42)),
                    );
                    if response.clicked() {
                        self.navigation_collapsed = !collapsed;
                    }
                });
                if let Some(key) = next {
                    self.navigate(key, ui.ctx());
                }
            });
    }
    pub(super) fn header(&mut self, ui: &mut egui::Ui) {
        let (section, title, description) = match self.view {
            View::Workspace(key) => workspace::navigation(key)
                .map(|(group, item)| (group.label, item.label, item.description))
                .unwrap_or(("业务资料", self.view.title(), "维护业务资料")),
            View::Designer => ("资料与工具", "报表设计", "维护报表版式、字段和分页输出"),
            _ => (
                "单证与申报",
                if self.invoice.header.id == 0 {
                    "新建发票"
                } else {
                    "编辑发票"
                },
                "新建和维护出口发票，核对并输出单据",
            ),
        };
        egui::Panel::top("heading")
            .exact_size(106.)
            .frame(
                egui::Frame::new()
                    .fill(Color32::WHITE)
                    .stroke(Stroke::new(1., theme::BORDER))
                    .inner_margin(egui::Margin::symmetric(26, 16)),
            )
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    let (rect, _) =
                        ui.allocate_exact_size(egui::vec2(42., 56.), egui::Sense::hover());
                    ui.painter().rect(
                        egui::Rect::from_center_size(rect.center(), egui::vec2(42., 42.)),
                        14.,
                        Color32::from_rgb(244, 253, 250),
                        Stroke::new(1., Color32::from_rgb(218, 239, 231)),
                        egui::StrokeKind::Inside,
                    );
                    icon(ui, rect.center(), "documents", theme::PRIMARY);
                    ui.vertical(|ui| {
                        ui.label(
                            RichText::new(section)
                                .color(theme::PRIMARY)
                                .size(12.)
                                .strong(),
                        );
                        ui.label(
                            RichText::new(title)
                                .color(Color32::from_rgb(20, 40, 37))
                                .size(24.)
                                .strong(),
                        );
                        ui.label(RichText::new(description).color(theme::MUTED).size(12.));
                    });
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        let exit = ui.add(egui::Button::new("退出").corner_radius(20));
                        if exit.clicked() {
                            self.close_requested = true;
                        }
                        let name = self
                            .user
                            .as_ref()
                            .map(|user| user.full_name.as_str())
                            .unwrap_or("请登录");
                        ui.add(
                            egui::Button::new(RichText::new(format!("●  {name}")).size(12.))
                                .corner_radius(20),
                        );
                        ui.add(
                            egui::Button::new(
                                RichText::new("●  服务已连接")
                                    .color(theme::PRIMARY)
                                    .size(12.),
                            )
                            .corner_radius(20),
                        );
                        if ui
                            .add(
                                egui::Button::new(
                                    RichText::new(if self.compact { "紧凑" } else { "舒适" })
                                        .size(12.),
                                )
                                .corner_radius(20),
                            )
                            .clicked()
                        {
                            self.compact = !self.compact;
                            let mut style = (*ui.ctx().style_of(egui::Theme::Light)).clone();
                            style.spacing.item_spacing.y = if self.compact { 5. } else { 8. };
                            ui.ctx().set_style_of(egui::Theme::Light, style);
                        }
                    });
                });
            });
    }
}
