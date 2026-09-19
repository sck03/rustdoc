use eframe::egui::{self, Color32, FontFamily, FontId, RichText};
use std::{collections::BTreeMap, path::Path};
pub const PRIMARY: Color32 = Color32::from_rgb(15, 127, 114);
pub const INK: Color32 = Color32::from_rgb(23, 63, 59);
pub const MUTED: Color32 = Color32::from_rgb(82, 104, 102);
pub const BACKGROUND: Color32 = Color32::from_rgb(243, 247, 245);
pub const BORDER: Color32 = Color32::from_rgb(217, 227, 224);
pub const PALE: Color32 = Color32::from_rgb(238, 246, 245);
pub const ERROR: Color32 = Color32::from_rgb(171, 66, 38);
pub const NAVIGATION: Color32 = Color32::from_rgb(16, 42, 37);

pub fn configure(ctx: &egui::Context, font_path: &Path) -> Result<(), String> {
    let mut fonts = egui::FontDefinitions::default();
    let font = std::fs::read(font_path).map_err(|error| format!("读取中文字体失败：{error}"))?;
    fonts
        .font_data
        .insert("Noto CJK".into(), egui::FontData::from_owned(font).into());
    fonts
        .families
        .entry(FontFamily::Proportional)
        .or_default()
        .insert(0, "Noto CJK".into());
    fonts
        .families
        .entry(FontFamily::Monospace)
        .or_default()
        .push("Noto CJK".into());
    let bold_path = font_path.with_file_name("NotoSansCJKsc-Bold.otf");
    if let Ok(font) = std::fs::read(bold_path) {
        fonts
            .font_data
            .insert("Noto Bold".into(), egui::FontData::from_owned(font).into());
        fonts
            .families
            .insert(FontFamily::Name("bold".into()), vec!["Noto Bold".into()]);
    }
    ctx.set_fonts(fonts);
    let mut style = egui::Style::default();
    style.text_styles = BTreeMap::from([
        (egui::TextStyle::Heading, FontId::proportional(24.)),
        (egui::TextStyle::Body, FontId::proportional(14.)),
        (egui::TextStyle::Button, FontId::proportional(14.)),
        (egui::TextStyle::Small, FontId::proportional(12.)),
        (egui::TextStyle::Monospace, FontId::monospace(14.)),
    ]);
    style.spacing.item_spacing = egui::vec2(8., 6.);
    style.spacing.button_padding = egui::vec2(10., 7.);
    style.spacing.interact_size.y = 28.;
    style.visuals = egui::Visuals::light();
    style.visuals.override_text_color = Some(INK);
    style.visuals.panel_fill = BACKGROUND;
    style.visuals.selection.bg_fill = PALE;
    style.visuals.selection.stroke.color = PRIMARY;
    style.visuals.widgets.inactive.bg_fill = Color32::WHITE;
    style.visuals.widgets.inactive.weak_bg_fill = Color32::WHITE;
    style.visuals.widgets.inactive.bg_stroke = egui::Stroke::new(1., BORDER);
    style.visuals.widgets.hovered.bg_fill = PALE;
    style.visuals.widgets.hovered.weak_bg_fill = PALE;
    style.visuals.widgets.hovered.bg_stroke = egui::Stroke::new(1., PRIMARY);
    style.visuals.widgets.active.bg_fill = PALE;
    style.visuals.widgets.active.weak_bg_fill = PALE;
    for widget in [
        &mut style.visuals.widgets.inactive,
        &mut style.visuals.widgets.hovered,
        &mut style.visuals.widgets.active,
    ] {
        widget.corner_radius = egui::CornerRadius::same(6);
    }
    ctx.set_theme(egui::Theme::Light);
    ctx.set_style_of(egui::Theme::Light, style);
    Ok(())
}
pub fn primary(text: &str) -> egui::Button<'_> {
    egui::Button::new(RichText::new(text).color(Color32::WHITE))
        .fill(PRIMARY)
        .stroke(egui::Stroke::NONE)
}
pub fn card() -> egui::Frame {
    egui::Frame::new()
        .fill(Color32::WHITE)
        .stroke(egui::Stroke::new(1., BORDER))
        .corner_radius(10)
        .inner_margin(18)
}
pub fn title(ui: &mut egui::Ui, text: &str) {
    ui.label(RichText::new(text).size(18.).strong());
    ui.add_space(6.);
}
pub fn field(ui: &mut egui::Ui, label: &str, text: &mut String, id: &str) -> egui::Response {
    let label = ui.label(RichText::new(label).size(13.).color(MUTED));
    ui.add_sized(
        [ui.available_width(), 34.],
        egui::TextEdit::singleline(text).id_salt(id),
    )
    .labelled_by(label.id)
}
