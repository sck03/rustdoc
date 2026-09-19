//! Vector PDF output. Layout, pagination and embedded fonts run inside Rust.
use crate::error::{Result, invalid};
use export_doc_contracts::generated_api::ApiInvoiceDetailDto;
use export_doc_domain::designer::{Design, DetailTable, Element, Kind};
use serde_json::Value;
use std::{path::Path, sync::atomic::AtomicBool};

const PT_MM: f32 = 25.4 / 72.0;
pub fn field_value(invoice: &Value, item: Option<&Value>, path: &str) -> String {
    let (group, key) = path.split_once('.').unwrap_or(("Invoice", path));
    let target = if group.eq_ignore_ascii_case("item") {
        item.unwrap_or(&Value::Null)
    } else {
        invoice
    };
    let alias = match (group, key) {
        ("Exporter", "ExporterNameEN") => "exporterNameEN",
        ("Exporter", "ExporterNameCN") => "exporterNameCN",
        ("Exporter", "AddressEN") => "exporterAddressEN",
        ("Customer", "CustomerNameEN") => "customerNameEN",
        ("Customer", "AddressEN") => "customerAddressEN",
        _ => key,
    };
    let value = target.as_object().and_then(|object| {
        object
            .iter()
            .find(|(name, _)| name.eq_ignore_ascii_case(alias))
            .map(|(_, value)| value)
    });
    match value {
        Some(Value::String(value)) => value.clone(),
        Some(Value::Number(value)) => value.to_string(),
        Some(Value::Bool(value)) => if *value { "是" } else { "否" }.into(),
        _ => String::new(),
    }
}
pub(crate) fn escape(text: &str) -> String {
    text.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}
pub(crate) fn wrap(text: &str, width: f32, size: f32) -> Vec<String> {
    let capacity = (width / size).max(1.);
    let mut lines = Vec::new();
    let advance = |character: char| if character.is_ascii() { 0.56 } else { 1. };
    for paragraph in text.split('\n') {
        let mut tokens = Vec::new();
        let mut word = String::new();
        for character in paragraph.chars() {
            if character.is_ascii_alphanumeric() || "'-.%".contains(character) {
                word.push(character);
            } else {
                if !word.is_empty() {
                    tokens.push(std::mem::take(&mut word));
                }
                tokens.push(character.to_string());
            }
        }
        if !word.is_empty() {
            tokens.push(word);
        }
        let mut line = String::new();
        let mut units = 0.;
        for token in tokens {
            let token_width: f32 = token.chars().map(advance).sum();
            if units + token_width > capacity && !line.is_empty() {
                lines.push(line.trim_end().to_owned());
                line.clear();
                units = 0.;
            }
            if token.trim().is_empty() && line.is_empty() {
                continue;
            }
            for character in token.chars() {
                let next = advance(character);
                if units + next > capacity && !line.is_empty() {
                    lines.push(std::mem::take(&mut line));
                    units = 0.;
                }
                line.push(character);
                units += next;
            }
        }
        lines.push(line.trim_end().to_owned());
    }
    lines
}
pub(crate) fn text_svg(
    svg: &mut String,
    lines: &[String],
    x: f32,
    y: f32,
    width: f32,
    size: f32,
    bold: bool,
    color: &str,
    align: &str,
) {
    let (anchor, position) = match align {
        "Right" => ("end", x + width),
        "Center" => ("middle", x + width / 2.),
        _ => ("start", x),
    };
    for (index, line) in lines.iter().enumerate() {
        svg.push_str(&format!("<text x=\"{position}\" y=\"{}\" font-size=\"{size}\" font-weight=\"{}\" text-anchor=\"{anchor}\" fill=\"{}\">{}</text>",y+size+index as f32*size*1.35,if bold{700}else{400},escape(color),escape(line)));
    }
}
fn element(
    svg: &mut String,
    element: &Element,
    data: &crate::ReportData,
    page: usize,
    count: usize,
) -> Result<()> {
    if !element.visible || !element.output_enabled {
        return Ok(());
    }
    let x = element.x_hundredth_mm as f32 / 100.;
    let y = element.y_hundredth_mm as f32 / 100.;
    let width = element.width_hundredth_mm as f32 / 100.;
    let height = element.height_hundredth_mm as f32 / 100.;
    let style = &element.style;
    if element.rotation_deg != 0 {
        svg.push_str(&format!(
            "<g transform=\"rotate({} {} {})\">",
            element.rotation_deg,
            x + width / 2.,
            y + height / 2.
        ));
    }
    let result = (|| {
        let image = match &element.kind {
            Kind::Image {
                source_kind,
                field_path,
                resource_id,
                hide_when_source_empty,
                ..
            } => {
                let key = if source_kind == "Field" {
                    field_path
                } else {
                    resource_id
                };
                match data.images.get(key) {
                    Some(image) => Some(image),
                    None if source_kind == "Field" && *hide_when_source_empty => return Ok(()),
                    None => return Err(invalid("模板引用的图片不存在或未获授权。")),
                }
            }
            Kind::Field { field_path, .. } => data.images.get(field_path),
            _ => None,
        };
        if let Some(image) = image {
            svg.push_str(&format!("<image x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" preserveAspectRatio=\"xMidYMid meet\" href=\"{}\"/>",image.data_url()?));
            return Ok(());
        }
        let border = if style.border_style == "None" {
            0.
        } else {
            style.border_width_px * 25.4 / 96.
        };
        if !matches!(element.kind, Kind::Line { .. } | Kind::Flow { .. }) {
            svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\" stroke=\"{}\" stroke-width=\"{border}\"/>",escape(&style.background_color),escape(&style.border_color)));
        }
        let text = match &element.kind {
            Kind::Line { direction } => {
                let (x2, y2) = if direction == "Vertical" {
                    (x, y + height)
                } else {
                    (x + width, y)
                };
                svg.push_str(&format!("<line x1=\"{x}\" y1=\"{y}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{}\" stroke-width=\"{}\"/>",escape(&style.border_color),border.max(0.2)));
                return Ok(());
            }
            Kind::Rectangle | Kind::Flow { .. } | Kind::Image { .. } => return Ok(()),
            Kind::Text { text } => text.clone(),
            Kind::Field {
                field_path,
                fallback_text,
            } => {
                let value = data.text(field_path);
                if value.is_empty() {
                    fallback_text.clone()
                } else {
                    value
                }
            }
            Kind::PageNumber {
                format,
                prefix,
                suffix,
            } => format!(
                "{prefix}{}{suffix}",
                if format == "CurrentOfTotal" {
                    format!("{} / {count}", page + 1)
                } else {
                    (page + 1).to_string()
                }
            ),
        };
        let size = style.font_size_pt * PT_MM;
        let padding = style.padding_hundredth_mm as f32 / 100.;
        let lines = wrap(&text, (width - padding * 2.).max(size), size);
        if lines.len() as f32 * size * 1.35 > height + size * 0.6 {
            return Err(invalid(format!(
                "报表组件“{}”高度不足，请扩大高度以显示完整内容。",
                element.label
            )));
        }
        let mut content = String::new();
        text_svg(
            &mut content,
            &lines,
            x + padding,
            y,
            width - padding * 2.,
            size,
            style.bold,
            &style.color,
            &style.align,
        );
        content = format!(
            "<g font-family=\"{}\">{content}</g>",
            escape(&style.font_family)
        );
        svg.push_str(&content);
        Ok(())
    })();
    if element.rotation_deg != 0 {
        svg.push_str("</g>");
    }
    result
}

fn fixed_elements(
    svg: &mut String,
    design: &Design,
    data: &crate::ReportData,
    index: usize,
    count: usize,
) -> Result<()> {
    for layer in design.layers.iter().filter(|l| l.visible) {
        if (layer.role == "Header" && index > 0 && !layer.print.repeat_on_every_page)
            || (layer.role == "Footer" && index + 1 < count && !layer.print.repeat_on_every_page)
            || (layer.role == "Body" && index > 0)
        {
            continue;
        }
        let mut elements: Vec<_> = layer.elements.iter().collect();
        elements.sort_by_key(|e| e.z_index);
        for item in elements {
            element(svg, item, data, index, count)?;
        }
    }
    Ok(())
}
pub fn render_design(
    data: &crate::ReportData,
    design: &Design,
    cancelled: &AtomicBool,
) -> Result<crate::Document> {
    if data.report_type != design.report_type {
        return Err(invalid("模板与单据的数据域不一致。"));
    }
    let pages = pages_data(data, design, cancelled)?;
    Ok(crate::Document {
        pages: pages
            .into_iter()
            .map(|svg| crate::Page {
                width_mm: design.page.width_hundredth_mm as f32 / 100.,
                height_mm: design.page.height_hundredth_mm as f32 / 100.,
                svg,
            })
            .collect(),
    })
}

pub fn pages(invoice: &ApiInvoiceDetailDto, design: &Design) -> Result<Vec<String>> {
    pages_data(
        &crate::ReportData::invoice(invoice, serde_json::json!({}), serde_json::json!({}), false)?,
        design,
        &AtomicBool::new(false),
    )
}
fn pages_data(
    data: &crate::ReportData,
    design: &Design,
    cancelled: &AtomicBool,
) -> Result<Vec<String>> {
    let width = design.page.width_hundredth_mm as f32 / 100.;
    let height = design.page.height_hundredth_mm as f32 / 100.;
    if width <= 0. || height <= 0. || width > 500. || height > 1000. {
        return Err(invalid("报表纸张尺寸无效。"));
    }
    let elements: Vec<_> = design
        .layers
        .iter()
        .filter(|layer| layer.visible)
        .flat_map(|layer| &layer.elements)
        .filter(|element| element.visible && element.output_enabled)
        .collect();
    let tables: Vec<_> = elements
        .iter()
        .filter_map(|element| {
            if let Kind::Flow { block, .. } = &element.kind {
                Some((*element, block))
            } else {
                None
            }
        })
        .collect();
    if tables.is_empty() {
        let mut canvas = crate::canvas::Canvas::new(width, height);
        fixed_elements(&mut canvas.svg, design, data, 0, 1)?;
        return Ok(vec![canvas.finish().svg]);
    }
    if tables.len() > 1 {
        return Err(invalid("此原生模板只能包含一个商品明细表。"));
    }
    let (table, block) = tables[0];
    if block.columns.is_empty() {
        return Err(invalid("商品明细表必须包含至少一列。"));
    }
    let footer = design
        .layers
        .iter()
        .filter(|layer| layer.role == "Footer" && layer.visible)
        .flat_map(|layer| &layer.elements)
        .map(|element| element.y_hundredth_mm as f32 / 100.)
        .fold(height - 10., f32::min);
    let top = table.y_hundredth_mm as f32 / 100.;
    let left = table.x_hundredth_mm as f32 / 100.;
    let table_width = table.width_hundredth_mm as f32 / 100.;
    let size = table.style.font_size_pt * PT_MM;
    let total_width: f32 = block.columns.iter().map(|column| column.width_mm).sum();
    if total_width <= 0. {
        return Err(invalid("商品明细列宽无效。"));
    }
    let widths: Vec<_> = block
        .columns
        .iter()
        .map(|column| column.width_mm / total_width * table_width)
        .collect();
    let header_lines: Vec<_> = block
        .columns
        .iter()
        .zip(&widths)
        .map(|(column, width)| wrap(&column.title, width - 3., size))
        .collect();
    let header_height =
        header_lines.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.;
    let capacity = footer - 3. - top - header_height;
    if capacity < 10. {
        return Err(invalid("页眉与页脚之间没有足够的明细空间。"));
    }
    let items = data.items();
    let mut chunks: Vec<Vec<(Vec<Vec<String>>, f32)>> = vec![vec![]];
    let mut used = 0.;
    for item in items {
        if cancelled.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(crate::Error {
                kind: crate::ErrorKind::Cancelled,
                message: "报表输出已取消。".into(),
            });
        }
        let lines: Vec<_> = block
            .columns
            .iter()
            .zip(&widths)
            .map(|(column, width)| {
                wrap(
                    &crate::data::plain(data.value(&column.field_path, Some(item))),
                    width - 3.,
                    size,
                )
            })
            .collect();
        let row_height = lines.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.;
        if row_height > capacity {
            return Err(invalid("单行商品内容超过一页，请调整明细列宽或字体。"));
        }
        if used + row_height > capacity {
            chunks.push(vec![]);
            used = 0.;
        }
        chunks.last_mut().unwrap().push((lines, row_height));
        used += row_height;
        if chunks.len() > 500 {
            return Err(invalid("报表页数超过 500 页限制。"));
        }
    }
    let count = chunks.len();
    let mut pages = vec![];
    for (index, rows) in chunks.iter().enumerate() {
        let mut svg = format!(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" font-family=\"Noto Sans CJK SC\" width=\"{}\" height=\"{}\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"white\"/>",
            width / PT_MM,
            height / PT_MM
        );
        fixed_elements(&mut svg, design, data, index, count)?;
        let mut y = top;
        table_row(
            &mut svg,
            block,
            &header_lines,
            &widths,
            left,
            y,
            header_height,
            size,
            true,
        );
        y += header_height;
        for (lines, row_height) in rows {
            table_row(
                &mut svg,
                block,
                lines,
                &widths,
                left,
                y,
                *row_height,
                size,
                false,
            );
            y += row_height;
        }
        text_svg(
            &mut svg,
            &[format!("{} / {count}", index + 1)],
            width - 25.,
            height - 7.,
            15.,
            2.6,
            false,
            "#526866",
            "Right",
        );
        svg.push_str("</svg>");
        pages.push(svg);
    }
    Ok(pages)
}
fn table_row(
    svg: &mut String,
    block: &DetailTable,
    lines: &[Vec<String>],
    widths: &[f32],
    left: f32,
    y: f32,
    height: f32,
    size: f32,
    header: bool,
) {
    let mut x = left;
    for ((column, lines), width) in block.columns.iter().zip(lines).zip(widths) {
        svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\" stroke=\"#bdcdc8\" stroke-width=\"0.2\"/>",if header{"#eef6f4"}else{"white"}));
        text_svg(
            svg,
            lines,
            x + 1.5,
            y + 1.5,
            width - 3.,
            size,
            header,
            "#173f3b",
            &column.align,
        );
        x += width;
    }
}

pub fn pdf(
    invoice: &ApiInvoiceDetailDto,
    design: &Design,
    font: &Path,
    cancelled: &AtomicBool,
) -> Result<Vec<u8>> {
    let document = crate::Document {
        pages: pages(invoice, design)?
            .into_iter()
            .map(|svg| crate::Page {
                width_mm: design.page.width_hundredth_mm as f32 / 100.,
                height_mm: design.page.height_hundredth_mm as f32 / 100.,
                svg,
            })
            .collect(),
    };
    crate::pdf_document(&document, font, cancelled)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn long_invoice_paginates_without_losing_last_row_or_escaping_text() {
        let mut draft = export_doc_domain::invoice::InvoiceDraft::demo("2026-09-16", "TEST-<>&");
        let row = draft.rows[0].clone();
        draft.rows = vec![row; 75];
        draft.rows[74].cells[2] = "最后一行 END-75".into();
        let output = pages(&draft.build().unwrap(), &Design::invoice()).unwrap();
        assert!(output.len() > 1);
        assert!(output.last().unwrap().contains("END-75"));
        assert!(output[0].contains("TEST-&lt;&gt;&amp;"));
    }
}
