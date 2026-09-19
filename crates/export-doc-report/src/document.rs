use crate::{
    Error, ErrorKind, Result,
    canvas::PT_MM,
    error::{invalid, unavailable},
};
use krilla::Document as PdfDocument;
use krilla::geom::Size;
use krilla::page::PageSettings;
use krilla_svg::{SurfaceExt, SvgSettings};
use std::{
    path::Path,
    sync::atomic::{AtomicBool, Ordering},
    time::{Duration, Instant},
};

pub struct Page {
    pub width_mm: f32,
    pub height_mm: f32,
    pub svg: String,
}
pub struct Document {
    pub pages: Vec<Page>,
}
impl Document {
    pub fn html(&self) -> Result<String> {
        let mut html = String::from(
            "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>html,body{margin:0;padding:0;background:#e8ebed}.report-page{margin:12px auto;background:white;break-after:page;page-break-after:always;max-width:100%}.report-page:last-child{break-after:auto;page-break-after:auto}.report-page svg{display:block;width:100%;height:auto}@media print{html,body{background:white}.report-page{margin:0;max-width:none}}@page{margin:0}",
        );
        for (index, page) in self.pages.iter().enumerate() {
            html.push_str(&format!("@page report{index}{{size:{}mm {}mm}}#report{index}{{page:report{index};width:{}mm}}",page.width_mm,page.height_mm,page.width_mm));
        }
        html.push_str("</style></head><body>");
        for (index, page) in self.pages.iter().enumerate() {
            html.push_str(&format!(
                "<section class=\"report-page\" id=\"report{index}\">{}</section>",
                page.svg
            ));
            if html.len() > 96 * 1024 * 1024 {
                return Err(invalid("报表预览超过容量上限。"));
            }
        }
        html.push_str("</body></html>");
        Ok(html)
    }
}

pub fn pdf_document(document: &Document, font: &Path, cancelled: &AtomicBool) -> Result<Vec<u8>> {
    let started = Instant::now();
    if document.pages.is_empty() || document.pages.len() > 500 {
        return Err(invalid("报表页数必须为 1–500。"));
    }
    let mut options = usvg::Options::default();
    options
        .fontdb_mut()
        .load_font_file(font)
        .map_err(|e| unavailable(format!("无法读取随包中文字体:{e}")))?;
    for name in ["NotoSansCJKsc-Bold.otf", "NotoSerifCJKsc-Regular.otf"] {
        let path = font.with_file_name(name);
        if path.is_file() {
            options
                .fontdb_mut()
                .load_font_file(path)
                .map_err(|e| unavailable(e.to_string()))?;
        }
    }
    options.font_family = "Noto Sans CJK SC".into();
    let mut output = PdfDocument::new();
    for input in &document.pages {
        if cancelled.load(Ordering::Relaxed) {
            return Err(Error {
                kind: ErrorKind::Cancelled,
                message: "PDF 输出已取消。".into(),
            });
        }
        if started.elapsed() > Duration::from_secs(90) {
            return Err(Error {
                kind: ErrorKind::Timeout,
                message: "PDF 输出超过 90 秒。".into(),
            });
        }
        if input.width_mm <= 0.
            || input.height_mm <= 0.
            || !input.width_mm.is_finite()
            || !input.height_mm.is_finite()
        {
            return Err(invalid("报表纸张尺寸无效。"));
        }
        let tree = usvg::Tree::from_str(&input.svg, &options)
            .map_err(|e| invalid(format!("报表布局无效:{e}")))?;
        let svg_size = Size::from_wh(tree.size().width(), tree.size().height())
            .ok_or_else(|| invalid("报表 SVG 尺寸无效。"))?;
        let page_size = Size::from_wh(input.width_mm / PT_MM, input.height_mm / PT_MM)
            .ok_or_else(|| invalid("报表纸张尺寸无效。"))?;
        let mut page = output.start_page_with(PageSettings::new(page_size));
        let mut surface = page.surface();
        surface.draw_svg(&tree, svg_size, SvgSettings::default());
        surface.finish();
        page.finish();
        if cancelled.load(Ordering::Relaxed) {
            return Err(Error {
                kind: ErrorKind::Cancelled,
                message: "PDF 输出已取消。".into(),
            });
        }
    }
    let bytes = output
        .finish()
        .map_err(|e| unavailable(format!("PDF 编码失败:{e}")))?;
    if bytes.len() > 64 * 1024 * 1024 {
        return Err(invalid("PDF 输出超过 64 MiB。"));
    }
    Ok(bytes)
}
