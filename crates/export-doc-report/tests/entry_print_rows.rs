use export_doc_contracts::generated_api::{
    ApiInvoiceDetailDto, ApiInvoiceItemDto, ApiInvoicePrintRow,
};
use export_doc_domain::designer::{Kind, ReportBlock};
use export_doc_domain::invoice::InvoiceDraft;
use export_doc_report::{Builtin, ReportData, render_builtin, render_design};
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn row(kind: &str, text: &str) -> ApiInvoicePrintRow {
    ApiInvoicePrintRow {
        kind: kind.into(),
        text: text.into(),
        ..Default::default()
    }
}

fn y(svg: &str, text: &str) -> f32 {
    svg.split("<text ")
        .find(|node| node.contains(&format!(">{text}</text>")))
        .unwrap()
        .split("y=\"")
        .nth(1)
        .unwrap()
        .split('"')
        .next()
        .unwrap()
        .parse()
        .unwrap()
}

#[test]
fn entry_order_blank_rows_and_two_amounts_share_one_description_without_changing_totals() {
    let items = [(331, 7944, 73849), (185, 3330, 40293)]
        .into_iter()
        .enumerate()
        .map(|(index, (cartons, quantity, amount))| ApiInvoiceItemDto {
            style_name: "INTERNAL PRODUCT NAME".into(),
            quantity: quantity.into(),
            cartons: cartons.into(),
            total_price: amount.into(),
            price_calculation_mode: "LineAmountDriven".into(),
            unit_en: "PCS".into(),
            ctn_unit_en: "CTNS".into(),
            print_rows: Some(if index == 0 {
                vec![
                    row("Text", "MEN SWEATERS"),
                    row("Item", ""),
                    row("Blank", ""),
                ]
            } else {
                vec![row("Item", "")]
            }),
            ..Default::default()
        })
        .collect();
    let invoice = InvoiceDraft::from_dto(ApiInvoiceDetailDto {
        invoice_no: "ENTRY-TEST".into(),
        invoice_date: "2026-10-19".into(),
        shipment_date: "2026-10-19".into(),
        use_entry_print_rows: Some(true),
        currency: "USD".into(),
        items,
        ..Default::default()
    })
    .build()
    .unwrap();
    assert_eq!(invoice.total_cartons, 516.into());
    assert_eq!(invoice.total_amount, 114142.into());
    assert_eq!(
        invoice.items[0].print_rows.as_ref().unwrap()[2].kind,
        "Blank"
    );
    let mut data = ReportData::invoice(&invoice, json!({}), json!({}), false).unwrap();
    for template in [Builtin::Invoice, Builtin::PackingList] {
        let document = render_builtin(template, &data, &AtomicBool::new(false)).unwrap();
        let svg = document
            .pages
            .iter()
            .map(|page| page.svg.as_str())
            .collect::<String>();
        assert_eq!(svg.matches("MEN SWEATERS").count(), 1);
        assert!(!svg.contains("INTERNAL PRODUCT NAME"));
        assert!(svg.find("7944PCS").unwrap() < svg.find("3330PCS").unwrap());
        let mut compact = invoice.clone();
        compact.items[0].print_rows.as_mut().unwrap().pop();
        let compact_data = ReportData::invoice(&compact, json!({}), json!({}), false).unwrap();
        let compact_output =
            render_builtin(template, &compact_data, &AtomicBool::new(false)).unwrap();
        assert!(
            y(&svg, "3330PCS") - y(&compact_output.pages[0].svg, "3330PCS") >= 3.99,
            "a stored blank row must reserve real printed height"
        );
    }
    for template in [Builtin::Contract, Builtin::CustomsDeclaration] {
        let original = render_builtin(template, &data, &AtomicBool::new(false)).unwrap();
        assert!(
            !original
                .pages
                .iter()
                .any(|page| page.svg.contains("MEN SWEATERS"))
        );
    }
    data.root["Invoice"]["useEntryPrintRows"] = json!(false);
    let document = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
    assert!(document.pages[0].svg.contains("INTERNAL PRODUCT NAME"));
    assert!(!document.pages[0].svg.contains("MEN SWEATERS"));
}

#[test]
fn arbitrary_entry_rows_preserve_order_across_page_limits() {
    let mut invoice = InvoiceDraft::demo("2026-10-19", "ENTRY-PAGES")
        .build()
        .unwrap();
    let seed = invoice.items[0].clone();
    invoice.use_entry_print_rows = Some(true);
    invoice.items = (0..12)
        .map(|index| ApiInvoiceItemDto {
            print_rows: Some(vec![
                row("Text", &format!("STYLE-{index:02}")),
                row("Item", &format!("PRODUCT-{index:02}")),
                row("Text", &format!("ORDER-{index:02}")),
                row("Blank", ""),
            ]),
            ..seed.clone()
        })
        .collect();
    let invoice = InvoiceDraft::from_dto(invoice).build().unwrap();
    let data = ReportData::invoice(&invoice, json!({}), json!({}), false).unwrap();
    let mut design = Builtin::Invoice.design().unwrap();
    let detail = design
        .layers
        .iter_mut()
        .flat_map(|layer| &mut layer.elements)
        .find_map(|element| match &mut element.kind {
            Kind::Flow {
                block: ReportBlock::DetailTable(table),
                ..
            } => Some(table),
            _ => None,
        })
        .unwrap();
    detail.print.first_page_rows = Some(5);
    detail.print.continuation_page_rows = Some(8);
    let output = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert!(output.pages.len() > 2);
    let svg = output
        .pages
        .iter()
        .map(|page| page.svg.as_str())
        .collect::<String>();
    let mut previous = 0;
    for index in 0..12 {
        for prefix in ["STYLE", "PRODUCT", "ORDER"] {
            let marker = format!("{prefix}-{index:02}");
            assert_eq!(svg.matches(&marker).count(), 1);
            let position = svg.find(&marker).unwrap();
            assert!(position > previous);
            previous = position;
        }
    }
}
