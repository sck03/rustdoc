use crate::designer::{Design, Element, Field, Kind, PROFILE_MARKER, SCHEMA_MARKER};

pub fn escape(text: &str) -> String {
    text.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&#39;")
        .replace('{', "&#123;")
        .replace('}', "&#125;")
}
fn mm(value: i32) -> String {
    format!("{:.2}", value as f64 / 100.)
}
fn safe_color(value: &str) -> bool {
    value.len() == 7
        && value.starts_with('#')
        && value[1..]
            .bytes()
            .all(|character| character.is_ascii_hexdigit())
}

pub fn validate(design: &Design, fields: &[Field]) -> Result<(), String> {
    if design.version != 3
        || design.ast_kind != "ReportDocument"
        || design.coordinate_unit != "hundredth-mm"
        || design.contract_version != "3.0"
        || !["ExportDocument", "PaymentVoucher"].contains(&design.report_type.as_str())
    {
        return Err("不受支持的 V3 合同。".into());
    }
    let page = &design.page;
    if page.size != "A4"
        || !matches!(
            (page.width_hundredth_mm, page.height_hundredth_mm),
            (21000, 29700) | (29700, 21000)
        )
    {
        return Err("验证版只支持 A4 纸张。".into());
    }
    let mut detail_count = 0;
    let mut ids = std::collections::BTreeSet::new();
    for layer in &design.layers {
        if !matches!(
            layer.role.as_str(),
            "Header" | "Body" | "Footer" | "Overlay"
        ) {
            return Err("此图层不受验证版支持。".into());
        }
        for element in &layer.elements {
            if !ids.insert(&element.id) {
                return Err("组件 ID 重复。".into());
            }
            if element.x_hundredth_mm < 0
                || element.y_hundredth_mm < 0
                || element.width_hundredth_mm < 100
                || element.height_hundredth_mm < 100
                || element.x_hundredth_mm + element.width_hundredth_mm > page.width_hundredth_mm
                || element.y_hundredth_mm + element.height_hundredth_mm > page.height_hundredth_mm
            {
                return Err(format!("{} 超出纸张范围。", element.label));
            }
            let style = &element.style;
            if !(5. ..=48.).contains(&style.font_size_pt)
                || !(0. ..=8.).contains(&style.border_width_px)
                || ![&style.color, &style.background_color, &style.border_color]
                    .iter()
                    .all(|color| safe_color(color))
                || !matches!(style.align.as_str(), "Left" | "Center" | "Right")
                || !matches!(style.border_style.as_str(), "Solid" | "Dashed" | "None")
                || !["Noto Sans CJK SC", "Noto Serif CJK SC"].contains(&style.font_family.as_str())
                || !(-360..=360).contains(&element.rotation_deg)
            {
                return Err("组件样式含不受支持的值。".into());
            }
            match &element.kind {
                Kind::Field { field_path, .. } => {
                    expression(field_path, fields)?;
                    if field_path.starts_with("item.") {
                        return Err("商品字段只能放在明细表中。".into());
                    }
                }
                Kind::Text { text } if text.chars().count() > 32768 => {
                    return Err("文字组件超出长度上限。".into());
                }
                Kind::Flow { flow_kind, block } => {
                    if layer.role != "Body"
                        || flow_kind != "DetailTable"
                        || block.r#type != "DetailTable"
                        || block.source_path != "Invoice.Items"
                        || design.report_type != "ExportDocument"
                    {
                        return Err("验证版只支持 Invoice.Items 明细表。".into());
                    }
                    if layer.visible && element.visible && element.output_enabled {
                        detail_count += 1;
                    }
                    if block.columns.is_empty() || block.columns.len() > 20 {
                        return Err("明细表应包含 1–20 列。".into());
                    }
                    for column in &block.columns {
                        expression(&column.field_path, fields)?;
                        if !column.field_path.starts_with("item.")
                            || !(5. ..=190.).contains(&column.width_mm)
                            || !matches!(column.align.as_str(), "Left" | "Center" | "Right")
                            || column.title.chars().count() > 200
                        {
                            return Err("明细列设置无效。".into());
                        }
                    }
                }
                Kind::Image {
                    source_kind,
                    purpose,
                    field_path,
                    resource_id,
                    ..
                } => {
                    if !["Image", "Stamp"].contains(&purpose.as_str()) {
                        return Err("图片用途无效。".into());
                    }
                    match source_kind.as_str() {
                        "Resource" if design.resources.iter().any(|r| r.id == *resource_id) => {}
                        "Field"
                            if ["doc_seal_path", "customs_seal_path"]
                                .contains(&field_path.as_str())
                                && design.report_type == "ExportDocument" => {}
                        _ => return Err("图片必须绑定已登记资源或出口商印章。".into()),
                    }
                }
                Kind::PageNumber {
                    format,
                    prefix,
                    suffix,
                } => {
                    if !["Current", "CurrentOfTotal"].contains(&format.as_str())
                        || prefix.len() + suffix.len() > 1000
                    {
                        return Err("页码格式无效。".into());
                    }
                }
                _ => {}
            }
        }
    }
    if detail_count > 1 {
        return Err("当前模板只能包含一个可见的商品明细表。".into());
    }
    if detail_count == 0 {
        return Ok(());
    }
    let detail = design
        .layers
        .iter()
        .flat_map(|layer| &layer.elements)
        .find(|element| matches!(element.kind, Kind::Flow { .. }))
        .unwrap();
    let header_bottom = design
        .layers
        .iter()
        .filter(|layer| layer.role == "Header" && layer.visible)
        .flat_map(|layer| &layer.elements)
        .filter(|element| element.visible && element.output_enabled)
        .map(|element| element.y_hundredth_mm + element.height_hundredth_mm)
        .max()
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
    if header_bottom > detail.y_hundredth_mm || detail.y_hundredth_mm + 2000 > footer_top {
        return Err("页眉、明细与页脚互相重叠，请调整位置后输出。".into());
    }
    Ok(())
}

fn expression(path: &str, fields: &[Field]) -> Result<String, String> {
    let field = fields
        .iter()
        .find(|field| field.path == path)
        .ok_or_else(|| format!("字段 {path} 不在后端字段目录中。"))?;
    // Shipping marks may project to a controlled image in the existing engine.
    // This native profile is text-only; the caller checks the invoice's marks
    // mode before using a text binding. Other image bindings are never listed.
    if matches!(path, "doc_seal_path" | "customs_seal_path") {
        return Err("图片和印章组件留待下一阶段验证。".into());
    }
    Ok(field.expression.clone())
}

pub fn export(design: &Design, fields: &[Field]) -> Result<String, String> {
    validate(design, fields)?;
    let page = &design.page;
    if !design
        .layers
        .iter()
        .flat_map(|l| &l.elements)
        .any(|e| matches!(e.kind, Kind::Flow { .. }))
    {
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
    let detail = design
        .layers
        .iter()
        .flat_map(|layer| &layer.elements)
        .find(|element| matches!(element.kind, Kind::Flow { .. }))
        .unwrap();
    let top = detail.y_hundredth_mm;
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
        detail_width = mm(detail.width_hundredth_mm),
        left = mm(detail.x_hundredth_mm),
        font = detail.style.font_size_pt
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
    let Kind::Flow { block, .. } = &detail.kind else {
        unreachable!()
    };
    let total_width: f32 = block.columns.iter().map(|column| column.width_mm).sum();
    html.push_str("<table class=\"native-flow\"><thead><tr><td></td></tr></thead><tbody><tr><td><table class=\"native-detail\"><colgroup>");
    for column in &block.columns {
        html.push_str(&format!(
            "<col style=\"width:{:.4}%\">",
            100. * column.width_mm / total_width
        ));
    }
    html.push_str("</colgroup><thead><tr>");
    for column in &block.columns {
        html.push_str(&format!(
            "<th style=\"text-align:{}\">{}</th>",
            column.align.to_ascii_lowercase(),
            escape(&column.title)
        ));
    }
    html.push_str("</tr></thead><tbody>{{ for item in Invoice.Items }}<tr>");
    for column in &block.columns {
        html.push_str(&format!(
            "<td style=\"text-align:{}\">{}</td>",
            column.align.to_ascii_lowercase(),
            expression(&column.field_path, fields)?
        ));
    }
    html.push_str("</tr>{{ end }}</tbody></table></td></tr></tbody><tfoot><tr><td></td></tr></tfoot></table></body></html>");
    Ok(html)
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

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn static_text_cannot_become_html_or_scriban_code() {
        assert_eq!(
            escape("<b>{{ x }}</b>"),
            "&lt;b&gt;&#123;&#123; x &#125;&#125;&lt;/b&gt;"
        );
    }
    #[test]
    fn foreign_templates_are_rejected_without_silent_conversion() {
        assert!(Design::from_html("<html>Original</html>").is_err());
    }
}
