use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::str::FromStr;

#[test]
fn generated_decimal_fields_use_text_and_reject_numeric_request_values() {
    use export_doc_contracts::{contracts, generated_api::ApiInvoiceItemDto, validation};
    for source in ["0", "0.1", "12345678901234567890.12345678", "-999.001"] {
        let amount = Decimal::from_str(source).unwrap();
        let item = ApiInvoiceItemDto {
            unit_price: amount,
            ..Default::default()
        };
        let json = serde_json::to_string(&item).unwrap();
        let value: Value = serde_json::from_str(&json).unwrap();
        assert_eq!(value["unitPrice"].as_str().unwrap(), amount.to_string());
        let roundtrip: ApiInvoiceItemDto = serde_json::from_value(value).unwrap();
        assert_eq!(roundtrip.unit_price, amount);
        let schema = contracts::schema("ApiInvoiceItemDto");
        assert!(validation::provided_fields(schema, &json!({"unitPrice":source})).is_ok());
        assert!(validation::provided_fields(schema, &json!({"unitPrice":amount})).is_err());
    }
}

#[test]
fn decimal_text_rejects_non_contract_syntax_and_precision_loss() {
    use export_doc_contracts::{
        contracts,
        generated_api::{ApiInvoiceDetailDto, ApiInvoiceItemDto},
        validation,
    };
    for source in [
        "1e3",
        "1_000",
        "1.00000000000000000000000000001",
        "79228162514264337593543950336",
    ] {
        let mut item = serde_json::to_value(ApiInvoiceItemDto::default()).unwrap();
        item["unitPrice"] = json!(source);
        assert!(
            serde_json::from_value::<ApiInvoiceItemDto>(item).is_err(),
            "wire accepted {source}"
        );
        assert!(
            validation::provided_fields(
                contracts::schema("ApiInvoiceItemDto"),
                &json!({"unitPrice":source})
            )
            .is_err()
        );
    }
    let mut invoice = serde_json::to_value(ApiInvoiceDetailDto::default()).unwrap();
    invoice["exchangeRate"] = Value::Null;
    assert!(
        serde_json::from_value::<ApiInvoiceDetailDto>(invoice.clone())
            .unwrap()
            .exchange_rate
            .is_none()
    );
    invoice["exchangeRate"] = json!("+0001.250");
    assert_eq!(
        serde_json::from_value::<ApiInvoiceDetailDto>(invoice.clone())
            .unwrap()
            .exchange_rate
            .unwrap(),
        Decimal::new(1250, 3)
    );
    invoice["exchangeRate"] = json!("");
    assert!(serde_json::from_value::<ApiInvoiceDetailDto>(invoice).is_err());
}
