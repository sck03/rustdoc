use export_doc_domain::{generated_api::ApiInvoiceItemDto, invoice_calculation};
use rust_decimal::Decimal;
use serde_json::Value;
use std::str::FromStr;

#[test]
fn profit_persistence_and_analysis_share_the_editor_fixtures() {
    use export_doc_domain::{
        generated_api::ApiInvoiceDetailDto, invoice::InvoiceDraft, invoice_profit,
    };
    let cases: Vec<Value> = serde_json::from_str(include_str!(
        "../../../tests/ContractFixtures/invoice-profit.json"
    ))
    .unwrap();
    for case in cases {
        let number = |value: &Value| Decimal::from_str(value.as_str().unwrap()).unwrap();
        let dto = ApiInvoiceDetailDto {
            invoice_no: "PROFIT-TEST".into(),
            invoice_date: "2026-11-18".into(),
            shipment_date: "2026-11-18".into(),
            currency: case["currency"].as_str().unwrap().into(),
            exchange_rate: case.get("exchangeRate").map(number),
            items: case["items"]
                .as_array()
                .unwrap()
                .iter()
                .map(|row| ApiInvoiceItemDto {
                    quantity: number(&row["quantity"]),
                    unit_price: number(&row["unitPrice"]),
                    purchase_total: number(&row["purchaseTotal"]),
                    tax_rebate_rate: number(&row["taxRebateRate"]),
                    ..Default::default()
                })
                .collect(),
            ..Default::default()
        };
        let invoice = InvoiceDraft::from_dto(dto).build().unwrap();
        assert_eq!(
            invoice.total_tax_refund_amount,
            number(&case["expectedRefund"]),
            "{case}"
        );
        assert_eq!(
            invoice.total_profit,
            number(&case["expectedProfit"]),
            "{case}"
        );
        assert_eq!(
            invoice_profit::calculate(&invoice).unwrap().gross_profit,
            invoice.total_profit,
            "{case}"
        );
    }
}

#[test]
fn invoice_editor_and_rust_use_the_same_exact_arithmetic_fixtures() {
    let cases: Vec<Value> = serde_json::from_str(include_str!(
        "../../../tests/ContractFixtures/invoice-arithmetic.json"
    ))
    .unwrap();
    for case in cases {
        let number = |key: &str| Decimal::from_str(case[key].as_str().unwrap()).unwrap();
        let mut item = ApiInvoiceItemDto {
            quantity: number("quantity"),
            unit_price: number("unitPrice"),
            ..Default::default()
        };
        invoice_calculation::recalculate(&mut item, &[8, 23]).unwrap();
        assert_eq!(item.unit_price, number("expectedUnitPrice"), "{case}");
        assert_eq!(item.total_price, number("expectedAmount"), "{case}");
    }
    assert!(std::mem::size_of::<export_doc_domain::designer::Kind>() <= 512);
}

#[test]
fn signed_invoice_money_roundtrips_and_physical_values_remain_nonnegative() {
    let mut draft = InvoiceDraft::demo("2026-11-19", "SIGNED-MONEY");
    draft.rows.truncate(1);
    for (column, value) in [(8, "2"), (24, "-1000"), (25, "-1.005"), (27, "13")] {
        draft.rows[0].cells[column] = value.into();
    }
    draft.rows[0].recalculate(&[8, 24, 25, 27]).unwrap();
    let invoice = draft.build().unwrap();
    assert_eq!(invoice.items[0].unit_price, Decimal::from(-500));
    assert_eq!(invoice.total_amount, Decimal::from(-1000));
    assert_eq!(invoice.total_purchase_amount, Decimal::new(-201, 2));
    assert!(invoice.total_tax_refund_amount < Decimal::ZERO);
    assert_eq!(
        InvoiceDraft::from_dto(invoice.clone()).build().unwrap(),
        invoice
    );
    for column in [8, 11, 12, 15, 16, 17, 18, 19, 20, 21, 22, 27] {
        let mut invalid = draft.clone();
        invalid.rows[0].cells[column] = "-1".into();
        assert!(invalid.build().is_err(), "column {column}");
    }
    for (text, expected) in [("-1000", -1000), ("+001.00", 1), ("-0", 0)] {
        assert_eq!(
            export_doc_domain::invoice::parse_number(text).unwrap(),
            Decimal::from(expected)
        );
    }
    for text in ["--1", "1_0", "1e3", "-1000000001", "0.0000000000001"] {
        assert!(
            export_doc_domain::invoice::parse_number(text).is_err(),
            "{text}"
        );
    }
}
