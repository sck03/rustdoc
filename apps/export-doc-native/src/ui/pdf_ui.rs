use super::{Desktop, theme, worker::Work};
use eframe::egui::{self, RichText};
use export_doc_native::paths::{save_pdf, suggested_pdf_name};

impl Desktop {
    pub(super) fn pdf_ui(&mut self, ui: &mut egui::Ui) {
        self.invoice_toolbar(ui);
        theme::card().show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label("输出模板");
                let selected = if self.pdf_template.is_empty() {
                    "当前原生模板".to_owned()
                } else {
                    self.templates
                        .iter()
                        .find(|template| template.template_path == self.pdf_template)
                        .map(|template| template.display_name.clone())
                        .unwrap_or_else(|| self.pdf_template.clone())
                };
                egui::ComboBox::from_id_salt("output-template")
                    .selected_text(selected)
                    .width(240.)
                    .show_ui(ui, |ui| {
                        ui.selectable_value(&mut self.pdf_template, String::new(), "当前原生模板");
                        for template in &self.templates {
                            ui.selectable_value(
                                &mut self.pdf_template,
                                template.template_path.clone(),
                                &template.display_name,
                            );
                        }
                    });
                let render = ui.add_enabled(!self.busy(), theme::primary("生成 PDF 预览"));
                self.probe("render-pdf", &render);
                if render.clicked() {
                    self.render_pdf(ui.ctx());
                }
                let save = ui.add_enabled(self.pdf_bytes.is_some(), egui::Button::new("另存 PDF…"));
                self.probe("save-pdf", &save);
                if save.clicked() {
                    self.choose_pdf_destination();
                }
                if let Some(path) = self.pdf_saved_path.clone() {
                    if ui.button("打开已保存文件").clicked() {
                        #[cfg(windows)]
                        {
                            let result = std::process::Command::new("explorer.exe")
                                .arg(&path)
                                .spawn();
                            if let Err(error) = result {
                                self.error = Some(format!("无法打开文件：{error}"));
                            }
                        }
                        #[cfg(not(windows))]
                        {
                            self.status = format!("PDF 已保存：{}", path.display());
                        }
                    }
                }
            });
            ui.label(
                RichText::new("使用已保存的发票和模板生成。文件只会写入你选择的位置。")
                    .color(theme::MUTED)
                    .size(12.),
            );
            if self.invoice_dirty || self.invoice.header.id <= 0 {
                ui.label(
                    RichText::new("发票有未保存修改，请先保存发票。")
                        .color(egui::Color32::from_rgb(150, 90, 10)),
                );
            }
            if self.pdf_template.is_empty() && (self.design_dirty || self.template_record.is_none())
            {
                ui.label(
                    RichText::new("请先在报表设计中保存并应用模板。")
                        .color(egui::Color32::from_rgb(150, 90, 10)),
                );
            }
        });
        ui.add_space(12.);
        if let Some(texture) = self.pdf_texture.clone() {
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(
                        !self.busy() && self.pdf_page > 0,
                        egui::Button::new("上一页"),
                    )
                    .clicked()
                {
                    if let Some(bytes) = self.pdf_bytes.clone() {
                        self.start(
                            ui.ctx(),
                            Work::Page {
                                bytes,
                                index: self.pdf_page - 1,
                            },
                            "正在读取 PDF 页面",
                        );
                    }
                }
                ui.label(format!(
                    "第 {} / {} 页",
                    self.pdf_page + 1,
                    self.pdf_page_count
                ));
                if ui
                    .add_enabled(
                        !self.busy() && self.pdf_page + 1 < self.pdf_page_count,
                        egui::Button::new("下一页"),
                    )
                    .clicked()
                {
                    if let Some(bytes) = self.pdf_bytes.clone() {
                        self.start(
                            ui.ctx(),
                            Work::Page {
                                bytes,
                                index: self.pdf_page + 1,
                            },
                            "正在读取 PDF 页面",
                        );
                    }
                }
                if let Some(bytes) = &self.pdf_bytes {
                    ui.label(
                        RichText::new(format!(
                            "{:.1} KiB · 系统 PDF 预览",
                            bytes.len() as f64 / 1024.
                        ))
                        .color(theme::MUTED)
                        .size(12.),
                    );
                }
            });
            egui::ScrollArea::both()
                .id_salt("pdf-scroll")
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    let size = texture.size_vec2();
                    let width = ui.available_width().min(size.x).min(860.);
                    let ratio = width / size.x;
                    ui.horizontal(|ui| {
                        ui.add_space(((ui.available_width() - width) / 2.).max(0.));
                        ui.add(egui::Image::new(&texture).fit_to_exact_size(size * ratio));
                    });
                });
        } else {
            theme::card().show(ui, |ui| {
                ui.set_min_height((ui.available_height() - 40.).max(180.));
                ui.vertical_centered(|ui| {
                    ui.add_space(70.);
                    ui.label(
                        RichText::new(if self.busy() {
                            "正在生成 PDF…"
                        } else {
                            "PDF 预览"
                        })
                        .size(26.)
                        .color(theme::INK),
                    );
                    ui.add_space(12.);
                    ui.label("完成发票编辑与报表设计后，点击“生成 PDF 预览”。");
                    ui.label(
                        RichText::new("中文、换行和分页以这里的最终 PDF 为准。")
                            .size(13.)
                            .color(theme::MUTED),
                    );
                });
            });
        }
    }
    fn render_pdf(&mut self, ctx: &egui::Context) {
        if self.invoice_dirty || self.invoice.header.id <= 0 {
            self.error = Some("请先保存发票，再生成 PDF。".into());
            return;
        }
        let template = if self.pdf_template.is_empty() {
            if self.design_dirty {
                self.error = Some("请先保存并应用报表模板。".into());
                return;
            }
            let Some(record) = &self.template_record else {
                self.error = Some("请先保存并应用报表模板。".into());
                return;
            };
            if record.status != "Published" {
                self.error = Some("模板尚未应用，请重新保存并应用。".into());
                return;
            }
            format!("user-template:{}", record.id)
        } else {
            self.pdf_template.clone()
        };
        self.start(
            ctx,
            Work::Pdf {
                invoice_id: self.invoice.header.id,
                template,
            },
            "正在生成 PDF",
        );
    }
    fn choose_pdf_destination(&mut self) {
        let Some(bytes) = self.pdf_bytes.clone() else {
            return;
        };
        let dialog = rfd::FileDialog::new()
            .set_title("保存发票 PDF")
            .set_file_name(suggested_pdf_name(&self.invoice.header.invoice_no))
            .add_filter("PDF 文档", &["pdf"]);
        if let Some(path) = dialog.save_file() {
            match save_pdf(&path, &bytes) {
                Ok(()) => {
                    self.status = format!("PDF 已保存：{}", path.display());
                    self.pdf_saved_path = Some(path);
                }
                Err(error) => self.error = Some(error),
            }
        }
    }
}
