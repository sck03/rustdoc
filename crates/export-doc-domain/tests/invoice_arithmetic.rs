use export_doc_domain::{generated_api::ApiInvoiceItemDto, invoice_calculation};
use rust_decimal::Decimal;
use serde_json::Value;
use std::str::FromStr;

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
