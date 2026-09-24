//! Vector PDF output. Layout, pagination and embedded fonts run inside Rust.
use crate::{
    canvas::Canvas,
    error::{Result, invalid},
};
use export_doc_contracts::generated_api::ApiInvoiceDetailDto;
use export_doc_domain::designer::{Design, Element, Kind, ReportBlock};
use serde_json::Value;
use std::{path::Path, sync::atomic::AtomicBool};

mod bands;
mod detail;
use bands::{
    footer_applies, footer_content_bottom, footer_content_height, footer_top_for, layer_footer_top,
    layer_header_bottom,
};
mod detail_content;
mod detail_mix;
mod flow;
mod free_detail;

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
    let mut lines = Vec::new();
    let capacity = width.max(size);
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
        let mut measured = 0.;
        for token in tokens {
            let token_width = measure("Noto Sans CJK SC", false, &token, size);
            if measured + token_width > capacity && !line.is_empty() {
                lines.push(line.trim_end().to_owned());
                line.clear();
                measured = 0.;
            }
            if token.trim().is_empty() && line.is_empty() {
                continue;
            }
            for character in token.chars() {
                let next = measure("Noto Sans CJK SC", false, &character.to_string(), size);
                if measured + next > capacity && !line.is_empty() {
                    lines.push(std::mem::take(&mut line));
                    measured = 0.;
                }
                line.push(character);
                measured += next;
            }
        }
        lines.push(line.trim_end().to_owned());
    }
    lines
}

pub(crate) fn measured_wrap(
    text: &str,
    width: f32,
    family: &str,
    bold: bool,
    size: f32,
) -> Vec<String> {
    if family.eq_ignore_ascii_case("Noto Sans CJK SC") && !bold {
        return wrap(text, width, size);
    }
    wrap_measured(text, width, family, bold, size)
}

fn wrap_measured(text: &str, width: f32, family: &str, bold: bool, size: f32) -> Vec<String> {
    let capacity = width.max(size);
    let mut lines = Vec::new();
    for paragraph in text.split('\n') {
        let mut line = String::new();
        let mut measured = 0.;
        for character in paragraph.chars() {
            let next = measure(family, bold, &character.to_string(), size);
            let hard_break = measured + next > capacity && !line.is_empty();
            if hard_break {
                lines.push(std::mem::take(&mut line));
                measured = 0.;
            }
            line.push(character);
            measured += next;
        }
        lines.push(line.trim_end().to_owned());
    }
    lines
}

fn measure(family: &str, bold: bool, text: &str, size: f32) -> f32 {
    crate::fonts::text_width_mm(text, family, bold, size).unwrap_or_else(|_| {
        let fallback = |character: char| if character.is_ascii() { 0.56 } else { 1. };
        text.chars().map(fallback).sum::<f32>() * size
    })
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
        let lines = measured_wrap(
            &text,
            (width - padding * 2.).max(size),
            &style.font_family,
            style.bold,
            size,
        );
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
    render_body_flow: bool,
    body_bottom: Option<f32>,
) -> Result<()> {
    for layer in design.layers.iter().filter(|l| l.visible) {
        if (layer.print.first_page_only && index > 0)
            || (layer.role == "Header" && index > 0 && !layer.print.repeat_on_every_page)
            || (layer.role == "Footer" && !footer_applies(layer, index, index + 1 == count))
            || (layer.role == "Body" && index > 0)
        {
            continue;
        }
        let pinned_footer_offset = if layer.role == "Footer" && layer.print.follow_body {
            body_bottom.unwrap_or(0.)
                - (footer_content_bottom(layer) - footer_content_height(layer))
        } else if layer.role == "Footer" && layer.print.pin_to_page_bottom {
            design.page.height_hundredth_mm as f32 / 100. - footer_content_bottom(layer)
        } else {
            0.
        };
        if pinned_footer_offset.abs() > f32::EPSILON {
            svg.push_str(&format!(
                "<g transform=\"translate(0 {pinned_footer_offset})\">"
            ));
        }
        let mut elements: Vec<_> = layer.elements.iter().collect();
        elements.sort_by_key(|e| e.z_index);
        for item in elements {
            if free_detail::is_item(item) {
                continue;
            }
            if !render_body_flow && layer.role == "Body" && matches!(item.kind, Kind::Flow { .. }) {
                continue;
            }
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
        if pinned_footer_offset.abs() > f32::EPSILON {
            svg.push_str("</g>");
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
    if elements.iter().any(|element| free_detail::is_item(element)) {
        return free_detail::render(design, data, cancelled);
    }
    let body_flows: Vec<_> = design
        .layers
        .iter()
        .filter(|layer| layer.visible && layer.role == "Body")
        .flat_map(|layer| &layer.elements)
        .filter(|element| {
            element.visible
                && element.output_enabled
                && matches!(
                    &element.kind,
                    Kind::Flow {
                        block: ReportBlock::Row(_)
                            | ReportBlock::Grid(_)
                            | ReportBlock::Conditional(_)
                            | ReportBlock::PageBreak(_),
                        ..
                    }
                )
        })
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
        if !body_flows.is_empty() {
            return render_body_flow(design, data, &body_flows, width, height, cancelled);
        }
        let mut canvas = Canvas::new(width, height);
        let body_bottom = design
            .layers
            .iter()
            .filter(|l| l.role == "Body" && l.visible)
            .flat_map(|l| &l.elements)
            .filter(|e| e.visible && e.output_enabled)
            .map(|e| (e.y_hundredth_mm + e.height_hundredth_mm) as f32 / 100.)
            .fold(0., f32::max);
        fixed_elements(&mut canvas.svg, design, data, 0, 1, true, Some(body_bottom))?;
        return Ok(vec![canvas.finish().svg]);
    }
    if tables.len() > 1 {
        return Err(invalid("此原生模板只能包含一个商品明细表。"));
    }
    let (table, block) = tables[0];
    if block.columns.is_empty() {
        return Err(invalid("商品明细表必须包含至少一列。"));
    }
    let footer = layer_footer_top(design, height);
    let top = table.y_hundredth_mm as f32 / 100.;
    let left = table.x_hundredth_mm as f32 / 100.;
    let table_width = table.width_hundredth_mm as f32 / 100.;
    let following_flows: Vec<_> = body_flows
        .iter()
        .copied()
        .filter(|element| element.y_hundredth_mm > table.y_hundredth_mm)
        .collect();
    let preceding_flows: Vec<_> = body_flows
        .iter()
        .copied()
        .filter(|element| element.y_hundredth_mm < table.y_hundredth_mm)
        .collect();
    return detail::render(
        detail::DetailLayout {
            table: block,
            data,
            left,
            top,
            continuation_top: if design.layers.iter().any(|layer| {
                layer.role == "Header"
                    && layer.visible
                    && layer.print.repeat_on_every_page
                    && !layer.elements.is_empty()
            }) {
                top
            } else {
                design.page.margin_top_hundredth_mm as f32 / 100.
            },
            bottoms: detail::PageBottoms {
                first: footer_top_for(design, height, 0, false),
                continuation: footer_top_for(design, height, 1, false),
                last: footer_top_for(design, height, 1, true),
                single: footer,
            },
            width: table_width,
            page_width: width,
            height,
            cancelled,
        },
        |svg, index, count, content_bottom| {
            fixed_elements(svg, design, data, index, count, false, Some(content_bottom))?;
            if index == 0 {
                detail_mix::render_preceding(svg, data, &preceding_flows, top)?;
            }
            if index + 1 == count {
                detail_mix::render_following(svg, data, &following_flows, content_bottom, footer)?;
            }
            Ok(())
        },
    );
}

enum FlowStep<'a> {
    Element { element: &'a Element, gap_mm: f32 },
    Break,
}

fn render_body_flow(
    design: &Design,
    data: &crate::ReportData,
    flows: &[&Element],
    width: f32,
    height: f32,
    cancelled: &AtomicBool,
) -> Result<Vec<String>> {
    let header_bottom = layer_header_bottom(design, height);
    let footer_top = layer_footer_top(design, height);
    let capacity = footer_top - header_bottom;
    if capacity <= 0. {
        return Err(invalid("页眉与页脚之间没有足够的 Flow 空间。"));
    }

    let mut ordered = flows.to_vec();
    ordered.sort_by_key(|element| (element.y_hundredth_mm, element.z_index));
    let mut steps = Vec::with_capacity(ordered.len());
    let mut previous_bottom = 0.;
    for element in ordered {
        if matches!(
            &element.kind,
            Kind::Flow {
                block: ReportBlock::PageBreak(_),
                ..
            }
        ) {
            steps.push(FlowStep::Break);
        } else {
            let top = element.y_hundredth_mm as f32 / 100.;
            let gap_mm = if previous_bottom == 0. {
                top
            } else {
                (top - previous_bottom).max(0.)
            };
            steps.push(FlowStep::Element { element, gap_mm });
        }
        previous_bottom = (element.y_hundredth_mm + element.height_hundredth_mm) as f32 / 100.;
    }

    let mut pages: Vec<Vec<(&Element, f32)>> = Vec::new();
    let mut current = Vec::new();
    let mut used = 0.;
    for step in steps {
        if cancelled.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(crate::Error {
                kind: crate::ErrorKind::Cancelled,
                message: "报表输出已取消。".into(),
            });
        }
        match step {
            FlowStep::Break => {
                if !current.is_empty() {
                    pages.push(std::mem::take(&mut current));
                    used = 0.;
                }
            }
            FlowStep::Element { element, gap_mm } => {
                let item_height = element.height_hundredth_mm as f32 / 100.;
                let mut gap_mm = if current.is_empty() { 0. } else { gap_mm };
                if item_height > capacity {
                    return Err(invalid(format!(
                        "报表组件“{}”高度超过当前页可用空间。",
                        element.label
                    )));
                }
                if used + gap_mm + item_height > capacity && !current.is_empty() {
                    pages.push(std::mem::take(&mut current));
                    used = 0.;
                    gap_mm = 0.;
                }
                current.push((element, gap_mm));
                used += gap_mm + item_height;
            }
        }
        if pages.len() > 500 {
            return Err(invalid("报表页数超过 500 页限制。"));
        }
    }
    if !current.is_empty() {
        pages.push(current);
    }
    if pages.is_empty() {
        pages.push(Vec::new());
    }

    let count = pages.len();
    let mut rendered = Vec::with_capacity(count);
    for (index, items) in pages.iter().enumerate() {
        let mut canvas = Canvas::new(width, height);
        let body_bottom = header_bottom
            + items
                .iter()
                .map(|(element, gap)| gap + element.height_hundredth_mm as f32 / 100.)
                .sum::<f32>();
        fixed_elements(
            &mut canvas.svg,
            design,
            data,
            index,
            count,
            false,
            Some(body_bottom),
        )?;
        let mut cursor = 0.;
        for (element, gap_mm) in items {
            cursor += gap_mm;
            let target_y = header_bottom + cursor;
            let offset = target_y - element.y_hundredth_mm as f32 / 100.;
            canvas
                .svg
                .push_str(&format!("<g transform=\"translate(0 {offset})\">"));
            flow::render(&mut canvas.svg, element, data)?;
            canvas.svg.push_str("</g>");
            cursor += element.height_hundredth_mm as f32 / 100.;
        }
        rendered.push(canvas.finish().svg);
    }
    Ok(rendered)
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
                first_page_only: false,
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
