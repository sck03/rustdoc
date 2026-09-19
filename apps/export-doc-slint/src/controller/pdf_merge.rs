use super::*;
use crate::{PageTab, PdfMerge};
impl Desktop {
    pub fn pdf_merge_action(&mut self, action: &str, index: i32) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<PdfMerge>();
        let selected = index.checked_sub(1).filter(|n| *n >= 0).map(|n| n as usize);
        match action {
            "add" => {
                if let Some(files) = self.platform.choose_pdf_sources(ui.window()) {
                    if self.pdf_sources.len() + files.len() > 100 {
                        self.error("每次最多合并 100 个 PDF 文件。");
                        return;
                    }
                    self.pdf_sources.extend(files);
                }
            }
            "clear" => {
                self.pdf_sources.clear();
                view.set_selected(0);
            }
            "remove" => {
                if let Some(index) = selected.filter(|i| *i < self.pdf_sources.len()) {
                    self.pdf_sources.remove(index);
                    view.set_selected(0);
                }
            }
            "up" => {
                if let Some(index) = selected.filter(|i| *i > 0 && *i < self.pdf_sources.len()) {
                    self.pdf_sources.swap(index, index - 1);
                    view.set_selected(index as i32);
                }
            }
            "down" => {
                if let Some(index) = selected.filter(|i| *i + 1 < self.pdf_sources.len()) {
                    self.pdf_sources.swap(index, index + 1);
                    view.set_selected(index as i32 + 2);
                }
            }
            "merge" => {
                if !self.can(START_PDF_MERGE_SAVE_TO_PATH_JOB) || self.pdf_sources.is_empty() {
                    return;
                }
                let Some(destination) = self
                    .platform
                    .choose_pdf_destination(ui.window(), "合并文件.pdf")
                else {
                    return;
                };
                self.start(Work::FileJob{operation:START_PDF_MERGE_SAVE_TO_PATH_JOB,parameters:vec![],body:serde_json::json!({"sourceFiles":self.pdf_sources,"destinationPath":destination}),destination});
                return;
            }
            _ => {}
        }
        view.set_files(model(
            self.pdf_sources
                .iter()
                .map(|path| PageTab {
                    key: path
                        .parent()
                        .map(|p| p.to_string_lossy().into_owned())
                        .unwrap_or_default()
                        .into(),
                    label: path
                        .file_name()
                        .map(|p| p.to_string_lossy().into_owned())
                        .unwrap_or_default()
                        .into(),
                })
                .collect(),
        ));
    }
}
