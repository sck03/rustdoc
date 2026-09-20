use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::str::FromStr;

#[test]
fn wire_amounts_are_json_numbers_without_binary_float_rounding() {
    // React's generated API requires number, including exact zero comparisons
    // used by dashboard trends. Rust must keep all decimal digits on the wire.
    for source in ["0", "0.1", "12345678901234567890.12345678", "-999.001"] {
        let amount = Decimal::from_str(source).unwrap();
        let json = serde_json::to_string(&json!({"amount":amount})).unwrap();
        let value: Value = serde_json::from_str(&json).unwrap();
        assert!(value["amount"].is_number(), "{json}");
        assert_eq!(value["amount"].to_string(), amount.to_string());
        let roundtrip: Decimal = serde_json::from_value(value["amount"].clone()).unwrap();
        assert_eq!(roundtrip, amount);
    }
}
