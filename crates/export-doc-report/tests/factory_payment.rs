use export_doc_contracts::generated_api::{ApiPaymentDto, ApiPaymentGoodsItem};
use export_doc_report::{Builtin, ReportData, render_builtin};
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn positions(svg: &str, text: &str) -> Vec<(f32, f32)> {
    svg.split("<text ")
        .filter_map(|part| {
            let (attributes, content) = part.split_once('>')?;
            if content.split("</text>").next()? != text {
                return None;
            }
            let coordinate = |name: &str| -> Option<f32> {
                attributes
                    .split(&format!("{name}=\""))
                    .nth(1)?
                    .split('"')
                    .next()?
                    .parse()
                    .ok()
            };
            Some((coordinate("x")?, coordinate("y")?))
        })
        .collect()
}

#[test]
fn one_to_four_goods_print_identically_on_two_a4_copies() {
    for count in 1..=4 {
        let payment = ApiPaymentDto {
            payment_date: Some("2026-11-25".into()),
            payer_name: "样例贸易有限公司".into(),
            invoice_no: "FACTORY-2026-001".into(),
            bank_name: "样例银行支行".into(),
            account_no: "TEST-ACCOUNT-001".into(),
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
        let invoice = positions(&page.svg, "FACTORY-2026-001");
        for i in 1..=count {
            let names = positions(&page.svg, &format!("货物{i}"));
            let quantities = positions(&page.svg, &format!("{i}.125件"));
            for copy in 0..2 {
                assert!((names[copy].1 - quantities[copy].1).abs() < 0.1);
                if count == 1 {
                    assert!(
                        (names[copy].1 - invoice[copy].1).abs() < 0.1,
                        "single item must share the merged invoice region's vertical center"
                    );
                }
            }
        }
        let bank = positions(&page.svg, "样例银行支行");
        let account = positions(&page.svg, "TEST-ACCOUNT-001");
        let label = positions(&page.svg, "开户银行帐号：");
        for copy in 0..2 {
            assert!(bank[copy].0 > label[copy].0);
            assert!((bank[copy].0 - account[copy].0).abs() < 0.1);
            assert!((account[copy].1 - bank[copy].1 - 7.).abs() < 0.1);
        }
        for labels in [["L/C", "D/P"].as_slice(), ["13%", "9%", "0%"].as_slice()] {
            let expected = if labels.len() == 2 { 18. } else { 12. };
            for copy in 0..2 {
                let rows: Vec<_> = labels
                    .iter()
                    .map(|value| positions(&page.svg, value)[copy].1)
                    .collect();
                assert!(
                    rows.windows(2)
                        .all(|row| (row[1] - row[0] - expected).abs() < 0.1)
                );
            }
        }
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
