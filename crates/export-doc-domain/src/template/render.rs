use super::*;
use crate::designer::{Element, PROFILE_MARKER, SCHEMA_MARKER};

pub(super) fn export(design: &Design, fields: &[Field]) -> Result<String, String> {
    let page = &design.page;
    let flow_elements: Vec<_> = design
        .layers
        .iter()
        .filter(|layer| layer.visible && layer.role == "Body")
        .flat_map(|layer| &layer.elements)
        .filter(|element| element.visible && element.output_enabled)
        .filter_map(|element| match &element.kind {
            Kind::Flow { block, .. } => Some((element, block)),
            _ => None,
        })
        .collect();
    if flow_elements.is_empty() {
        let schema = serde_json::to_string(design)
            .map_err(|e| e.to_string())?
            .replace("--", "-\\u002d")
            .replace('<', "\\u003c");
        let mut html = format!(
            "<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{{size:{}mm {}mm;margin:0}}body{{margin:0}}.native-element{{position:absolute;white-space:pre-wrap;box-sizing:border-box;line-height:1.3}}</style></head><body>{SCHEMA_MARKER}{schema}-->",
            mm(page.width_hundredth_mm),
            mm(page.height_hundredth_mm)
        );
        for layer in design.layers.iter().filter(|l| l.visible) {
            for element in layer
                .elements
                .iter()
                .filter(|e| e.visible && e.output_enabled)
            {
                html.push_str(&render_element(element, fields)?);
            }
        }
        html.push_str("</body></html>");
        return Ok(html);
    }
    let top = flow_elements
        .iter()
        .map(|(element, _)| element.y_hundredth_mm)
        .min()
        .unwrap_or(0);
    let footer_top = design
        .layers
        .iter()
        .filter(|layer| layer.role == "Footer" && layer.visible)
        .flat_map(|layer| &layer.elements)
        .filter(|element| element.visible && element.output_enabled)
        .map(|element| element.y_hundredth_mm)
        .min()
        .unwrap_or(page.height_hundredth_mm - 500);
    let bottom = page.height_hundredth_mm - footer_top;
    let schema = serde_json::to_string(design)
        .map_err(|error| error.to_string())?
        .replace("--", "-\\u002d")
        .replace('<', "\\u003c");
    let mut html = format!(
        r#"<!doctype html><html><head><meta charset="utf-8"><style>
@page {{ size: {width}mm {height}mm; margin: 0; }}
html,body {{ margin:0; padding:0; color:#173f3b; font-family:'Noto Sans CJK SC',sans-serif; font-size:10pt; -webkit-print-color-adjust:exact; print-color-adjust:exact; }}
* {{ box-sizing:border-box; }}
.native-band {{ position:fixed; left:0; top:0; width:{width}mm; height:{height}mm; pointer-events:none; }}
.native-flow {{ width:{width}mm; border-collapse:collapse; table-layout:fixed; }}
.native-flow > thead {{ display:table-header-group; break-inside:avoid; page-break-inside:avoid; }}
.native-flow > tfoot {{ display:table-footer-group; break-inside:avoid; page-break-inside:avoid; }}
.native-flow > thead > tr > td {{ height:{top}mm; padding:0; border:0; }}
.native-flow > tfoot > tr > td {{ height:{bottom}mm; padding:0; border:0; }}
.native-flow > tbody > tr > td {{ padding:0; border:0; vertical-align:top; }}
.native-element {{ position:absolute; white-space:pre-wrap; overflow-wrap:anywhere; line-height:1.3; }}
.native-detail {{ width:{detail_width}mm; margin-left:{left}mm; border-collapse:collapse; table-layout:fixed; font-size:{font}pt; }}
.native-detail thead {{ display:table-header-group; }}
.native-detail tr {{ break-inside:avoid; page-break-inside:avoid; }}
.native-detail th,.native-detail td {{ border:0.2mm solid #c5d3cf; padding:2mm; vertical-align:top; overflow-wrap:anywhere; white-space:pre-wrap; }}
.native-detail th {{ background:#eef6f5; font-weight:bold; }}
</style></head><body>{PROFILE_MARKER}
{SCHEMA_MARKER}
{schema}
-->
"#,
        width = mm(page.width_hundredth_mm),
        height = mm(page.height_hundredth_mm),
        top = mm(top),
        bottom = mm(bottom),
        detail_width = "auto",
        left = "0",
        font = page.font_size_pt
    );
    for layer in design
        .layers
        .iter()
        .filter(|layer| layer.visible && layer.role != "Body")
    {
        html.push_str("<div class=\"native-band\">");
        for element in layer
            .elements
            .iter()
            .filter(|element| element.visible && element.output_enabled)
        {
            html.push_str(&render_element(element, fields)?);
        }
        html.push_str("</div>");
    }
    html.push_str("<table class=\"native-flow\"><thead><tr><td></td></tr></thead><tbody><tr><td><div class=\"native-flow-items\">");
    for (element, block) in flow_elements {
        let style = format!(
            "margin-left:{}mm;width:{}mm;min-height:{}mm;",
            mm(element.x_hundredth_mm),
            mm(element.width_hundredth_mm),
            mm(element.height_hundredth_mm)
        );
        html.push_str(&format!(
            "<section class=\"native-flow-item native-flow-{}\" style=\"{style}\">{}</section>",
            block.kind().to_ascii_lowercase(),
            render_block(block, fields)?
        ));
    }
    html.push_str(
        "</div></td></tr></tbody><tfoot><tr><td></td></tr></tfoot></table></body></html>",
    );
    Ok(html)
}

fn render_block(block: &ReportBlock, fields: &[Field]) -> Result<String, String> {
    match block {
        ReportBlock::Row(block) => render_row(&block.columns, fields),
        ReportBlock::Grid(block) => render_grid(
            &block.title,
            &block.columns,
            &block.rows,
            &block.default_cell_style,
            &block.border,
            fields,
        ),
        ReportBlock::Conditional(block) => render_conditional(
            &block.condition,
            &block.content,
            &block.style,
            block.border.as_ref(),
            fields,
        ),
        ReportBlock::DetailTable(table) => render_detail_table(table, fields),
        ReportBlock::PageBreak(_) => Ok("<div class=\"report-page-break-row\"></div>".into()),
    }
}

fn render_row(columns: &[crate::designer::RowColumn], fields: &[Field]) -> Result<String, String> {
    let total: f32 = columns
        .iter()
        .map(|column| column.width_percent.max(1.))
        .sum();
    let mut html = String::from("<table class=\"edm-report-row\"><tbody><tr>");
    for column in columns {
        let content = match column.content_kind.as_str() {
            "Field" => format!(
                "{}{}",
                if column.label.is_empty() {
                    String::new()
                } else {
                    format!("{}: ", escape(&column.label))
                },
                expression(&column.field_path, fields)?
            ),
            _ => escape(&column.text),
        };
        html.push_str(&format!(
            "<td style=\"width:{:.4}%;{}\">{content}</td>",
            100. * column.width_percent.max(1.) / total,
            render_text_style(&column.style)
        ));
    }
    html.push_str("</tr></tbody></table>");
    Ok(html)
}

fn render_grid(
    title: &str,
    columns: &[crate::designer::GridColumn],
    rows: &[GridRow],
    default_style: &ReportTextStyle,
    border: &ReportBorderStyle,
    fields: &[Field],
) -> Result<String, String> {
    let total: f32 = columns
        .iter()
        .map(|column| column.width_percent.max(1.))
        .sum();
    let mut html = String::from("<table class=\"edm-report-grid\">");
    if !title.is_empty() {
        html.push_str(&format!(
            "<caption class=\"edm-report-grid-title\">{}</caption>",
            escape(title)
        ));
    }
    html.push_str("<colgroup>");
    for column in columns {
        html.push_str(&format!(
            "<col style=\"width:{:.4}%\">",
            100. * column.width_percent.max(1.) / total
        ));
    }
    html.push_str("</colgroup><tbody>");
    for row in rows {
        html.push_str("<tr>");
        for cell in &row.cells {
            let content = match cell.content_kind.as_str() {
                "Field" => format!(
                    "{}{}",
                    if cell.label.is_empty() {
                        String::new()
                    } else {
                        format!("{}: ", escape(&cell.label))
                    },
                    expression(&cell.field_path, fields)?
                ),
                "CheckboxGroup" => {
                    let value = expression(&cell.field_path, fields)?;
                    cell.checkbox_options
                        .iter()
                        .map(|option| {
                            format!(
                                "<span class=\"edm-report-grid-checkbox\">{{{{ if {value} == \"{}\" }}}}☑{{{{ else }}}}☐{{{{ end }}}} {}</span>",
                                escape(&option.value.replace('"', "\\\"")),
                                escape(&option.label)
                            )
                        })
                        .collect()
                }
                _ => escape(&cell.text),
            };
            html.push_str(&format!(
                "<td colspan=\"{}\" rowspan=\"{}\" style=\"{}{}\">{content}</td>",
                cell.col_span,
                cell.row_span,
                render_text_style(default_style),
                if cell.style.font_size_pt.is_some() || cell.style.bold.is_some() {
                    render_text_style(&cell.style)
                } else {
                    String::new()
                }
            ));
        }
        html.push_str("</tr>");
    }
    html.push_str("</tbody></table>");
    let _ = border;
    Ok(html)
}

fn render_conditional(
    condition: &ConditionalRule,
    content: &ConditionalContent,
    style: &ReportTextStyle,
    border: Option<&ReportBorderStyle>,
    fields: &[Field],
) -> Result<String, String> {
    let field = expression(&condition.field_path, fields)?;
    let condition_expression = match condition.operator.as_str() {
        "Equals" => format!(
            "{field} == \"{}\"",
            escape(&condition.value.replace('"', "\\\""))
        ),
        "NotEquals" => format!(
            "{field} != \"{}\"",
            escape(&condition.value.replace('"', "\\\""))
        ),
        _ => field,
    };
    let content = match content.kind.as_str() {
        "Field" => format!(
            "{}{}",
            if content.label.is_empty() {
                String::new()
            } else {
                format!("{}: ", escape(&content.label))
            },
            expression(&content.field_path, fields)?
        ),
        _ => escape(&content.text),
    };
    Ok(format!(
        "{{{{ if {condition_expression} }}}}<div class=\"edm-conditional-block\" style=\"{}\">{content}</div>{{{{ end }}}}",
        render_text_style(style)
    ))
    .map(|html| {
        let _ = border;
        html
    })
}

fn render_detail_table(table: &DetailTable, fields: &[Field]) -> Result<String, String> {
    let total_width: f32 = table.columns.iter().map(|column| column.width_mm).sum();
    let mut table_html = String::from("<table class=\"native-detail\"><colgroup>");
    for column in &table.columns {
        table_html.push_str(&format!(
            "<col style=\"width:{:.4}%\">",
            100. * column.width_mm / total_width
        ));
    }
    table_html.push_str("</colgroup><thead><tr>");
    for column in &table.columns {
        table_html.push_str(&format!(
            "<th style=\"text-align:{}\">{}</th>",
            column.align.to_ascii_lowercase(),
            escape(&column.title)
        ));
    }
    table_html.push_str("</tr></thead><tbody>");
    if let Some(grouping) = &table.grouping {
        table_html.push_str(&format!(
            "{{{{ group = item.{} }}}}{{{{ if group != previous_group }}}}<tr class=\"native-detail-group\"><td colspan=\"{}\">{}{{{{ if show_group_value }}}} {{{{ group }}}}{{{{ end }}}}</td></tr>{{{{ end }}}}{{{{ previous_group = group }}}}",
            escape(&detail_field(&grouping.field_path)),
            table.columns.len(),
            escape(&grouping.label)
        ));
    }
    table_html.push_str("{{ for item in Invoice.Items }}<tr>");
    for column in &table.columns {
        let content = if column.content_kind == "Composite" && !column.content.is_empty() {
            column
                .content
                .iter()
                .map(|part| match part.kind.as_str() {
                    "Text" => escape(&part.text),
                    "LineBreak" => "<br>".into(),
                    _ => format!("{{{{ {} }}}}", escape(&detail_field(&part.field_path))),
                })
                .collect::<String>()
        } else {
            format!("{{{{ {} }}}}", escape(&detail_field(&column.field_path)))
        };
        table_html.push_str(&format!(
            "<td style=\"text-align:{}\">{}</td>",
            column.align.to_ascii_lowercase(),
            content
        ));
    }
    table_html.push_str("</tr>{{ end }}");
    if let Some(grouping) = &table.grouping {
        if let Some(footer) = &grouping.footer {
            table_html.push_str(&format!(
                "<tr class=\"native-detail-group-footer\"><td colspan=\"{}\" style=\"text-align:right\">{}</td>",
                footer.label_column_span.clamp(1, table.columns.len() as i32),
                escape(&footer.label)
            ));
            for column in table.columns.iter().skip(
                footer
                    .label_column_span
                    .clamp(1, table.columns.len() as i32) as usize,
            ) {
                let content = footer
                    .cells
                    .iter()
                    .find(|cell| cell.column_id == column.id)
                    .map(|cell| footer_cell_html(cell))
                    .unwrap_or_default();
                table_html.push_str(&format!(
                    "<td style=\"text-align:{}\">{content}</td>",
                    column.align.to_ascii_lowercase()
                ));
            }
            table_html.push_str("</tr>");
        }
    }
    if let Some(summary) = &table.summary_row {
        table_html.push_str(&format!(
            "<tr class=\"native-detail-summary\"><td colspan=\"{}\" style=\"text-align:right\">{}</td>",
            summary.label_column_span.clamp(1, table.columns.len() as i32),
            escape(&summary.label)
        ));
        for column in table.columns.iter().skip(
            summary
                .label_column_span
                .clamp(1, table.columns.len() as i32) as usize,
        ) {
            let content = summary
                .cells
                .iter()
                .find(|cell| cell.column_id == column.id)
                .map(|cell| summary_cell_html(cell))
                .unwrap_or_default();
            table_html.push_str(&format!(
                "<td style=\"text-align:{}\">{content}</td>",
                column.align.to_ascii_lowercase()
            ));
        }
        table_html.push_str("</tr>");
    }
    table_html.push_str("</tbody></table>");
    if let Some(side) = &table.side_band {
        let content = if side.content_kind == "Field" {
            format!("{{{{ {} }}}}", escape(&detail_field(&side.field_path)))
        } else {
            escape(&side.text)
        };
        return Ok(format!(
            "<table class=\"native-detail-layout\"><colgroup><col style=\"width:{:.4}%\"><col style=\"width:{:.4}%\"></colgroup><thead><tr><th>{}</th><th>{}</th></tr></thead><tbody><tr><td>{content}</td><td style=\"padding:0\">{table_html}</td></tr></tbody></table>",
            100. * side.width_mm / (side.width_mm + table.detail_width_mm.unwrap_or(total_width)),
            100. * table.detail_width_mm.unwrap_or(total_width)
                / (side.width_mm + table.detail_width_mm.unwrap_or(total_width)),
            escape(&side.title),
            escape(if table.title.is_empty() {
                "Detail"
            } else {
                &table.title
            })
        ));
    }
    let _ = fields;
    Ok(table_html)
}

fn detail_field(path: &str) -> String {
    path.strip_prefix("item.")
        .or_else(|| path.strip_prefix("Invoice.Items."))
        .unwrap_or(path)
        .to_owned()
}

fn footer_cell_html(cell: &crate::designer::DetailGroupFooterCell) -> String {
    match cell.content_kind.as_str() {
        "Text" => escape(&cell.text),
        "Count" => "{{ group_count }}".into(),
        "Sum" => format!(
            "{{{{ sum_{} }}}}",
            escape(&cell.column_id.replace('_', "-"))
        ),
        _ => String::new(),
    }
}

fn summary_cell_html(cell: &crate::designer::DetailSummaryCell) -> String {
    match cell.content_kind.as_str() {
        "Text" => escape(&cell.text),
        "Field" => format!("{{{{ {} }}}}", escape(&detail_field(&cell.field_path))),
        _ => String::new(),
    }
}

fn render_text_style(style: &ReportTextStyle) -> String {
    let mut css = String::new();
    if let Some(size) = style.font_size_pt {
        css.push_str(&format!("font-size:{size}pt;"));
    }
    if style.bold == Some(true) {
        css.push_str("font-weight:700;");
    }
    if let Some(align) = &style.align {
        css.push_str(&format!("text-align:{};", align.to_ascii_lowercase()));
    }
    if let Some(value) = style.margin_top_mm {
        css.push_str(&format!("padding-top:{value}mm;"));
    }
    if let Some(value) = style.margin_right_mm {
        css.push_str(&format!("padding-right:{value}mm;"));
    }
    if let Some(value) = style.margin_bottom_mm {
        css.push_str(&format!("padding-bottom:{value}mm;"));
    }
    if let Some(value) = style.margin_left_mm {
        css.push_str(&format!("padding-left:{value}mm;"));
    }
    css
}

fn render_element(element: &Element, fields: &[Field]) -> Result<String, String> {
    let style = &element.style;
    let mut css = format!(
        "left:{}mm;top:{}mm;width:{}mm;height:{}mm;font-size:{}pt;font-weight:{};color:{};background:{};text-align:{};padding:{}mm;border:{}px {} {};",
        mm(element.x_hundredth_mm),
        mm(element.y_hundredth_mm),
        mm(element.width_hundredth_mm),
        mm(element.height_hundredth_mm),
        style.font_size_pt,
        if style.bold { "bold" } else { "normal" },
        style.color,
        style.background_color,
        style.align.to_ascii_lowercase(),
        mm(style.padding_hundredth_mm),
        style.border_width_px,
        style.border_style.to_ascii_lowercase(),
        style.border_color
    );
    let content = match &element.kind {
        Kind::Text { text } => escape(text),
        Kind::Field { field_path, .. } => expression(field_path, fields)?,
        Kind::Rectangle => String::new(),
        Kind::Image {
            source_kind,
            field_path,
            resource_id,
            alt_text,
            ..
        } => {
            let source = if source_kind == "Field" {
                format!("{{{{ {field_path} }}}}")
            } else {
                format!("edm-resource:{resource_id}")
            };
            format!(
                "<img src=\"{source}\" alt=\"{}\" style=\"width:100%;height:100%;object-fit:contain\">",
                escape(alt_text)
            )
        }
        Kind::PageNumber { prefix, suffix, .. } => format!(
            "{}<span class=\"page-number\"></span>{}",
            escape(prefix),
            escape(suffix)
        ),
        Kind::Line { direction } => {
            let side = if direction == "Vertical" {
                "left"
            } else {
                "top"
            };
            css.push_str(&format!(
                "border:0;border-{side}:1px solid {};",
                style.color
            ));
            String::new()
        }
        Kind::Flow { .. } => return Err("明细表不能出现在固定图层。".into()),
    };
    Ok(format!(
        "<div class=\"native-element\" style=\"{css}\">{content}</div>"
    ))
}
