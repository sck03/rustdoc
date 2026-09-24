use export_doc_domain::{
    designer::{Design, Kind},
    invoice::InvoiceDraft,
};
use export_doc_report::{ReportData, render_design};
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn design() -> Design {
    let mut design = Design::invoice();
    let seed = design.layers[0].elements[0].clone();
    for layer in &mut design.layers {
        layer.elements.clear();
        layer.print.repeat_on_every_page = false;
    }
    design.detail_row_height_hundredth_mm = Some(1800);
    for (id, path, x, y, width) in [
        ("name", "item.StyleName", 6000, 10000, 6000),
        ("quantity", "item.Quantity", 14000, 10000, 2500),
        ("number", "Invoice.InvoiceNo", 1000, 5000, 9000),
        ("marks", "Invoice.ShippingMarks", 1000, 10000, 4000),
    ] {
        let mut field = seed.clone();
        field.id = id.into();
        field.label = id.into();
        field.x_hundredth_mm = x;
        field.y_hundredth_mm = y;
        field.width_hundredth_mm = width;
        field.height_hundredth_mm = 800;
        field.style.font_size_pt = 9.;
        field.kind = Kind::Field {
            field_path: path.into(),
            fallback_text: String::new(),
        };
        design.layers[1].elements.push(field);
    }
    design
}
#[test]
fn products_repeat_at_fixed_columns_across_pages_while_document_fields_appear_once() {
    let mut data = ReportData::invoice(
        &InvoiceDraft::demo("2026-09-25", "ONCE-INVOICE")
            .build()
            .unwrap(),
        json!({}),
        json!({}),
        false,
    )
    .unwrap();
    data.root["Invoice"]["shippingMarks"] = json!("ONCE-MARKS");
    data.root["items"] = json!(
        (0..25)
            .map(|i| json!({"StyleName":format!("PRODUCT-{i:02}"),"Quantity":i+1}))
            .collect::<Vec<_>>()
    );
    let result = render_design(&data, &design(), &AtomicBool::new(false)).unwrap();
    assert!(result.pages.len() >= 3);
    let all = result
        .pages
        .iter()
        .map(|page| page.svg.as_str())
        .collect::<String>();
    assert_eq!(all.matches("ONCE-INVOICE").count(), 1);
    assert_eq!(all.matches("ONCE-MARKS").count(), 1);
    for i in 0..25 {
        assert_eq!(all.matches(&format!("PRODUCT-{i:02}")).count(), 1);
    }
    for page in &result.pages {
        assert!(page.svg.contains("x=\"61\""));
    }
}
#[test]
fn empty_product_list_has_no_fake_rows_and_overlap_is_rejected() {
    let mut data = ReportData::invoice(
        &InvoiceDraft::demo("2026-09-25", "EMPTY-INVOICE")
            .build()
            .unwrap(),
        json!({}),
        json!({}),
        false,
    )
    .unwrap();
    data.root["items"] = json!([]);
    data.root["Invoice"]["shippingMarks"] = json!("");
    let mut design = design();
    let output = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert_eq!(output.pages.len(), 1);
    assert!(output.pages[0].svg.contains("EMPTY-INVOICE"));
    design.layers[1]
        .elements
        .iter_mut()
        .find(|e| e.id == "number")
        .unwrap()
        .y_hundredth_mm = 11000;
    assert!(render_design(&data, &design, &AtomicBool::new(false)).is_err());
}
