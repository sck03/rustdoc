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
            "id":"grid", "type":"Grid", "columns":[{"id":"c1","widthPercent":25},{"id":"c2","widthPercent":25},{"id":"c3","widthPercent":50}],
            "defaultCellStyle":{"fontSizePt":12,"bold":true,"align":"Center","marginLeftMm":0,"marginTopMm":0},
            "border":{"color":"#ff0000","widthPx":1,"style":"Dashed","top":true,"right":false,"bottom":true,"left":false},
            "rows":[
                {"id":"r1","heightMm":10,"cells":[{"id":"a","contentKind":"Text","text":"SPAN","colSpan":2,"rowSpan":2},{"id":"b","contentKind":"Field","fieldPath":"Invoice.InvoiceNo","label":"No"}]},
                {"id":"r2","heightMm":15,"cells":[{"id":"c","contentKind":"Text","text":"中文","verticalText":true}]}
            ]
        })).unwrap(),
    };
    design.layers[1].elements.push(element);
    design
}

#[test]
fn grid_merge_uses_real_column_occupancy_styles_and_vertical_text() {
    let result = render_design(&data(), &grid(), &AtomicBool::new(false)).unwrap();
    let svg = &result.pages[0].svg;
    assert!(
        svg.contains("x1=\"10\" y1=\"35\" x2=\"60\" y2=\"35\""),
        "merged cell must cover 50 mm x 25 mm"
    );
    assert!(
        svg.contains("x1=\"60\" y1=\"20\" x2=\"110\" y2=\"20\""),
        "second-row cell must skip the rowspan"
    );
    assert!(svg.contains("stroke=\"#ff0000\""));
    assert!(svg.contains("stroke-dasharray=\"2 1\""));
    assert!(svg.contains("font-weight=\"700\" font-family=\"Noto Sans CJK SC\""));
    assert!(svg.contains("fill=\"#112233\""));
    assert!(svg.contains("No: GRID-INVOICE"));
    assert!(svg.contains(">中</text>"));
    assert!(svg.contains(">文</text>"));
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
        block.rows[0].cells[0].col_span = 3;
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
