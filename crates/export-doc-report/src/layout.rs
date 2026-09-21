//! Vector PDF output. Layout, pagination and embedded fonts run inside Rust.
use crate::error::{Result, invalid};
use export_doc_contracts::generated_api::ApiInvoiceDetailDto;
use export_doc_domain::designer::{Design, Element, Kind, ReportBlock};
use serde_json::Value;
use std::{path::Path, sync::atomic::AtomicBool};

mod detail;
mod flow;

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
    // The approved palette has no Serif Bold face. Select the real bundled
    // Sans Bold face instead of synthesizing weight or substituting regular.
    let family = if bold {
        " font-family=\"Noto Sans CJK SC\""
    } else {
        ""
    };
    for (index, line) in lines.iter().enumerate() {
        svg.push_str(&format!("<text x=\"{position}\" y=\"{}\" font-size=\"{size}\" font-weight=\"{}\"{family} text-anchor=\"{anchor}\" fill=\"{}\">{}</text>",y+size+index as f32*size*1.35,if bold{700}else{400},escape(color),escape(line)));
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
        let dash = if style.border_style == "Dashed" {
            " stroke-dasharray=\"1.1 0.7\""
        } else {
            ""
        };
        if !matches!(element.kind, Kind::Line { .. } | Kind::Flow { .. }) {
            svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\" stroke=\"{}\" stroke-width=\"{border}\"{dash}/>",escape(&style.background_color),escape(&style.border_color)));
        }
        let text = match &element.kind {
            Kind::Line { direction } => {
                let (x2, y2) = if direction == "Vertical" {
                    (x, y + height)
                } else {
                    (x + width, y)
                };
                svg.push_str(&format!("<line x1=\"{x}\" y1=\"{y}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{}\" stroke-width=\"{}\"{dash}/>",escape(&style.border_color),border.max(0.2)));
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
            if let Kind::Flow {
                block: ReportBlock::Row(_) | ReportBlock::Grid(_) | ReportBlock::Conditional(_),
                ..
            } = &item.kind
            {
                flow::render(svg, item, data)?;
            } else if matches!(item.kind, Kind::Flow { .. }) {
                continue;
            } else {
                element(svg, item, data, index, count)?;
            }
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
            if let Kind::Flow {
                block: ReportBlock::DetailTable(table),
                ..
            } = &element.kind
                && table.output.as_ref().is_none_or(|output| output.enabled)
            {
                Some((*element, table))
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
    return detail::render(
        detail::DetailLayout {
            table: block,
            data,
            left,
            top,
            footer_top: footer,
            width: table_width,
            page_width: width,
            height,
            cancelled,
        },
        |svg, index, count| fixed_elements(svg, design, data, index, count),
    );
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
    use export_doc_domain::designer::{
        DetailGroupFooter, DetailGroupFooterCell, DetailGrouping, DetailSideBand,
        DetailSummaryCell, DetailSummaryRow, ReportTextStyle,
    };
    use serde_json::json;

    fn structured_design() -> Design {
        let mut design = Design::invoice();
        if let Kind::Flow {
            block: ReportBlock::DetailTable(table),
            ..
        } = &mut design.layers[1].elements[0].kind
        {
            table.grouping = Some(DetailGrouping {
                field_path: "item.UnitEN".into(),
                label: "GROUP".into(),
                show_field_value: true,
                keep_together: true,
                page_break_before: false,
                footer: Some(DetailGroupFooter {
                    label: "SUBTOTAL".into(),
                    label_column_span: 2,
                    cells: vec![
                        DetailGroupFooterCell {
                            column_id: "col-2".into(),
                            content_kind: "Sum".into(),
                            text: String::new(),
                            field_path: "item.Quantity".into(),
                        },
                        DetailGroupFooterCell {
                            column_id: "col-5".into(),
                            content_kind: "Sum".into(),
                            text: String::new(),
                            field_path: "item.TotalPrice".into(),
                        },
                    ],
                    style: ReportTextStyle::default(),
                }),
                style: ReportTextStyle::default(),
            });
            table.summary_row = Some(DetailSummaryRow {
                label: "GRAND TOTAL".into(),
                label_column_span: 2,
                cells: vec![DetailSummaryCell {
                    column_id: "col-2".into(),
                    content_kind: "Text".into(),
                    text: "TOTAL QTY".into(),
                    field_path: String::new(),
                }],
                style: ReportTextStyle::default(),
            });
            table.side_band = Some(DetailSideBand {
                title: "MARK".into(),
                width_mm: 35.,
                content_kind: "Text".into(),
                text: "SIDE-BAND-VALUE".into(),
                field_path: String::new(),
                style: ReportTextStyle::default(),
            });
        }
        design
    }

    fn structured_data(rows: usize) -> crate::ReportData {
        let mut draft = export_doc_domain::invoice::InvoiceDraft::demo("2026-09-16", "STRUCT-001");
        let row = draft.rows[0].clone();
        draft.rows = (0..rows)
            .map(|index| {
                let mut row = row.clone();
                row.cells[1] = format!("STYLE-{}", index + 1);
                row
            })
            .collect();
        crate::ReportData::invoice(&draft.build().unwrap(), json!({}), json!({}), false).unwrap()
    }

    #[test]
    fn structured_detail_renders_groups_subtotals_summary_and_side_band() {
        let pages = pages_data(
            &structured_data(3),
            &structured_design(),
            &AtomicBool::new(false),
        )
        .unwrap();
        let svg = pages.join("");
        assert!(svg.contains("GROUP"));
        assert!(svg.contains("SUBTOTAL"));
        assert!(svg.contains("GRAND TOTAL"));
        assert!(svg.contains("SIDE-BAND-VALUE"));
        assert!(svg.contains("STYLE-3"));
    }

    #[test]
    fn structured_detail_repeats_header_and_keeps_last_group_on_later_pages() {
        let pages = pages_data(
            &structured_data(75),
            &structured_design(),
            &AtomicBool::new(false),
        )
        .unwrap();
        assert!(pages.len() > 1);
        assert!(pages[0].contains("品名 / DESCRIPTION"));
        assert!(pages[1].contains("品名 / DESCRIPTION"));
        assert!(pages.last().unwrap().contains("STYLE-75"));
        assert!(pages.last().unwrap().contains("GRAND TOTAL"));
    }

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

    #[test]
    fn dashed_styles_and_explicit_page_numbers_are_not_silently_replaced_or_appended() {
        let mut design = Design::invoice();
        let title = design.element_mut("title").unwrap();
        title.style.border_width_px = 1.;
        title.style.border_style = "Dashed".into();
        let output = pages(
            &export_doc_domain::invoice::InvoiceDraft::demo("2026-09-16", "DASH-001")
                .build()
                .unwrap(),
            &design,
        )
        .unwrap();
        assert!(output[0].contains("stroke-dasharray"));
        assert!(!output[0].contains(" / 1</text>"));

        let mut explicit = design.clone();
        explicit.element_mut("title").unwrap().kind =
            export_doc_domain::designer::Kind::PageNumber {
                format: "CurrentOfTotal".into(),
                prefix: "P".into(),
                suffix: String::new(),
            };
        let output = pages(
            &export_doc_domain::invoice::InvoiceDraft::demo("2026-09-16", "PAGE-001")
                .build()
                .unwrap(),
            &explicit,
        )
        .unwrap();
        assert!(output[0].contains("P1 / 1"));
    }
}
