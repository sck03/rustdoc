//! Structured fixed blocks. Geometry and styling are interpreted once for
//! both SVG preview and krilla PDF output.
use super::{PT_MM, text_svg, wrap};
use crate::{ReportData, Result, error::invalid};
use export_doc_domain::{
    designer::{Element, Kind, ReportBlock, ReportBorderStyle, ReportTextStyle, grid},
    template::escape,
};

pub(super) fn render(svg: &mut String, element: &Element, data: &ReportData) -> Result<()> {
    let Kind::Flow { block, .. } = &element.kind else {
        return Ok(());
    };
    if !element.visible || !element.output_enabled || !block.output_enabled() {
        return Ok(());
    }
    let x = element.x_hundredth_mm as f32 / 100.;
    let y = element.y_hundredth_mm as f32 / 100.;
    let width = element.width_hundredth_mm as f32 / 100.;
    let height = element.height_hundredth_mm as f32 / 100.;
    let mut content = String::new();
    match block {
        ReportBlock::Row(row) => {
            let total: f32 = row.columns.iter().map(|c| c.width_percent).sum();
            let mut left = x;
            for column in &row.columns {
                let w = width * column.width_percent / total;
                let value = value(
                    data,
                    &column.content_kind,
                    &column.text,
                    &column.field_path,
                    &column.label,
                    &column.fallback_text,
                );
                box_content(
                    &mut content,
                    &value,
                    [left, y, w, height],
                    &column.style,
                    column.border.as_ref(),
                    false,
                    element,
                    data,
                    if column.content_kind == "Field" {
                        &column.field_path
                    } else {
                        ""
                    },
                )?;
                left += w;
            }
        }
        ReportBlock::Grid(block) => {
            let total: f32 = block.columns.iter().map(|c| c.width_percent).sum();
            let widths: Vec<f32> = block
                .columns
                .iter()
                .map(|c| width * c.width_percent / total)
                .collect();
            let heights: Vec<f32> = block
                .rows
                .iter()
                .map(|r| r.height_mm.unwrap_or(8.))
                .collect();
            let mut top = y;
            if !block.title.is_empty() {
                let size = element.style.font_size_pt * PT_MM;
                let lines = wrap(&block.title, width, size);
                text_svg(
                    &mut content,
                    &lines,
                    x,
                    top,
                    width,
                    size,
                    true,
                    &element.style.color,
                    "Left",
                );
                top += lines.len() as f32 * size * 1.35;
            }
            for place in grid::placements(block).map_err(invalid)? {
                let cell = place.cell;
                let rect = [
                    x + widths[..place.column].iter().sum::<f32>(),
                    top + heights[..place.row].iter().sum::<f32>(),
                    widths[place.column..place.column + place.col_span]
                        .iter()
                        .sum(),
                    heights[place.row..place.row + place.row_span].iter().sum(),
                ];
                let value = if cell.content_kind == "CheckboxGroup" {
                    let selected = data.text(&cell.field_path);
                    cell.checkbox_options
                        .iter()
                        .map(|option| {
                            format!(
                                "{} {}",
                                if selected == option.value {
                                    "☑"
                                } else {
                                    "☐"
                                },
                                option.label
                            )
                        })
                        .collect::<Vec<_>>()
                        .join("  ")
                } else {
                    value(
                        data,
                        &cell.content_kind,
                        &cell.text,
                        &cell.field_path,
                        &cell.label,
                        &cell.fallback_text,
                    )
                };
                let style = inherit(&cell.style, &block.default_cell_style);
                box_content(
                    &mut content,
                    &value,
                    rect,
                    &style,
                    Some(cell.border.as_ref().unwrap_or(&block.border)),
                    cell.vertical_text,
                    element,
                    data,
                    if cell.content_kind == "Field" {
                        &cell.field_path
                    } else {
                        ""
                    },
                )?;
            }
        }
        ReportBlock::Conditional(block) => {
            let field = data.value(&block.condition.field_path, None);
            let actual = data.text(&block.condition.field_path);
            let visible = match block.condition.operator.as_str() {
                "Equals" => actual == block.condition.value,
                "NotEquals" => actual != block.condition.value,
                "HasValue" => !field.is_null() && field != false && !actual.is_empty(),
                _ => return Err(invalid("条件操作符不受支持。")),
            };
            if visible {
                let c = &block.content;
                let value = value(
                    data,
                    &c.kind,
                    &c.text,
                    &c.field_path,
                    &c.label,
                    &c.fallback_text,
                );
                box_content(
                    &mut content,
                    &value,
                    [x, y, width, height],
                    &block.style,
                    block.border.as_ref(),
                    false,
                    element,
                    data,
                    if c.kind == "Field" { &c.field_path } else { "" },
                )?;
            }
        }
        _ => return Ok(()),
    }
    let rotation = element.rotation_deg;
    svg.push_str(&format!(
        "<g font-family=\"{}\" transform=\"rotate({rotation} {} {})\">{content}</g>",
        escape(&element.style.font_family),
        x + width / 2.,
        y + height / 2.
    ));
    Ok(())
}

fn value(
    data: &ReportData,
    kind: &str,
    text: &str,
    path: &str,
    label: &str,
    fallback: &str,
) -> String {
    if kind != "Field" {
        return text.into();
    }
    let mut value = data.text(path);
    if value.is_empty() {
        value = fallback.into();
    }
    if !label.is_empty() {
        value = format!("{label}: {value}");
    }
    value
}

fn inherit(style: &ReportTextStyle, base: &ReportTextStyle) -> ReportTextStyle {
    ReportTextStyle {
        font_size_pt: style.font_size_pt.or(base.font_size_pt),
        bold: style.bold.or(base.bold),
        align: style.align.clone().or_else(|| base.align.clone()),
        margin_top_mm: style.margin_top_mm.or(base.margin_top_mm),
        margin_right_mm: style.margin_right_mm.or(base.margin_right_mm),
        margin_bottom_mm: style.margin_bottom_mm.or(base.margin_bottom_mm),
        margin_left_mm: style.margin_left_mm.or(base.margin_left_mm),
    }
}

#[allow(clippy::too_many_arguments)]
fn box_content(
    svg: &mut String,
    value: &str,
    rect: [f32; 4],
    style: &ReportTextStyle,
    border: Option<&ReportBorderStyle>,
    vertical: bool,
    element: &Element,
    data: &ReportData,
    path: &str,
) -> Result<()> {
    let [x, y, width, height] = rect;
    if let Some(border) = border {
        draw_border(svg, rect, border);
    }
    let left = style.margin_left_mm.unwrap_or(1.2);
    let right = style.margin_right_mm.unwrap_or(1.2);
    let top = style.margin_top_mm.unwrap_or(1.2);
    let bottom = style.margin_bottom_mm.unwrap_or(1.2);
    let size = style.font_size_pt.unwrap_or(element.style.font_size_pt) * PT_MM;
    let w = width - left - right;
    let h = height - top - bottom;
    if w <= 0. || h <= 0. {
        return Err(invalid("表格单元格的内边距超过可用空间。"));
    }
    if path == "Invoice.ShippingMarks"
        && let Some(image) = data.images.get(path)
    {
        svg.push_str(&format!("<image x=\"{}\" y=\"{}\" width=\"{w}\" height=\"{h}\" preserveAspectRatio=\"xMidYMid meet\" href=\"{}\"/>",x+left,y+top,image.data_url()?));
        return Ok(());
    }
    let lines = if vertical {
        value
            .chars()
            .filter(|c| !c.is_whitespace())
            .map(|c| c.to_string())
            .collect()
    } else {
        wrap(value, w, size)
    };
    text_svg(
        svg,
        &lines,
        x + left,
        y + top,
        w,
        size,
        style.bold.unwrap_or(element.style.bold),
        &element.style.color,
        style.align.as_deref().unwrap_or(&element.style.align),
    );
    Ok(())
}

fn draw_border(svg: &mut String, [x, y, w, h]: [f32; 4], border: &ReportBorderStyle) {
    if border.width_px <= 0. || border.style == "None" {
        return;
    }
    let dash = if border.style == "Dashed" {
        " stroke-dasharray=\"2 1\""
    } else {
        ""
    };
    for (enabled, [x1, y1, x2, y2]) in [
        (border.top, [x, y, x + w, y]),
        (border.right, [x + w, y, x + w, y + h]),
        (border.bottom, [x, y + h, x + w, y + h]),
        (border.left, [x, y, x, y + h]),
    ] {
        if enabled {
            svg.push_str(&format!("<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{}\" stroke-width=\"{}\"{dash}/>",escape(&border.color),border.width_px*25.4/96.));
        }
    }
}
