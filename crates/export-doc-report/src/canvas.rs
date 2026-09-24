use crate::layout::{escape, text_svg, wrap};

pub(crate) const PT_MM: f32 = 25.4 / 72.;
pub(crate) struct Canvas {
    pub width: f32,
    pub height: f32,
    pub svg: String,
}
impl Canvas {
    pub fn new(width: f32, height: f32) -> Self {
        Self {
            width,
            height,
            svg: format!(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" font-family=\"Noto Sans CJK SC\" width=\"{}\" height=\"{}\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"white\"/>",
                width / PT_MM,
                height / PT_MM
            ),
        }
    }
    pub fn text(
        &mut self,
        text: &str,
        x: f32,
        y: f32,
        width: f32,
        pt: f32,
        bold: bool,
        align: &str,
    ) -> f32 {
        let size = pt * PT_MM;
        let lines = wrap(text, width, size);
        text_svg(
            &mut self.svg,
            &lines,
            x,
            y,
            width,
            size,
            bold,
            "#000000",
            align,
        );
        lines.len() as f32 * size * 1.35
    }
    pub fn text_height(text: &str, width: f32, pt: f32) -> f32 {
        wrap(text, width, pt * PT_MM).len() as f32 * pt * PT_MM * 1.35
    }
    pub fn rect(&mut self, x: f32, y: f32, width: f32, height: f32, fill: &str, border: f32) {
        self.svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\" stroke=\"#000000\" stroke-width=\"{border}\"/>",escape(fill)));
    }
    pub fn finish(mut self) -> crate::document::Page {
        self.svg.push_str("</svg>");
        crate::document::Page {
            width_mm: self.width,
            height_mm: self.height,
            svg: self.svg,
        }
    }
}
