use super::*;
use serde_json::json;

fn blank_grid(top: i32, height: i32) -> Design {
    let mut design = Design::invoice();
    let mut element = design.layers[0].elements[0].clone();
    for layer in &mut design.layers {
        layer.elements.clear();
    }
    design.page.margin_top_hundredth_mm = 1000;
    design.page.margin_bottom_hundredth_mm = 1000;
    element.x_hundredth_mm = 1500;
    element.y_hundredth_mm = top;
    element.width_hundredth_mm = 18000;
    element.height_hundredth_mm = height;
    element.kind = Kind::Flow {
        flow_kind: "Grid".into(),
        block: serde_json::from_value(json!({
            "id":"grid", "type":"Grid", "title":"",
            "columns":[{"id":"column","widthPercent":100}],
            "rows":[{"id":"row","heightMm":10,"cells":[{
                "id":"cell","colSpan":1,"rowSpan":1,"contentKind":"Text",
                "text":"Blank grid","label":"","fieldPath":"","fallbackText":"",
                "verticalText":false,"checkboxOptions":[],"style":{"fontSizePt":10}
            }]}],
            "border":{"color":"#000000","widthPx":0.6,"style":"Solid","top":true,"right":true,"bottom":true,"left":true},
            "defaultCellStyle":{"fontSizePt":10}
        }))
        .unwrap(),
    };
    design.layers[1].elements.push(element);
    design
}

fn render(design: &Design) -> Result<Vec<String>> {
    let invoice = export_doc_domain::invoice::InvoiceDraft::demo("2026-10-12", "BLANK-001")
        .build()
        .unwrap();
    let data = crate::ReportData::invoice(&invoice, json!({}), json!({}), false).unwrap();
    pages_data(&data, design, &AtomicBool::new(false))
}

#[test]
fn body_flow_preserves_initial_position_and_printable_margins() {
    for (top, offset) in [(0, 10), (1800, 0), (6000, 0)] {
        let pages = render(&blank_grid(top, 1000)).unwrap();
        assert_eq!(pages.len(), 1);
        assert!(pages[0].contains(&format!("<g transform=\"translate(0 {offset})\">")));
    }
    assert!(render(&blank_grid(28000, 1000)).is_err());
}

#[test]
fn body_flow_continuation_respects_top_and_bottom_margins() {
    let mut design = blank_grid(2000, 10000);
    let mut second = design.layers[1].elements[0].clone();
    second.id = "second-grid".into();
    second.y_hundredth_mm = 19000;
    design.layers[1].elements.push(second);
    let pages = render(&design).unwrap();
    assert_eq!(pages.len(), 2);
    assert!(pages[0].contains("<g transform=\"translate(0 0)\">"));
    assert!(pages[1].contains("<g transform=\"translate(0 -180)\">"));
}
