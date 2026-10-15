use super::CellStyle;
use crate::layout::{flow::draw_border, text_svg};
use export_doc_domain::designer::{DetailTable, ReportBorderStyle};

pub(super) struct SpanCell<'a> {
    pub width: f32,
    pub lines: &'a [String],
    pub border: ReportBorderStyle,
    pub align: &'a str,
}

pub(super) fn draw<'a>(
    svg: &mut String,
    [left, top, height]: [f32; 3],
    style: &CellStyle,
    cells: impl IntoIterator<Item = SpanCell<'a>>,
    row_border: Option<&ReportBorderStyle>,
) {
    let mut x = left;
    for cell in cells {
        let width = cell.width;
        svg.push_str(&format!(
            "<rect x=\"{x}\" y=\"{top}\" width=\"{width}\" height=\"{height}\" fill=\"white\"/>"
        ));
        let mut border = cell.border;
        if row_border.is_some() {
            border.top = false;
            border.bottom = false;
        }
        draw_border(svg, [x, top, width, height], &border);
        text_svg(
            svg,
            cell.lines,
            x + style.left,
            top + style.content_top(height, cell.lines.len() as f32 * style.size * 1.35),
            width - style.left - style.right,
            style.size,
            style.bold,
            "#000000",
            cell.align,
        );
        x += width;
    }
    if let Some(border) = row_border {
        draw_border(svg, [left, top, x - left, height], border);
    }
}

pub(super) fn span_border(table: &DetailTable, start: usize, span: usize) -> ReportBorderStyle {
    let mut border = table.columns[start]
        .border
        .as_ref()
        .unwrap_or(&table.border)
        .clone();
    border.right = table.columns[start + span - 1]
        .border
        .as_ref()
        .unwrap_or(&table.border)
        .right;
    border
}
