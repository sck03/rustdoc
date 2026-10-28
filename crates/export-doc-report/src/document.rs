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
const MAX_SVG_PAGE: usize = 64 * 1024 * 1024;
const MAX_SVG_DOCUMENT: usize = 128 * 1024 * 1024;
const MAX_HTML: usize = 96 * 1024 * 1024;
pub(crate) fn check_svg_size(svg: &str) -> Result<()> {
    if svg.len() > MAX_SVG_PAGE {
        return Err(invalid("报表单页 SVG 超过 64 MiB。"));
    }
    Ok(())
}
pub(crate) fn push_svg(pages: &mut Vec<String>, svg: String) -> Result<()> {
    check_svg_size(&svg)?;
    if pages.len() >= 500
        || pages
            .iter()
            .map(String::len)
            .sum::<usize>()
            .saturating_add(svg.len())
            > MAX_SVG_DOCUMENT
    {
        return Err(invalid("报表页数或 SVG 累计容量超过处理上限。"));
    }
    pages.push(svg);
    Ok(())
}
struct Conversion<'a> {
    cancelled: &'a AtomicBool,
    started: Instant,
}
impl<'a> Conversion<'a> {
    fn new(document: &Document, cancelled: &'a AtomicBool) -> Result<Self> {
        if document.pages.is_empty()
            || document.pages.len() > 500
            || document
                .pages
                .iter()
                .map(|page| page.svg.len())
                .sum::<usize>()
                > MAX_SVG_DOCUMENT
        {
            return Err(invalid("报表页数须为 1–500 且 SVG 累计不超过 128 MiB。"));
        }
        let control = Self {
            cancelled,
            started: Instant::now(),
        };
        control.check()?;
        Ok(control)
    }
    fn check(&self) -> Result<()> {
        if self.cancelled.load(Ordering::Relaxed) {
            return Err(Error {
                kind: ErrorKind::Cancelled,
                message: "报表转换已取消。".into(),
            });
        }
        if self.started.elapsed() > Duration::from_secs(90) {
            return Err(Error {
                kind: ErrorKind::Timeout,
                message: "报表转换超过 90 秒。".into(),
            });
        }
        Ok(())
    }
    fn page(&self, svg: &str) -> Result<()> {
        self.check()?;
        check_svg_size(svg)?;
        let source =
            usvg::roxmltree::Document::parse(svg).map_err(|cause| invalid(cause.to_string()))?;
        let characters = source
            .descendants()
            .filter(|node| node.has_tag_name("text"))
            .filter_map(|node| node.text())
            .map(|text| text.chars().count())
            .sum::<usize>();
        if characters > 200000 {
            return Err(invalid("报表单页文字超过处理容量，请拆分页。"));
        }
        Ok(())
    }
}
fn append_html(html: &mut String, text: &str) -> Result<()> {
    if text.len() > MAX_HTML.saturating_sub(html.len()) {
        return Err(invalid("报表预览超过容量上限。"));
    }
    let required = html.len() + text.len();
    if html.capacity() < required {
        let capacity = required
            .max(html.capacity().saturating_mul(2))
            .min(MAX_HTML);
        html.try_reserve_exact(capacity - html.len())
            .map_err(|_| unavailable("无法分配报表预览缓冲。"))?;
    }
    html.push_str(text);
    Ok(())
}
impl Document {
    pub fn html(&self, fonts: &crate::Fonts, cancelled: &AtomicBool) -> Result<String> {
        let control = Conversion::new(self, cancelled)?;
        let options = fonts.svg_options()?;
        let mut html = String::from(
            "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>html,body{margin:0;padding:0;background:#e8ebed}.report-page{margin:12px auto;background:white;break-after:page;page-break-after:always;max-width:100%}.report-page:last-child{break-after:auto;page-break-after:auto}.report-page svg{display:block;width:100%;height:auto}@media print{html,body{background:white}.report-page{margin:0;max-width:none}}@page{margin:0}",
        );
        for (index, page) in self.pages.iter().enumerate() {
            html.push_str(&format!("@page report{index}{{size:{}mm {}mm}}#report{index}{{page:report{index};width:{}mm}}",page.width_mm,page.height_mm,page.width_mm));
        }
        html.push_str("</style></head><body>");
        for (index, page) in self.pages.iter().enumerate() {
            control.page(&page.svg)?;
            let tree = usvg::Tree::from_str(&page.svg, &options)
                .map_err(|error| invalid(format!("报表预览布局无效：{error}")))?;
            let mut svg = tree.to_string(&usvg::WriteOptions::default());
            control.check()?;
            if svg.len() > MAX_HTML.saturating_sub(html.len()) {
                return Err(invalid("报表预览超过容量上限。"));
            }
            // usvg writes absolute dimensions without a viewBox. Restore the
            // viewport so CSS resizing scales the entire A4 page, including glyphs.
            svg.insert_str(
                4,
                &format!(
                    " viewBox=\"0 0 {} {}\"",
                    tree.size().width(),
                    tree.size().height()
                ),
            );
            // Outlined glyphs keep preview identical to PDF even when the web
            // client has none of our fonts. Retain readable page content for AT.
            let source = usvg::roxmltree::Document::parse(&page.svg)
                .map_err(|error| invalid(error.to_string()))?;
            let text = source
                .descendants()
                .filter(|node| node.has_tag_name("text"))
                .filter_map(|node| node.text())
                .collect::<Vec<_>>()
                .join("\n");
            if let Some(end) = svg.find('>') {
                svg.insert_str(
                    end + 1,
                    &format!("<desc>{}</desc>", crate::layout::escape(&text)),
                );
            }
            append_html(
                &mut html,
                &format!("<section class=\"report-page\" id=\"report{index}\">"),
            )?;
            append_html(&mut html, &svg)?;
            append_html(&mut html, "</section>")?;
        }
        control.check()?;
        append_html(&mut html, "</body></html>")?;
        Ok(html)
    }
}

pub fn pdf_document(
    document: &Document,
    fonts: &crate::Fonts,
    cancelled: &AtomicBool,
) -> Result<Vec<u8>> {
    let control = Conversion::new(document, cancelled)?;
    let options = fonts.svg_options()?;
    let mut output = PdfDocument::new();
    for input in &document.pages {
        control.page(&input.svg)?;
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
        control.check()?;
    }
    let bytes = output
        .finish()
        .map_err(|e| unavailable(format!("PDF 编码失败:{e}")))?;
    control.check()?;
    if bytes.len() > 64 * 1024 * 1024 {
        return Err(invalid("PDF 输出超过 64 MiB。"));
    }
    Ok(bytes)
}

#[cfg(test)]
mod conversion_tests {
    use super::*;
    #[test]
    fn cancelled_preview_exits_before_loading_fonts_and_capacity_checks_precede_append() {
        let document = Document {
            pages: vec![Page {
                width_mm: 210.,
                height_mm: 297.,
                svg: "<svg/>".into(),
            }],
        };
        let fonts = crate::Fonts::new(std::path::Path::new("not-a-font/font.otf"));
        assert_eq!(
            document
                .html(&fonts, &AtomicBool::new(true))
                .unwrap_err()
                .kind,
            ErrorKind::Cancelled
        );
        let mut html = String::from("kept");
        let text = "x".repeat(MAX_HTML);
        assert!(append_html(&mut html, &text).is_err());
        assert_eq!(html, "kept");
        let mut pages = (0..500).map(|_| "<svg/>".into()).collect();
        assert!(push_svg(&mut pages, "<svg/>".into()).is_err());
        assert_eq!(pages.len(), 500);
    }
}
