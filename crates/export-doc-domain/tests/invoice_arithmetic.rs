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
