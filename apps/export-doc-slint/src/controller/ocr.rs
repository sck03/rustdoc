use super::*;
use crate::{DataRow, Ocr, OcrLine};
use serde_json::Value;

impl Desktop {
    pub fn setup_ocr(&mut self) {
        self.reset_ocr();
        self.request(GET_HEALTH, 0, vec![], None, "ocr:health");
    }
    fn reset_ocr(&mut self) {
        self.ocr = Default::default();
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Ocr>();
            view.set_image(Default::default());
            view.set_has_image(false);
            view.set_rows(model(vec![]));
            view.set_lines(model(vec![]));
            view.set_text("".into());
            view.set_selected(0);
            view.set_zoom(1.);
            view.set_source("".into());
        }
    }
    pub fn ocr_action(&mut self, action: &str, index: i32) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Ocr>();
        match action {
            "choose" => {
                if let Some(source) = self.platform.choose_source(
                    ui.window(),
                    "OCR 图片",
                    &["png", "jpg", "jpeg", "bmp", "tiff", "tif", "webp"],
                ) {
                    self.start(Work::OpenOcr(crate::worker::ocr::Source::File(source)));
                }
            }
            "paste" => match self.platform.paste_image() {
                Ok(frame) => {
                    self.start(Work::OpenOcr(crate::worker::ocr::Source::Clipboard(frame)))
                }
                Err(cause) => self.error(cause),
            },
            "recognize" => {
                if !self.can(UPLOAD_OCR_IMAGE) {
                    return;
                }
                if let Some(bytes) = &self.ocr.bytes {
                    self.start(Work::RecognizeOcr {
                        name: self.ocr.name.clone(),
                        bytes: bytes.clone(),
                    });
                }
            }
            "copy" | "copy-line" => {
                let text = if action == "copy" {
                    view.get_text().to_string()
                } else {
                    view.get_selected()
                        .checked_sub(1)
                        .filter(|i| *i >= 0)
                        .and_then(|i| self.ocr.lines.get(i as usize))
                        .map(|row| row.text.clone())
                        .unwrap_or_default()
                };
                if let Err(cause) = self.platform.copy_text(ui.window(), text) {
                    self.error(cause);
                } else {
                    self.status("识别文字已复制。");
                }
            }
            "line" => {
                view.set_selected(index);
            }
            "reset" => self.reset_ocr(),
            _ => {}
        }
    }
    pub fn ocr_image_loaded(&mut self, image: crate::worker::ocr::Image) {
        self.reset_ocr();
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Ocr>();
            let pixels = slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(
                &image.preview.rgba,
                image.preview.width,
                image.preview.height,
            );
            view.set_image(slint::Image::from_rgba8(pixels));
            view.set_image_width(image.width as f32);
            view.set_image_height(image.height as f32);
            view.set_source(format!("{} · {} × {}", image.name, image.width, image.height).into());
            view.set_has_image(true);
        }
        self.ocr.name = image.name;
        self.ocr.width = image.width;
        self.ocr.height = image.height;
        self.ocr.bytes = Some(image.bytes);
    }
    pub fn ocr_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Ocr>();
        if reply == "ocr:health" {
            let dependency = value["runtimeDependencies"]
                .as_array()
                .into_iter()
                .flatten()
                .find(|r| r["key"] == "ocr-runtime");
            view.set_ready(
                dependency.is_some_and(|r| r["ready"] == true) && self.can(UPLOAD_OCR_IMAGE),
            );
            view.set_status(
                dependency
                    .and_then(|r| r["message"].as_str())
                    .unwrap_or("当前未启用 OCR 能力模块。")
                    .into(),
            );
            return;
        }
        match serde_json::from_value::<ApiOcrRecognizeImageResponse>(value) {
            Ok(response) => {
                view.set_text(response.full_text.into());
                self.ocr.lines = response.lines;
                view.set_selected(0);
                view.set_rows(model(
                    self.ocr
                        .lines
                        .iter()
                        .enumerate()
                        .map(|(i, row)| DataRow {
                            id: i as i32 + 1,
                            cells: model(vec![(i + 1).to_string().into(), row.text.clone().into()]),
                        })
                        .collect(),
                ));
                view.set_lines(model(
                    self.ocr
                        .lines
                        .iter()
                        .enumerate()
                        .map(|(i, row)| OcrLine {
                            index: i as i32 + 1,
                            x: row.x as f32,
                            y: row.y as f32,
                            width: row.width as f32,
                            height: row.height as f32,
                        })
                        .collect(),
                ));
                self.status(format!("识别完成，共 {} 行。", self.ocr.lines.len()));
            }
            Err(cause) => self.error(cause.to_string()),
        }
    }
}
