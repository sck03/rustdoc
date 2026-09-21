use crate::designer::{
    ConditionalContent, ConditionalRule, Design, DetailColumn, DetailTable, Field, GridRow, Kind,
    ReportBlock, ReportBorderStyle, ReportTextStyle, RowColumn,
};

mod render;

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
    let mut block_ids = std::collections::BTreeSet::new();
    for layer in &design.layers {
        if !matches!(
            layer.role.as_str(),
            "Header" | "Body" | "Footer" | "Overlay"
        ) {
            return Err("此图层不受验证版支持。".into());
        }
        for element in &layer.elements {
            if !ids.insert(element.id.clone()) {
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
                    validate_flow(
                        design,
                        layer,
                        flow_kind,
                        block,
                        fields,
                        &mut block_ids,
                        &mut detail_count,
                    )?;
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
        .find(|element| {
            matches!(
                element.kind,
                Kind::Flow {
                    block: ReportBlock::DetailTable(_),
                    ..
                }
            )
        })
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

fn validate_flow(
    design: &Design,
    layer: &crate::designer::Layer,
    flow_kind: &str,
    block: &ReportBlock,
    fields: &[Field],
    ids: &mut std::collections::BTreeSet<String>,
    detail_count: &mut usize,
) -> Result<(), String> {
    const FLOW_KINDS: &[&str] = &["Row", "Grid", "Conditional", "DetailTable", "PageBreak"];
    if !FLOW_KINDS.contains(&flow_kind) || flow_kind != block.kind() {
        return Err("V3 流组件类型与 block 类型不一致。".into());
    }
    if layer.role != "Body" && matches!(flow_kind, "DetailTable" | "PageBreak") {
        return Err("明细表和分页符只能放在主体图层。".into());
    }
    if design.report_type == "PaymentVoucher" && flow_kind == "DetailTable" {
        return Err("付款/报销模板不能使用出口单据明细表。".into());
    }
    if !ids.insert(block.id().to_owned()) {
        return Err("V3 结构组件 ID 重复。".into());
    }
    match block {
        ReportBlock::Row(block) => {
            let columns = &block.columns;
            if columns.is_empty() || columns.len() > 100 {
                return Err("V3 多列行应包含 1–100 列。".into());
            }
            for column in columns {
                validate_row_column(column, fields)?;
                if !ids.insert(column.id.clone()) {
                    return Err("V3 结构组件 ID 重复。".into());
                }
            }
        }
        ReportBlock::Grid(block) => {
            let columns = &block.columns;
            let rows = &block.rows;
            let default_cell_style = &block.default_cell_style;
            let border = &block.border;
            if columns.is_empty() || columns.len() > 100 || rows.is_empty() || rows.len() > 1000 {
                return Err("V3 普通表格应包含 1–100 列和 1–1000 行。".into());
            }
            validate_text_style(default_cell_style)?;
            validate_border(border)?;
            for column in columns {
                if column.width_percent <= 0.
                    || !column.width_percent.is_finite()
                    || !ids.insert(column.id.clone())
                {
                    return Err("V3 普通表格列设置无效。".into());
                }
            }
            for row in rows {
                validate_grid_row(row, fields, ids)?;
            }
        }
        ReportBlock::Conditional(block) => {
            let condition = &block.condition;
            let content = &block.content;
            let style = &block.style;
            let border = &block.border;
            validate_condition(condition, fields)?;
            validate_conditional_content(content, fields)?;
            validate_text_style(style)?;
            if let Some(border) = border {
                validate_border(border)?;
            }
        }
        ReportBlock::DetailTable(table) => {
            if layer.visible && table.output.as_ref().is_none_or(|output| output.enabled) {
                *detail_count += 1;
            }
            validate_detail_table(design, table, fields)?;
            for column in &table.columns {
                if !ids.insert(column.id.clone()) {
                    return Err("V3 结构组件 ID 重复。".into());
                }
            }
        }
        ReportBlock::PageBreak(_) => {}
    }
    Ok(())
}

fn validate_row_column(column: &RowColumn, fields: &[Field]) -> Result<(), String> {
    if !(0. ..=100.).contains(&column.width_percent) || !column.width_percent.is_finite() {
        return Err("V3 多列行列宽无效。".into());
    }
    validate_text_style(&column.style)?;
    if let Some(border) = &column.border {
        validate_border(border)?;
    }
    match column.content_kind.as_str() {
        "Text" if column.text.chars().count() <= 32768 => Ok(()),
        "Field" => {
            expression(&column.field_path, fields)?;
            if column.label.chars().count() > 200 || column.fallback_text.chars().count() > 2048 {
                Err("V3 多列行字段设置无效。".into())
            } else {
                Ok(())
            }
        }
        _ => Err("V3 多列行内容类型无效。".into()),
    }
}

fn validate_grid_row(
    row: &GridRow,
    fields: &[Field],
    ids: &mut std::collections::BTreeSet<String>,
) -> Result<(), String> {
    if row
        .height_mm
        .is_some_and(|height| !height.is_finite() || !(0. ..=500.).contains(&height))
        || row.cells.is_empty()
        || row.cells.len() > 100
        || !ids.insert(row.id.clone())
    {
        return Err("V3 普通表格行设置无效。".into());
    }
    for cell in &row.cells {
        if cell.col_span < 1
            || cell.row_span < 1
            || !ids.insert(cell.id.clone())
            || cell.label.chars().count() > 200
            || cell.fallback_text.chars().count() > 2048
        {
            return Err("V3 普通表格单元格设置无效。".into());
        }
        validate_text_style(&cell.style)?;
        if let Some(border) = &cell.border {
            validate_border(border)?;
        }
        match cell.content_kind.as_str() {
            "Text" if cell.text.chars().count() <= 32768 => {}
            "Field" => {
                expression(&cell.field_path, fields)?;
            }
            "CheckboxGroup" => {
                expression(&cell.field_path, fields)?;
                if cell.checkbox_options.len() > 100 {
                    return Err("V3 普通表格勾选项超过上限。".into());
                }
            }
            _ => return Err("V3 普通表格单元格内容类型无效。".into()),
        }
    }
    Ok(())
}

fn validate_condition(condition: &ConditionalRule, fields: &[Field]) -> Result<(), String> {
    expression(&condition.field_path, fields)?;
    if !["HasValue", "Equals", "NotEquals"].contains(&condition.operator.as_str())
        || condition.value.chars().count() > 2048
    {
        return Err("V3 条件设置无效。".into());
    }
    Ok(())
}

fn validate_conditional_content(
    content: &ConditionalContent,
    fields: &[Field],
) -> Result<(), String> {
    match content.kind.as_str() {
        "Text" if content.text.chars().count() <= 32768 => Ok(()),
        "Field" => {
            expression(&content.field_path, fields)?;
            if content.label.chars().count() > 200 || content.fallback_text.chars().count() > 2048 {
                Err("V3 条件字段设置无效。".into())
            } else {
                Ok(())
            }
        }
        _ => Err("V3 条件内容类型无效。".into()),
    }
}

fn validate_detail_table(
    design: &Design,
    table: &DetailTable,
    fields: &[Field],
) -> Result<(), String> {
    if table.r#type != "DetailTable"
        || table.source_path != "Invoice.Items"
        || table.repeat_mode != "ScribanFor"
        || design.report_type != "ExportDocument"
    {
        return Err("V3 明细表结构无效。".into());
    }
    if table.columns.is_empty() || table.columns.len() > 100 {
        return Err("明细表应包含 1–100 列。".into());
    }
    for column in &table.columns {
        validate_detail_column(column, fields)?;
    }
    if table
        .detail_width_mm
        .is_some_and(|width| !width.is_finite() || !(40. ..=240.).contains(&width))
    {
        return Err("V3 明细表宽度无效。".into());
    }
    validate_text_style(&table.header_style)?;
    validate_text_style(&table.body_style)?;
    validate_border(&table.border)?;
    if let Some(grouping) = &table.grouping {
        expression(&grouping.field_path, fields)?;
        if grouping.label.chars().count() > 200 {
            return Err("V3 分组标题过长。".into());
        }
        validate_text_style(&grouping.style)?;
        if let Some(footer) = &grouping.footer {
            if footer.label_column_span < 1
                || footer.label_column_span as usize > table.columns.len()
                || footer.label.chars().count() > 200
            {
                return Err("V3 分组小计设置无效。".into());
            }
            validate_text_style(&footer.style)?;
            for cell in &footer.cells {
                if !table
                    .columns
                    .iter()
                    .any(|column| column.id == cell.column_id)
                    || !["Empty", "Text", "Sum", "Count"].contains(&cell.content_kind.as_str())
                {
                    return Err("V3 分组小计单元格无效。".into());
                }
                if cell.content_kind == "Sum" {
                    expression(&cell.field_path, fields)?;
                }
            }
        }
    }
    if let Some(summary) = &table.summary_row {
        if summary.label_column_span < 1
            || summary.label_column_span as usize > table.columns.len()
            || summary.label.chars().count() > 200
        {
            return Err("V3 合计行设置无效。".into());
        }
        validate_text_style(&summary.style)?;
        for cell in &summary.cells {
            if !table
                .columns
                .iter()
                .any(|column| column.id == cell.column_id)
                || !["Empty", "Text", "Field"].contains(&cell.content_kind.as_str())
            {
                return Err("V3 合计单元格无效。".into());
            }
            if cell.content_kind == "Field" {
                expression(&cell.field_path, fields)?;
            }
        }
    }
    if let Some(side) = &table.side_band {
        if side.title.chars().count() > 200
            || !side.width_mm.is_finite()
            || !(16. ..=120.).contains(&side.width_mm)
            || !["Text", "Field"].contains(&side.content_kind.as_str())
        {
            return Err("V3 明细侧栏无效。".into());
        }
        if side.content_kind == "Field" {
            expression(&side.field_path, fields)?;
        }
        validate_text_style(&side.style)?;
    }
    Ok(())
}

fn validate_detail_column(column: &DetailColumn, fields: &[Field]) -> Result<(), String> {
    expression(&column.field_path, fields)?;
    if !column.field_path.starts_with("item.")
        || !(5. ..=190.).contains(&column.width_mm)
        || !column.width_mm.is_finite()
        || !matches!(column.align.as_str(), "Left" | "Center" | "Right")
        || column.title.chars().count() > 200
        || column.header_group_title.chars().count() > 200
        || !(1..=20).contains(&column.header_group_span)
        || !["Field", "Composite"].contains(&column.content_kind.as_str())
    {
        return Err("明细列设置无效。".into());
    }
    if let Some(border) = &column.border {
        validate_border(border)?;
    }
    for part in &column.content {
        if !["Text", "Field", "LineBreak"].contains(&part.kind.as_str()) {
            return Err("明细单元格组合内容无效。".into());
        }
        if part.kind == "Field" {
            expression(&part.field_path, fields)?;
        }
    }
    Ok(())
}

fn validate_text_style(style: &ReportTextStyle) -> Result<(), String> {
    for value in [
        style.font_size_pt,
        style.margin_top_mm,
        style.margin_right_mm,
        style.margin_bottom_mm,
        style.margin_left_mm,
    ]
    .into_iter()
    .flatten()
    {
        if !value.is_finite() {
            return Err("V3 文本样式包含无效数值。".into());
        }
    }
    if style
        .font_size_pt
        .is_some_and(|size| !(5. ..=48.).contains(&size))
        || style
            .align
            .as_ref()
            .is_some_and(|align| !matches!(align.as_str(), "Left" | "Center" | "Right"))
    {
        return Err("V3 文本样式无效。".into());
    }
    Ok(())
}

fn validate_border(border: &ReportBorderStyle) -> Result<(), String> {
    if !border.width_px.is_finite()
        || !(0. ..=8.).contains(&border.width_px)
        || !safe_color(&border.color)
        || !matches!(border.style.as_str(), "Solid" | "Dashed" | "None")
    {
        return Err("V3 边框样式无效。".into());
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
    render::export(design, fields)
}
#[cfg(test)]
mod tests {
    use super::*;
    use crate::designer::{Element, GridCell, GridColumn, GridRow, ReportBlock, RowColumn};
    use serde_json::json;

    fn fields() -> Vec<Field> {
        [
            ("Invoice.InvoiceNo", "{{ Invoice.InvoiceNo }}"),
            ("Invoice.Spare1", "{{ Invoice.Spare1 }}"),
            ("Invoice.ShippingMarks", "{{ Invoice.ShippingMarks }}"),
            ("Invoice.InvoiceDate", "{{ Invoice.InvoiceDate }}"),
            ("Invoice.TotalAmount", "{{ Invoice.TotalAmount }}"),
            ("Invoice.PaymentTerms", "{{ Invoice.PaymentTerms }}"),
            ("Customer.CustomerNameEN", "{{ Customer.CustomerNameEN }}"),
            ("Customer.AddressEN", "{{ Customer.AddressEN }}"),
            ("Exporter.ExporterNameEN", "{{ Exporter.ExporterNameEN }}"),
            ("Exporter.AddressEN", "{{ Exporter.AddressEN }}"),
            ("item.StyleNo", "{{ item.StyleNo }}"),
            ("item.StyleName", "{{ item.StyleName }}"),
            ("item.Quantity", "{{ item.Quantity }}"),
            ("item.UnitEN", "{{ item.UnitEN }}"),
            ("item.UnitPrice", "{{ item.UnitPrice }}"),
            ("item.TotalPrice", "{{ item.TotalPrice }}"),
        ]
        .into_iter()
        .map(|(path, expression)| Field {
            path: path.into(),
            label: path.into(),
            category: "test".into(),
            expression: expression.into(),
        })
        .collect()
    }

    fn append_flow(design: &mut Design, id: &str, kind: &str, block: ReportBlock, y: i32) {
        design.layers[1].elements.push(Element {
            id: id.into(),
            label: kind.into(),
            x_hundredth_mm: 1000,
            y_hundredth_mm: y,
            width_hundredth_mm: 19000,
            height_hundredth_mm: 1800,
            rotation_deg: 0,
            z_index: 0,
            visible: true,
            locked: false,
            output_enabled: true,
            style: crate::designer::Style::default(),
            kind: Kind::Flow {
                flow_kind: kind.into(),
                block,
            },
        });
    }

    #[test]
    fn original_v3_flow_blocks_round_trip_and_export() {
        let mut design = Design::invoice();
        design.layers[1].elements.clear();
        append_flow(
            &mut design,
            "row-1",
            "Row",
            ReportBlock::Row(crate::designer::ReportRowBlock {
                id: "row-1".into(),
                output: None,
                columns: vec![RowColumn {
                    id: "row-col-1".into(),
                    content_kind: "Field".into(),
                    text: String::new(),
                    label: "发票号".into(),
                    field_path: "Invoice.InvoiceNo".into(),
                    fallback_text: String::new(),
                    width_percent: 100.,
                    style: ReportTextStyle::default(),
                    border: None,
                }],
                margin_top_mm: None,
                margin_bottom_mm: None,
            }),
            6000,
        );
        append_flow(
            &mut design,
            "grid-1",
            "Grid",
            ReportBlock::Grid(crate::designer::ReportGridBlock {
                id: "grid-1".into(),
                output: None,
                title: "固定字段".into(),
                columns: vec![GridColumn {
                    id: "grid-col-1".into(),
                    width_percent: 100.,
                }],
                rows: vec![GridRow {
                    id: "grid-row-1".into(),
                    height_mm: Some(10.),
                    cells: vec![GridCell {
                        id: "grid-cell-1".into(),
                        col_span: 1,
                        row_span: 1,
                        content_kind: "Field".into(),
                        text: String::new(),
                        label: String::new(),
                        field_path: "Invoice.Spare1".into(),
                        fallback_text: String::new(),
                        checkbox_options: vec![],
                        vertical_text: false,
                        style: ReportTextStyle::default(),
                        border: None,
                    }],
                }],
                margin_top_mm: None,
                margin_bottom_mm: None,
                border: ReportBorderStyle::default(),
                default_cell_style: ReportTextStyle::default(),
            }),
            8000,
        );
        append_flow(
            &mut design,
            "conditional-1",
            "Conditional",
            ReportBlock::Conditional(crate::designer::ReportConditionalBlock {
                id: "conditional-1".into(),
                output: None,
                condition: ConditionalRule {
                    field_path: "Invoice.Spare1".into(),
                    operator: "HasValue".into(),
                    value: String::new(),
                },
                content: ConditionalContent {
                    kind: "Field".into(),
                    text: String::new(),
                    label: String::new(),
                    field_path: "Invoice.InvoiceNo".into(),
                    fallback_text: String::new(),
                },
                style: ReportTextStyle::default(),
                border: None,
            }),
            10000,
        );
        append_flow(
            &mut design,
            "page-break-1",
            "PageBreak",
            ReportBlock::PageBreak(crate::designer::ReportPageBreakBlock {
                id: "page-break-1".into(),
                output: None,
            }),
            12000,
        );
        let html = export(&design, &fields()).unwrap();
        let parsed = Design::from_html(&html).unwrap();
        assert_eq!(parsed.layers[1].elements.len(), 4);
        assert!(html.contains("edm-report-row"));
        assert!(html.contains("edm-report-grid"));
        assert!(html.contains("edm-conditional-block"));
        assert!(html.contains("report-page-break-row"));
        let serialized = serde_json::to_value(&parsed).unwrap();
        assert_eq!(
            serialized["layers"][1]["elements"][0]["flowKind"],
            json!("Row")
        );
        assert_eq!(
            serialized["layers"][1]["elements"][1]["flowKind"],
            json!("Grid")
        );
        assert_eq!(
            serialized["layers"][1]["elements"][3]["flowKind"],
            json!("PageBreak")
        );
    }

    #[test]
    fn structured_detail_features_are_not_rejected_as_unrendered() {
        let mut design = Design::invoice();
        if let Kind::Flow {
            block: ReportBlock::DetailTable(table),
            ..
        } = &mut design.layers[1].elements[0].kind
        {
            table.grouping = Some(crate::designer::DetailGrouping {
                field_path: "item.StyleNo".into(),
                label: "分组".into(),
                show_field_value: true,
                keep_together: true,
                page_break_before: false,
                footer: None,
                style: ReportTextStyle::default(),
            });
        }
        let html = export(&design, &fields()).unwrap();
        assert!(html.contains("分组"));
    }

    #[test]
    fn structured_detail_html_contains_groups_subtotals_summary_and_side_band() {
        let mut design = Design::invoice();
        if let Kind::Flow {
            block: ReportBlock::DetailTable(table),
            ..
        } = &mut design.layers[1].elements[0].kind
        {
            table.grouping = Some(crate::designer::DetailGrouping {
                field_path: "item.UnitEN".into(),
                label: "GROUP".into(),
                show_field_value: true,
                keep_together: true,
                page_break_before: false,
                footer: Some(crate::designer::DetailGroupFooter {
                    label: "SUBTOTAL".into(),
                    label_column_span: 2,
                    cells: vec![crate::designer::DetailGroupFooterCell {
                        column_id: "col-2".into(),
                        content_kind: "Sum".into(),
                        text: String::new(),
                        field_path: "item.Quantity".into(),
                    }],
                    style: ReportTextStyle::default(),
                }),
                style: ReportTextStyle::default(),
            });
            table.summary_row = Some(crate::designer::DetailSummaryRow {
                label: "GRAND TOTAL".into(),
                label_column_span: 2,
                cells: vec![crate::designer::DetailSummaryCell {
                    column_id: "col-2".into(),
                    content_kind: "Text".into(),
                    text: "TOTAL QTY".into(),
                    field_path: String::new(),
                }],
                style: ReportTextStyle::default(),
            });
            table.side_band = Some(crate::designer::DetailSideBand {
                title: "MARK".into(),
                width_mm: 35.,
                content_kind: "Text".into(),
                text: "SIDE-BAND-VALUE".into(),
                field_path: String::new(),
                style: ReportTextStyle::default(),
            });
        }
        let html = export(&design, &fields()).unwrap();
        assert!(html.contains("GROUP"));
        assert!(html.contains("SUBTOTAL"));
        assert!(html.contains("GRAND TOTAL"));
        assert!(html.contains("SIDE-BAND-VALUE"));
        assert!(html.contains("native-detail-layout"));
    }

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
