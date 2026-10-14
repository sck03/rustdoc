use export_doc_domain::{
    designer::{Kind, ProductSettings},
    invoice::InvoiceDraft,
};
use export_doc_report::{Builtin, ReportData, render_builtin, render_design};
use rust_decimal::Decimal;
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn invoice() -> export_doc_contracts::generated_api::ApiInvoiceDetailDto {
    let mut invoice = InvoiceDraft::demo("2026-10-14", "ADJUSTMENTS")
        .build()
        .unwrap();
    invoice.items.truncate(1);
    invoice.items[0].quantity = Decimal::ONE;
    invoice.items[0].unit_price = Decimal::from(20000);
    invoice.items[0].total_price = Decimal::from(20000);
    invoice.total_amount = Decimal::from(20000);
    invoice
}
fn text_y(svg: &str, text: &str) -> f32 {
    text_coordinate(svg, text, "y")
}

fn text_coordinate(svg: &str, text: &str, axis: &str) -> f32 {
    svg.split("<text ")
        .find(|node| node.contains(&format!(">{text}</text>")))
        .unwrap_or_else(|| panic!("Missing {text}"))
        .split(&format!("{axis}=\""))
        .nth(1)
        .unwrap()
        .split('"')
        .next()
        .unwrap()
        .parse()
        .unwrap()
}

#[test]
fn invoice_trade_terms_use_the_first_page_amount_intro_cell() {
    for terms in ["FOB", "FOB SHANGHAI"] {
        let mut invoice = invoice();
        invoice.trade_terms = terms.into();
        invoice.port_of_loading = "SHANGHAI".into();
        let item = invoice.items[0].clone();
        invoice.items = (1..=36)
            .map(|index| {
                let mut item = item.clone();
                item.style_name = format!("GOODS-{index:02}");
                item
            })
            .collect();
        invoice.total_amount = Decimal::from(720000);
        let data = ReportData::invoice(&invoice, json!({}), json!({}), false).unwrap();
        let result = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
        assert!(result.pages.len() > 1);
        let first = &result.pages[0].svg;
        assert!(text_y(first, terms) > text_y(first, "Amount"));
        assert!(text_y(first, terms) < text_y(first, "GOODS-01"));
        assert!(text_coordinate(first, terms, "x") > 162.);
        assert_eq!(
            result
                .pages
                .iter()
                .map(|page| page.svg.matches(&format!(">{terms}</text>")).count())
                .sum::<usize>(),
            1
        );
        if terms == "FOB" {
            assert!(!first.contains(">FOB SHANGHAI</text>"));
        }
    }
}

#[test]
fn multiple_optional_adjustments_print_before_the_exact_payable_total() {
    let mut invoice = invoice();
    fn data(invoice: &export_doc_contracts::generated_api::ApiInvoiceDetailDto) -> ReportData {
        ReportData::invoice(invoice, json!({}), json!({}), false).unwrap()
    }
    let before =
        render_builtin(Builtin::Invoice, &data(&invoice), &AtomicBool::new(false)).unwrap();
    invoice.amount_adjustments = Some(vec![]);
    assert_eq!(
        before.pages[0].svg,
        render_builtin(Builtin::Invoice, &data(&invoice), &AtomicBool::new(false))
            .unwrap()
            .pages[0]
            .svg
    );
    invoice.amount_adjustments = Some(
        serde_json::from_value(json!([
            {"description":"RECEIVED DEPOSIT","amount":"-10963.50"},
            {"description":"SAMPLE CHARGE","amount":"125.10"},
            {"description":"OTHER FEE","amount":"0.20"}
        ]))
        .unwrap(),
    );
    let report = data(&invoice);
    assert_eq!(report.text("Invoice.TotalAmount"), "20000.00");
    assert_eq!(report.text("Invoice.AdjustedTotalAmount"), "9161.80");
    let result = render_builtin(Builtin::Invoice, &report, &AtomicBool::new(false)).unwrap();
    let page = &result.pages.last().unwrap().svg;
    for amount in ["-USD10963.50", "USD125.10", "USD0.20", "USD9161.80"] {
        assert!(page.contains(amount), "{amount}");
    }
    assert!(text_y(page, "TOTAL:") > text_y(page, "OTHER FEE"));
    invoice.special_terms = "CONTRACT-MUST-OMIT-THIS".into();
    let contract =
        render_builtin(Builtin::Contract, &data(&invoice), &AtomicBool::new(false)).unwrap();
    assert!(
        !contract
            .pages
            .iter()
            .any(|page| page.svg.contains("CONTRACT-MUST-OMIT-THIS"))
    );
}

#[test]
fn long_adjustment_lists_paginate_without_an_orphan_total() {
    let mut invoice = invoice();
    invoice.amount_adjustments = Some((0..32).map(|index| serde_json::from_value(json!({"description":format!("FEE-{index:02}\nExtra description\nRepeated service details"),"amount":"1.00"})).unwrap()).collect());
    let data = ReportData::invoice(&invoice, json!({}), json!({}), false).unwrap();
    let result = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
    assert!(result.pages.len() > 1);
    let all = result
        .pages
        .iter()
        .map(|page| page.svg.as_str())
        .collect::<String>();
    for index in 0..32 {
        assert_eq!(all.matches(&format!(">FEE-{index:02}</text>")).count(), 1);
    }
    let last = &result.pages.last().unwrap().svg;
    assert!(last.contains("FEE-31") && last.contains("USD20032.00") && last.contains("TOTAL:"));
    assert_eq!(all.matches(">TOTAL:</text>").count(), 1);
}

#[test]
fn independent_fields_collapse_empty_bands_and_keep_explicit_spacers() {
    let mut design = Builtin::Invoice.design().unwrap();
    let seed = design.layers[0].elements[0].clone();
    design
        .layers
        .iter_mut()
        .for_each(|layer| layer.elements.clear());
    design.detail_row_height_hundredth_mm = Some(400);
    for (id, path, x, y) in [
        ("name", "item.StyleName", 1000, 5000),
        ("style", "item.StyleNo", 1000, 5400),
        ("qty", "item.Quantity", 10000, 5400),
    ] {
        let mut field = seed.clone();
        field.id = id.into();
        field.x_hundredth_mm = x;
        field.y_hundredth_mm = y;
        field.width_hundredth_mm = 6000;
        field.height_hundredth_mm = 400;
        field.style.font_size_pt = 7.;
        field.style.padding_hundredth_mm = 0;
        field.style.vertical_align = if id == "qty" { "Bottom" } else { "Top" }.into();
        field.kind = Kind::Field {
            field_path: path.into(),
            fallback_text: String::new(),
            field_format: None,
        };
        if id == "style" {
            field.product = Some(ProductSettings {
                hide_when_empty: true,
                collapse_empty: true,
                ..Default::default()
            });
        }
        design.layers[1].elements.push(field);
    }
    let mut data = ReportData::invoice(&invoice(), json!({}), json!({}), false).unwrap();
    data.root["items"] = json!([{"StyleName":"FIRST","StyleNo":"STYLE-1","Quantity":10},{"StyleName":"SECOND","StyleNo":"STYLE-2","Quantity":20}]);
    let full = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    data.root["items"][0]["StyleNo"] = json!(" \t　");
    let compact = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert!(
        (text_y(&full.pages[0].svg, "SECOND") - text_y(&compact.pages[0].svg, "SECOND") - 4.).abs()
            < 0.02
    );
    assert!(
        (text_y(&full.pages[0].svg, "10") - text_y(&compact.pages[0].svg, "10") - 4.).abs() < 0.02
    );
    let mut spacer = seed;
    spacer.id = "spacer".into();
    spacer.kind = Kind::ProductSpacer;
    spacer.x_hundredth_mm = 1000;
    spacer.y_hundredth_mm = 5800;
    spacer.width_hundredth_mm = 15000;
    spacer.height_hundredth_mm = 400;
    design.layers[1].elements.push(spacer);
    let spaced = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert!(
        (text_y(&spaced.pages[0].svg, "SECOND") - text_y(&compact.pages[0].svg, "SECOND") - 4.)
            .abs()
            < 0.02
    );
    design.element_mut("style").unwrap().product=Some(serde_json::from_value(json!({"collapseEmpty":true,"condition":{"fieldPath":"item.Quantity","comparisonType":"Number","operator":"GreaterThan","value":"100"}})).unwrap());
    let conditional = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert!(!conditional.pages[0].svg.contains("STYLE-2"));
}
