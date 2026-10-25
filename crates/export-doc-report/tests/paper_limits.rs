#[cfg(test)]
#[path = "../../../tests/ReportTemplateFixtures/designer_fixture.rs"]
mod fixture;

use export_doc_report::{ReportData, render_design};
use std::sync::atomic::AtomicBool;
#[test]
fn full_custom_paper_contract_is_accepted_by_layout() {
    let mut design = fixture::invoice();
    for layer in &mut design.layers {
        layer.elements.clear();
    }
    design.page.size = "Custom".into();
    design.page.orientation = "Landscape".into();
    design.page.width_hundredth_mm = 60000;
    design.page.height_hundredth_mm = 40000;
    let data = ReportData {
        report_type: "ExportDocument",
        root: serde_json::json!({}),
        images: Default::default(),
    };
    let document = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert_eq!(document.pages.len(), 1);
    assert_eq!(document.pages[0].width_mm, 600.);
    assert_eq!(document.pages[0].height_mm, 400.);
    design.page.width_hundredth_mm = 60001;
    assert!(render_design(&data, &design, &AtomicBool::new(false)).is_err());
}
