//! Exact decimal text at DTO and input boundaries; no scientific notation or rounding.
use rust_decimal::Decimal;
use serde::{Deserialize, Deserializer, de::Error};

pub use rust_decimal::serde::str::serialize;
const INVALID: &str = "请输入普通十进制数字，且不得超过可表示精度。";

pub fn parse(text: &str) -> Option<Decimal> {
    let unsigned = text.strip_prefix(['-', '+']).unwrap_or(text);
    let (whole, fraction) = unsigned
        .split_once('.')
        .map_or((unsigned, None), |(whole, fraction)| {
            (whole, Some(fraction))
        });
    if whole.is_empty()
        || !whole.bytes().all(|b| b.is_ascii_digit())
        || fraction.is_some_and(|v| v.is_empty() || !v.bytes().all(|b| b.is_ascii_digit()))
    {
        return None;
    }
    Decimal::from_str_exact(text).ok()
}

pub fn deserialize<'de, D: Deserializer<'de>>(deserializer: D) -> Result<Decimal, D::Error> {
    parse(&String::deserialize(deserializer)?).ok_or_else(|| D::Error::custom(INVALID))
}

pub mod optional {
    use super::*;
    pub use rust_decimal::serde::str_option::serialize;

    pub fn deserialize<'de, D: Deserializer<'de>>(
        deserializer: D,
    ) -> Result<Option<Decimal>, D::Error> {
        Option::<String>::deserialize(deserializer)?
            .map(|text| parse(&text).ok_or_else(|| D::Error::custom(INVALID)))
            .transpose()
    }
}
