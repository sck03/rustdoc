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
                    diagonal_header: None,
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
fn grid_diagonal_header_round_trips_through_html_export() {
    let mut design = Design::invoice();
    design.layers[1].elements.clear();
    append_flow(
        &mut design,
        "grid-diagonal",
        "Grid",
        ReportBlock::Grid(crate::designer::ReportGridBlock {
            id: "grid-diagonal".into(),
            output: None,
            title: String::new(),
            columns: vec![GridColumn {
                id: "grid-col-1".into(),
                width_percent: 100.,
            }],
            rows: vec![GridRow {
                id: "grid-row-1".into(),
                height_mm: Some(18.),
                cells: vec![GridCell {
                    id: "grid-cell-1".into(),
                    col_span: 1,
                    row_span: 1,
                    content_kind: "Text".into(),
                    text: String::new(),
                    label: String::new(),
                    field_path: String::new(),
                    fallback_text: String::new(),
                    checkbox_options: vec![],
                    vertical_text: false,
                    diagonal_header: Some(crate::designer::GridDiagonalHeader {
                        upper_left_text: "项目".into(),
                        lower_right_text: "金额".into(),
                    }),
                    style: ReportTextStyle::default(),
                    border: None,
                }],
            }],
            margin_top_mm: None,
            margin_bottom_mm: None,
            border: ReportBorderStyle::default(),
            default_cell_style: ReportTextStyle::default(),
        }),
        6000,
    );
    let html = export(&design, &fields()).unwrap();
    let parsed = Design::from_html(&html).unwrap();
    let Kind::Flow {
        block: ReportBlock::Grid(grid),
        ..
    } = &parsed.layers[1].elements[0].kind
    else {
        panic!("grid flow should round trip");
    };
    assert_eq!(
        grid.rows[0].cells[0]
            .diagonal_header
            .as_ref()
            .unwrap()
            .upper_left_text,
        "项目"
    );
    assert!(html.contains("edm-report-grid-diagonal"));
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

#[test]
fn template_fonts_are_restricted_to_the_bundled_families() {
    for family in ["Noto Sans CJK SC", "Noto Serif CJK SC", "Arial", "SimSun"] {
        let mut design = Design::invoice();
        design.page.font_family = family.into();
        let supported = matches!(family, "Noto Sans CJK SC" | "Noto Serif CJK SC");
        assert_eq!(validate(&design, &fields()).is_ok(), supported);
    }
}
