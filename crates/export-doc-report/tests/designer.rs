use export_doc_domain::{
    designer::{Design, Kind, ReportBlock},
    invoice::InvoiceDraft,
};
use export_doc_report::{ReportData, render_design};
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn data() -> ReportData {
    ReportData::invoice(
        &InvoiceDraft::demo("2026-09-21", "GRID-INVOICE")
            .build()
            .unwrap(),
        json!({}),
        json!({}),
        false,
    )
    .unwrap()
}

fn grid() -> Design {
    let mut design = Design::invoice();
    let mut element = design.layers[0].elements[0].clone();
    for layer in &mut design.layers {
        layer.elements.clear();
    }
    element.x_hundredth_mm = 1000;
    element.y_hundredth_mm = 1000;
    element.width_hundredth_mm = 10000;
    element.height_hundredth_mm = 4000;
    element.style.color = "#112233".into();
    element.style.font_family = "Noto Serif CJK SC".into();
    element.kind = Kind::Flow {
        flow_kind: "Grid".into(),
        block: serde_json::from_value(json!({
            "id":"grid", "type":"Grid", "columns":[{"id":"c1","widthPercent":20},{"id":"c2","widthPercent":20},{"id":"c3","widthPercent":20},{"id":"c4","widthPercent":40}],
            "defaultCellStyle":{"fontSizePt":12,"bold":true,"align":"Center","marginLeftMm":0,"marginTopMm":0},
            "border":{"color":"#ff0000","widthPx":1,"style":"Dashed","top":true,"right":false,"bottom":true,"left":false},
            "rows":[
                {"id":"r1","heightMm":14,"cells":[{"id":"a","contentKind":"Text","text":"SPAN","colSpan":2,"rowSpan":2},{"id":"b","contentKind":"Field","fieldPath":"Invoice.InvoiceNo","label":"No","style":{"fontSizePt":8}},{"id":"diag","contentKind":"Text","text":"","diagonalHeader":{"upperLeftText":"项目","lowerRightText":"金额"}}]},
                {"id":"r2","heightMm":15,"cells":[{"id":"c","contentKind":"Text","text":"中文","verticalText":true}]}
            ]
        })).unwrap(),
    };
    design.layers[1].elements.push(element);
    design
}

fn body_flow_design(with_break: bool) -> Design {
    let mut design = Design::invoice();
    for layer in &mut design.layers {
        layer.elements.clear();
    }
    let mut element = Design::invoice().layers[0].elements[0].clone();
    element.id = "flow-block".into();
    element.label = "Flow block".into();
    element.x_hundredth_mm = 1000;
    element.y_hundredth_mm = 2000;
    element.width_hundredth_mm = 19000;
    element.height_hundredth_mm = 10000;
    element.kind = Kind::Flow {
        flow_kind: "Grid".into(),
        block: serde_json::from_value(json!({
            "id":"body-grid", "type":"Grid",
            "columns":[{"id":"c1","widthPercent":100}],
            "rows":[{"id":"r1","heightMm":10,"cells":[
                {"id":"cell-1","contentKind":"Field","fieldPath":"Invoice.InvoiceNo"}
            ]}]
        }))
        .unwrap(),
    };
    design.layers[1].elements.push(element);
    design.layers[0].elements.clear();
    design.layers[2].elements.clear();
    if with_break {
        let mut page_break = Design::invoice().layers[0].elements[0].clone();
        page_break.id = "page-break".into();
        page_break.label = "Page break".into();
        page_break.x_hundredth_mm = 1000;
        page_break.y_hundredth_mm = 20000;
        page_break.width_hundredth_mm = 19000;
        page_break.height_hundredth_mm = 400;
        page_break.kind = Kind::Flow {
            flow_kind: "PageBreak".into(),
            block: serde_json::from_value(json!({"id":"page-break","type":"PageBreak"})).unwrap(),
        };
        design.layers[1].elements.push(page_break);
        let mut after = design.layers[1].elements[0].clone();
        after.id = "after-break".into();
        after.label = "After break".into();
        after.y_hundredth_mm = 21000;
        if let Kind::Flow {
            block: ReportBlock::Grid(block),
            ..
        } = &mut after.kind
        {
            block.id = "after-grid".into();
            block.rows[0].cells[0].id = "after-cell".into();
            block.rows[0].cells[0].text = "AFTER BREAK".into();
            block.rows[0].cells[0].content_kind = "Text".into();
            block.rows[0].cells[0].field_path.clear();
        }
        design.layers[1].elements.push(after);
    }
    design
}

#[test]
fn grid_merge_uses_real_column_occupancy_styles_and_vertical_text() {
    let result = render_design(&data(), &grid(), &AtomicBool::new(false)).unwrap();
    let svg = &result.pages[0].svg;
    assert!(
        svg.contains("x1=\"10\" y1=\"39\" x2=\"50\" y2=\"39\""),
        "merged cell must cover 40 mm x 28 mm"
    );
    assert!(
        svg.contains("x1=\"50\" y1=\"24\" x2=\"70\" y2=\"24\""),
        "second-row cell must skip the rowspan"
    );
    assert!(svg.contains("stroke=\"#ff0000\""));
    assert!(svg.contains("stroke-dasharray=\"2 1\""));
    assert!(svg.contains("font-weight=\"700\" font-family=\"Noto Sans CJK SC\""));
    assert!(svg.contains("fill=\"#112233\""));
    assert!(svg.contains("No: GRID-IN"));
    assert!(svg.contains(">VOICE</text>"));
    assert!(svg.contains(">中</text>"));
    assert!(svg.contains(">文</text>"));
    assert!(svg.contains("x1=\"70\" y1=\"24\" x2=\"110\" y2=\"10\""));
    assert!(svg.contains(">项目</text>"));
    assert!(svg.contains(">金额</text>"));
}

#[test]
fn grid_content_that_does_not_fit_fails_instead_of_overlapping_neighbors() {
    let mut design = grid();
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut design.layers[1].elements[0].kind
    {
        block.rows[0].cells[1].style.font_size_pt = Some(24.);
        block.rows[0].height_mm = Some(4.);
        block.rows[0].cells[1].content_kind = "Text".into();
        block.rows[0].cells[1].text = "THIS CONTENT CANNOT FIT".into();
        block.rows[0].cells[1].field_path.clear();
    }
    assert!(render_design(&data(), &design, &AtomicBool::new(false)).is_err());
}

#[test]
fn hidden_or_disabled_structured_blocks_do_not_output() {
    for mode in 0..3 {
        let mut design = grid();
        let element = &mut design.layers[1].elements[0];
        match mode {
            0 => element.visible = false,
            1 => element.output_enabled = false,
            _ => {
                if let Kind::Flow {
                    block: ReportBlock::Grid(block),
                    ..
                } = &mut element.kind
                {
                    block.output = Some(serde_json::from_value(json!({"enabled":false})).unwrap());
                }
            }
        }
        let result = render_design(&data(), &design, &AtomicBool::new(false)).unwrap();
        assert!(!result.pages[0].svg.contains("SPAN"));
    }
}

#[test]
fn invalid_merged_cells_fail_instead_of_drawing_over_other_columns() {
    let mut design = grid();
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut design.layers[1].elements[0].kind
    {
        block.rows[0].cells[0].col_span = 4;
    }
    assert!(render_design(&data(), &design, &AtomicBool::new(false)).is_err());
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut design.layers[1].elements[0].kind
    {
        block.rows[0].cells.truncate(1);
        block.rows[0].cells[0].col_span = 4;
        block.rows[1].cells.clear();
    }
    // A row fully covered by a rowspan has no standalone cells in HTML/V3.
    export_doc_domain::template::validate(&design, &[]).unwrap();
    assert!(render_design(&data(), &design, &AtomicBool::new(false)).is_ok());
}

#[test]
fn detail_width_does_not_change_a4_page_viewbox() {
    let result = render_design(&data(), &Design::invoice(), &AtomicBool::new(false)).unwrap();
    assert_eq!(result.pages[0].width_mm, 210.);
    assert!(result.pages[0].svg.contains("viewBox=\"0 0 210 297\""));
}

#[test]
fn body_page_break_starts_a_new_physical_page() {
    let result = render_design(&data(), &body_flow_design(true), &AtomicBool::new(false)).unwrap();
    assert_eq!(result.pages.len(), 2);
    assert!(result.pages[0].svg.contains("GRID-INVOICE"));
    assert!(!result.pages[0].svg.contains("AFTER BREAK"));
    assert!(result.pages[1].svg.contains("AFTER BREAK"));
    assert!(!result.pages[1].svg.contains("GRID-INVOICE"));
}

#[test]
fn body_flow_wraps_before_the_footer_when_the_next_block_does_not_fit() {
    let mut design = body_flow_design(false);
    let mut second = design.layers[1].elements[0].clone();
    second.id = "second-flow".into();
    second.label = "Second flow".into();
    second.y_hundredth_mm = 26500;
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut second.kind
    {
        block.id = "second-grid".into();
        block.rows[0].cells[0].id = "second-cell".into();
        block.rows[0].cells[0].text = "SECOND FLOW".into();
        block.rows[0].cells[0].content_kind = "Text".into();
        block.rows[0].cells[0].field_path.clear();
    }
    design.layers[1].elements.push(second);

    let result = render_design(&data(), &design, &AtomicBool::new(false)).unwrap();
    assert_eq!(result.pages.len(), 2);
    assert!(result.pages[0].svg.contains("GRID-INVOICE"));
    assert!(!result.pages[0].svg.contains("SECOND FLOW"));
    assert!(result.pages[1].svg.contains("SECOND FLOW"));
}

#[test]
fn detail_row_that_only_fits_a_continuation_page_never_overlaps_the_footer() {
    let mut design = Design::invoice();
    design.layers[0].print.repeat_on_every_page = false;
    let element = &mut design.layers[1].elements[0];
    element.y_hundredth_mm = 22000;
    element.height_hundredth_mm = 3000;
    if let Kind::Flow {
        block: ReportBlock::DetailTable(table),
        ..
    } = &mut element.kind
    {
        table.columns.truncate(1);
        table.columns[0].content_kind = "Composite".into();
        table.columns[0].content = serde_json::from_value(json!([
            {"id":"long-cell","kind":"Text","text":"LONG ROW\n".repeat(25)}
        ]))
        .unwrap();
    }
    let result = render_design(&data(), &design, &AtomicBool::new(false)).unwrap();
    assert!(!result.pages[0].svg.contains("LONG ROW"));
    assert!(result.pages[1].svg.contains("LONG ROW"));
}

#[test]
fn detail_header_and_body_styles_control_measurement_and_output() {
    let mut design = Design::invoice();
    if let Kind::Flow {
        block: ReportBlock::DetailTable(table),
        ..
    } = &mut design.layers[1].elements[0].kind
    {
        table.header_style.font_size_pt = Some(18.);
        table.header_style.bold = Some(false);
        table.body_style.font_size_pt = Some(8.);
        table.body_style.bold = Some(true);
        table.body_style.margin_left_mm = Some(4.);
        table.border.color = "#aa1122".into();
    }
    let result = render_design(&data(), &design, &AtomicBool::new(false)).unwrap();
    assert!(
        result.pages[0]
            .svg
            .contains("font-size=\"6.35\" font-weight=\"400\"")
    );
    assert!(result.pages[0].svg.contains("font-weight=\"700\""));
    assert!(result.pages[0].svg.contains("stroke=\"#aa1122\""));
}

fn detail_with_surrounding_flows() -> Design {
    let mut design = Design::invoice();
    let mut before = design.layers[0].elements[0].clone();
    before.id = "before-detail".into();
    before.label = "Before detail".into();
    before.x_hundredth_mm = 1000;
    before.y_hundredth_mm = 2000;
    before.width_hundredth_mm = 19000;
    before.height_hundredth_mm = 800;
    before.kind = Kind::Flow {
        flow_kind: "Grid".into(),
        block: serde_json::from_value(json!({
            "id":"before-grid", "type":"Grid",
            "columns":[{"id":"before-col","widthPercent":100}],
            "rows":[{"id":"before-row","heightMm":8,"cells":[
                {"id":"before-cell","contentKind":"Text","text":"BEFORE DETAIL"}
            ]}]
        }))
        .unwrap(),
    };
    let mut after = before.clone();
    after.id = "after-detail".into();
    after.label = "After detail".into();
    after.y_hundredth_mm = 12000;
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut after.kind
    {
        block.id = "after-grid".into();
        block.rows[0].cells[0].id = "after-cell".into();
        block.rows[0].cells[0].text = "AFTER DETAIL".into();
    }
    design.layers[1].elements.insert(0, before);
    design.layers[1].elements.push(after);
    design
}

#[test]
fn detail_table_allows_ordinary_flows_before_and_after_it() {
    let result = render_design(
        &data(),
        &detail_with_surrounding_flows(),
        &AtomicBool::new(false),
    )
    .unwrap();
    let svg = &result.pages[0].svg;
    let y = |needle: &str| {
        svg.split("<text ")
            .find_map(|part| part.contains(needle).then_some(part))
            .and_then(|part| part.split(" y=\"").nth(1))
            .and_then(|part| part.split('"').next())
            .and_then(|value| value.parse::<f32>().ok())
            .unwrap_or_default()
    };
    assert!(y("BEFORE DETAIL") < y("品名"));
    assert!(y("品名") < y("AFTER DETAIL"));
}

#[test]
fn detail_table_rejects_overlapping_following_flow() {
    let mut design = detail_with_surrounding_flows();
    design.layers[1].elements.last_mut().unwrap().y_hundredth_mm = 7001;
    assert!(render_design(&data(), &design, &AtomicBool::new(false)).is_err());
}

#[test]
fn detail_row_limits_force_structured_page_breaks_without_losing_rows() {
    let mut design = Design::invoice();
    if let Kind::Flow {
        block: ReportBlock::DetailTable(table),
        ..
    } = &mut design.layers[1].elements[0].kind
    {
        table.print.first_page_rows = Some(2);
        table.print.continuation_page_rows = Some(2);
    }
    let mut draft = InvoiceDraft::demo("2026-09-21", "PAGE-LIMIT");
    let row = draft.rows[0].clone();
    draft.rows = (0..5)
        .map(|index| {
            let mut row = row.clone();
            row.cells[1] = format!("STYLE-{}", index + 1);
            row
        })
        .collect();
    let result = render_design(
        &crate::ReportData::invoice(&draft.build().unwrap(), json!({}), json!({}), false).unwrap(),
        &design,
        &AtomicBool::new(false),
    )
    .unwrap();
    assert_eq!(result.pages.len(), 3);
    assert!(result.pages[0].svg.contains("STYLE-2"));
    assert!(!result.pages[0].svg.contains("STYLE-3"));
    assert!(result.pages[1].svg.contains("STYLE-3"));
    assert!(result.pages[1].svg.contains("STYLE-4"));
    assert!(result.pages[2].svg.contains("STYLE-5"));
}

#[test]
fn grid_vertical_alignment_changes_text_origin() {
    let mut design = grid();
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut design.layers[1].elements[0].kind
    {
        block.rows[0].cells[1].style.vertical_align = Some("Bottom".into());
        block.rows[0].height_mm = Some(40.);
        block.rows[0].cells[1].style.font_size_pt = Some(6.);
    }
    let mut top_design = design.clone();
    if let Kind::Flow {
        block: ReportBlock::Grid(block),
        ..
    } = &mut top_design.layers[1].elements[0].kind
    {
        block.rows[0].cells[1].style.vertical_align = Some("Top".into());
    }
    let top = render_design(&data(), &top_design, &AtomicBool::new(false)).unwrap();
    let bottom = render_design(&data(), &design, &AtomicBool::new(false)).unwrap();
    let text_y = |svg: &str| {
        svg.split("<text ")
            .find_map(|part| part.contains("No: GRID-IN").then_some(part))
            .and_then(|part| part.split(" y=\"").nth(1))
            .and_then(|part| part.split('"').next())
            .and_then(|value| value.parse::<f32>().ok())
            .expect("invoice number text should be present")
    };
    assert!(text_y(&bottom.pages[0].svg) > text_y(&top.pages[0].svg));
}
