use crate::{
    RasterImage, Result,
    error::invalid,
    layout::{escape, text_svg, wrap},
};

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
    pub fn serif(width: f32, height: f32) -> Self {
        let mut canvas = Self::new(width, height);
        canvas.svg = canvas
            .svg
            .replacen("Noto Sans CJK SC", "Noto Serif CJK SC", 1);
        canvas
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
    pub fn text_box(
        &mut self,
        text: &str,
        x: f32,
        y: f32,
        width: f32,
        height: f32,
        pt: f32,
        bold: bool,
        align: &str,
    ) -> Result<()> {
        let needed = Self::text_height(text, width, pt);
        if needed > height + 0.1 {
            return Err(invalid("报表内容超过所选版式区域，请调整字号或模板尺寸。"));
        }
        self.text(text, x, y, width, pt, bold, align);
        Ok(())
    }
    pub fn text_height(text: &str, width: f32, pt: f32) -> f32 {
        wrap(text, width, pt * PT_MM).len() as f32 * pt * PT_MM * 1.35
    }
    pub fn label(
        &mut self,
        text: &str,
        x: f32,
        y: f32,
        width: f32,
        height: f32,
        pt: f32,
        bold: bool,
        align: &str,
    ) -> Result<()> {
        let needed = Self::text_height(text, width, pt);
        self.text_box(
            text,
            x,
            y + (height - needed).max(0.) / 2.,
            width,
            height,
            pt,
            bold,
            align,
        )
    }
    pub fn rect(&mut self, x: f32, y: f32, width: f32, height: f32, fill: &str, border: f32) {
        self.svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\" stroke=\"#000000\" stroke-width=\"{border}\"/>",escape(fill)));
    }
    pub fn line(&mut self, x: f32, y: f32, x2: f32, y2: f32, weight: f32) {
        self.svg.push_str(&format!("<line x1=\"{x}\" y1=\"{y}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"#000000\" stroke-width=\"{weight}\"/>"));
    }
    pub fn dashed_line(&mut self, x: f32, y: f32, x2: f32, y2: f32, weight: f32) {
        self.svg.push_str(&format!("<line x1=\"{x}\" y1=\"{y}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"#000000\" stroke-width=\"{weight}\" stroke-dasharray=\"1.1 0.7\"/>"));
    }
    pub fn image(
        &mut self,
        image: &RasterImage,
        x: f32,
        y: f32,
        width: f32,
        height: f32,
        opacity: f32,
    ) -> Result<()> {
        self.svg.push_str(&format!("<image x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" opacity=\"{}\" preserveAspectRatio=\"xMidYMid meet\" href=\"{}\"/>",opacity.clamp(0.,1.),image.data_url()?));
        Ok(())
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
