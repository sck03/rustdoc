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
fn goods_array_projects_editable_template_fields() {
    let data = ReportData::payment(
        &ApiPaymentDto {
            goods_items: Some(vec![ApiPaymentGoodsItem {
                goods_name: "付款货物".into(),
                quantity: "12.5".into(),
                quantity_unit: "箱".into(),
                ..Default::default()
            }]),
            ..Default::default()
        },
        json!({}),
    )
    .unwrap();
    assert_eq!(data.text("Payment.Goods1.GoodsName"), "付款货物");
    assert_eq!(data.text("Payment.goodsName"), "付款货物");
    assert_eq!(data.text("Payment.quantity"), "12.5");
    assert_eq!(data.text("Payment.quantityUnit"), "箱");
    assert_eq!(data.text("Payment.Goods1.QuantityText"), "12.5箱");
    assert!(data.text("Payment.Goods2.GoodsName").is_empty());
}

#[test]
fn expanded_bank_area_keeps_long_names_and_accounts_on_their_own_lines() {
    let bank_name = "中国工商银行股份有限公司宁波市海曙支行";
    let account_no = "1234567890123456789012";
    let mut payment = ApiPaymentDto {
        bank_name: bank_name.into(),
        account_no: account_no.into(),
        cny_amount: "232151.04".parse().unwrap(),
        ..Default::default()
    };
    let data = ReportData::payment(&payment, json!({})).unwrap();
    let document = render_builtin(Builtin::FactoryPayment, &data, &AtomicBool::new(false)).unwrap();
    let svg = &document.pages[0].svg;
    assert_eq!(
        positions(svg, bank_name).len(),
        2,
        "the entire bank name must occupy one line in each copy"
    );
    assert_eq!(positions(svg, account_no).len(), 2);
    let label = positions(svg, "开户银行帐号：");
    let amount = positions(svg, "小写：");
    for copy in 0..2 {
        assert!((label[copy].0 - amount[copy].0).abs() < 0.1);
        assert!(
            label[copy].0 < 115.,
            "bank and amount must start at the expanded boundary"
        );
    }
    payment.bank_name = "中".repeat(20);
    let data = ReportData::payment(&payment, json!({})).unwrap();
    let document = render_builtin(Builtin::FactoryPayment, &data, &AtomicBool::new(false)).unwrap();
    assert_eq!(positions(&document.pages[0].svg, &"中".repeat(19)).len(), 2);
    assert_eq!(positions(&document.pages[0].svg, "中").len(), 2);
}

#[test]
fn long_bank_and_payee_names_wrap_to_two_lines_with_consistent_typography() {
    let bank_name = "中国工商银行股份有限公司宁波市海曙区外贸综合服务支行";
    let payee_name = "宁波市海曙区布利杰国际进出口贸易发展有限责任公司";
    let account_no = "1234567890123456789012";
    let mut payment = ApiPaymentDto {
        bank_name: bank_name.into(),
        payee_name: payee_name.into(),
        account_no: account_no.into(),
        cny_amount: "232151.04".parse().unwrap(),
        ..Default::default()
    };
    let data = ReportData::payment(&payment, json!({})).unwrap();
    let document = render_builtin(Builtin::FactoryPayment, &data, &AtomicBool::new(false)).unwrap();
    assert_eq!(document.pages.len(), 1);
    let svg = &document.pages[0].svg;
    let account = positions(svg, account_no);
    for name in [bank_name, payee_name] {
        let first: String = name.chars().take(19).collect();
        let second: String = name.chars().skip(19).collect();
        let tops = positions(svg, &first);
        let bottoms = positions(svg, &second);
        assert_eq!(tops.len(), 2, "each copy must contain the first line");
        assert_eq!(
            bottoms.len(),
            2,
            "each copy must contain the rest of the name"
        );
        for copy in 0..2 {
            assert!((bottoms[copy].1 - tops[copy].1 - 8. * 25.4 / 72. * 1.35).abs() < 0.1);
            if name == bank_name {
                assert!(bottoms[copy].1 < account[copy].1 - 3.);
            } else {
                assert!(bottoms[copy].1 < 102. + copy as f32 * 140. - 1.);
            }
        }
        for line in [&first, &second] {
            assert_typography(svg, line, 8., true);
        }
    }
    for label in ["货源单位名称：", "开户银行帐号：", "小写："] {
        assert_typography(svg, label, 7.5, false);
    }
    for field in [account_no, "￥232151.04"] {
        assert_typography(svg, field, 8., true);
    }
    for bank in [true, false] {
        payment.bank_name = bank_name.into();
        payment.payee_name = payee_name.into();
        if bank {
            payment.bank_name = "中".repeat(39);
        } else {
            payment.payee_name = "中".repeat(39);
        }
        let data = ReportData::payment(&payment, json!({})).unwrap();
        assert!(
            render_builtin(Builtin::FactoryPayment, &data, &AtomicBool::new(false)).is_err(),
            "a third line must fail clearly instead of overlapping the account or amount"
        );
    }
}

fn assert_typography(svg: &str, text: &str, pt: f32, bold: bool) {
    let attributes: Vec<_> = svg
        .split("<text ")
        .filter_map(|part| {
            let (attributes, content) = part.split_once('>')?;
            (content.split("</text>").next()? == text).then_some(attributes)
        })
        .collect();
    assert_eq!(attributes.len(), 2, "both copies must include {text}");
    for attributes in attributes {
        let size: f32 = attributes
            .split("font-size=\"")
            .nth(1)
            .unwrap()
            .split('"')
            .next()
            .unwrap()
            .parse()
            .unwrap();
        assert!((size - pt * 25.4 / 72.).abs() < 0.001);
        assert!(attributes.contains(if bold {
            "font-weight=\"700\""
        } else {
            "font-weight=\"400\""
        }));
        if bold {
            assert!(attributes.contains("font-family=\"Noto Sans CJK SC\""));
        }
    }
}
