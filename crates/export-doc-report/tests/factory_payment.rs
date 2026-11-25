use export_doc_contracts::generated_api::{ApiPaymentDto, ApiPaymentGoodsItem};
use export_doc_report::{Builtin, ReportData, render_builtin};
use serde_json::json;
use std::sync::atomic::AtomicBool;

#[test]
fn one_to_four_goods_print_identically_on_two_a4_copies() {
    for count in 1..=4 {
        let payment = ApiPaymentDto {
            payment_date: Some("2026-11-25".into()),
            payer_name: "样例贸易有限公司".into(),
            invoice_no: "FACTORY-2026-001".into(),
            goods_items: Some(
                (1..=count)
                    .map(|i| ApiPaymentGoodsItem {
                        goods_name: format!("货物{i}"),
                        quantity: format!("{i}.125"),
                        quantity_unit: "件".into(),
                        ..Default::default()
                    })
                    .collect(),
            ),
            cny_amount: "232151.04".parse().unwrap(),
            ..Default::default()
        };
        let data = ReportData::payment(&payment, json!({})).unwrap();
        let document =
            render_builtin(Builtin::FactoryPayment, &data, &AtomicBool::new(false)).unwrap();
        assert_eq!(document.pages.len(), 1);
        let page = &document.pages[0];
        assert_eq!((page.width_mm, page.height_mm), (210., 297.));
        for i in 1..=count {
            assert_eq!(page.svg.matches(&format!("货物{i}")).count(), 2);
            assert_eq!(page.svg.matches(&format!("{i}.125件")).count(), 2);
        }
        assert_eq!(page.svg.matches("FACTORY-2026-001").count(), 2);
        assert_eq!(page.svg.matches("232151.04").count(), 2);
        assert_eq!(page.svg.matches("2026 年 11 月 25 日").count(), 2);
        assert!(!page.svg.contains("NO."));
        assert!(!page.svg.contains("#ffff00"));
        assert!(page.svg.contains('②') && page.svg.contains('③'));
    }
}

#[test]
fn existing_single_goods_remain_available_to_editable_template_fields() {
    let data = ReportData::payment(
        &ApiPaymentDto {
            goods_name: "既有货物".into(),
            quantity: "12.5".into(),
            quantity_unit: "箱".into(),
            ..Default::default()
        },
        json!({}),
    )
    .unwrap();
    assert_eq!(data.text("Payment.Goods1.GoodsName"), "既有货物");
    assert_eq!(data.text("Payment.Goods1.QuantityText"), "12.5箱");
    assert!(data.text("Payment.Goods2.GoodsName").is_empty());
}
