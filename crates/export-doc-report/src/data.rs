use crate::{Result, error::invalid};
use base64::{Engine, engine::general_purpose::STANDARD};
use export_doc_contracts::generated_api::{ApiInvoiceDetailDto, ApiPaymentDto};
use rust_decimal::{Decimal, prelude::ToPrimitive};
use serde_json::{Value, json};
use std::collections::BTreeMap;

/// Bytes have already passed the host's bounded image and ownership checks.
/// Neither this model nor the renderer resolves URLs or filesystem paths.
#[derive(Clone)]
pub struct RasterImage {
    pub media_type: String,
    pub bytes: Vec<u8>,
}
impl RasterImage {
    pub fn data_url(&self) -> Result<String> {
        if !["image/png", "image/jpeg"].contains(&self.media_type.as_str())
            || self.bytes.is_empty()
            || self.bytes.len() > 32 * 1024 * 1024
        {
            return Err(invalid("报表图片类型或容量无效。"));
        }
        Ok(format!(
            "data:{};base64,{}",
            self.media_type,
            STANDARD.encode(&self.bytes)
        ))
    }
}

#[derive(Clone)]
pub struct ReportData {
    pub report_type: &'static str,
    pub root: Value,
    pub images: BTreeMap<String, RasterImage>,
}
impl ReportData {
    pub fn invoice(
        invoice: &ApiInvoiceDetailDto,
        customer: Value,
        exporter: Value,
        with_seal: bool,
    ) -> Result<Self> {
        let value = serde_json::to_value(invoice)?;
        let mut data = Self {
            report_type: "ExportDocument",
            root: json!({"Invoice":value,"Customer":customer,"Exporter":exporter,"ShowSeal":with_seal,"withSeal":with_seal}),
            images: BTreeMap::new(),
        };
        // A saved invoice carries its party names and addresses; absent master
        // records do not erase those document facts.
        for (group, mappings) in [
            (
                "Customer",
                &[
                    ("CustomerNameEN", "customerNameEN"),
                    ("CustomerNameCN", "customerNameCN"),
                    ("AddressEN", "customerAddressEN"),
                    ("AddressCN", "customerAddressCN"),
                ][..],
            ),
            (
                "Exporter",
                &[
                    ("ExporterNameEN", "exporterNameEN"),
                    ("ExporterNameCN", "exporterNameCN"),
                    ("AddressEN", "exporterAddressEN"),
                    ("AddressCN", "exporterAddressCN"),
                    ("CreditCode", "exporterCreditCode"),
                    ("CustomsCode", "exporterCustomsCode"),
                ][..],
            ),
        ] {
            if !data.root[group].is_object() {
                data.root[group] = json!({});
            }
            for (target, source) in mappings {
                let snapshot = data.root["Invoice"][source].clone();
                if snapshot.as_str().is_some_and(|v| !v.is_empty()) {
                    let object = data.root[group].as_object_mut().unwrap();
                    let key = object
                        .keys()
                        .find(|key| key.eq_ignore_ascii_case(target))
                        .cloned()
                        .unwrap_or_else(|| (*target).into());
                    object.insert(key, snapshot);
                }
            }
        }
        data.root["items"] = value_at(&data.root["Invoice"], "Items").clone();
        data.root["total_amount_words"] =
            json!(format!("{} ONLY", english_money(invoice.total_amount)?));
        for (name, quantity, unit) in [
            ("total_by_ctn_unit", "Cartons", "CtnUnitEN"),
            ("total_by_qty_unit", "Quantity", "UnitEN"),
        ] {
            let mut groups = BTreeMap::<String, Decimal>::new();
            for item in data.items() {
                let number = decimal(value_at(item, quantity))?;
                let key = plain(value_at(item, unit));
                let total = groups.entry(key).or_default();
                *total = total
                    .checked_add(number)
                    .ok_or_else(|| invalid("报表分组合计超出范围。"))?;
            }
            data.root[name] = json!(
                groups
                    .into_iter()
                    .map(|(key, value)| json!({"Key":key,"Value":value}))
                    .collect::<Vec<_>>()
            );
        }
        Ok(data)
    }

    pub fn payment(payment: &ApiPaymentDto, payee: Value) -> Result<Self> {
        Ok(Self {
            report_type: "PaymentVoucher",
            root: json!({"Payment":payment,"Payee":payee,"cny_amount_upper":chinese_money(payment.cny_amount)?}),
            images: BTreeMap::new(),
        })
    }
    pub fn value<'a>(&'a self, path: &str, item: Option<&'a Value>) -> &'a Value {
        let (group, rest) = path.split_once('.').unwrap_or((path, ""));
        let mut value = if group.eq_ignore_ascii_case("item") {
            item.unwrap_or(&Value::Null)
        } else {
            value_at(&self.root, group)
        };
        if !rest.is_empty() {
            for key in rest.split('.') {
                value = value_at(value, key);
            }
        }
        value
    }
    pub fn text(&self, path: &str) -> String {
        plain(self.value(path, None))
    }
    pub fn item_text(&self, item: &Value, field: &str) -> String {
        plain(value_at(item, field))
    }
    pub(crate) fn date(&self, path: &str, chinese: bool) -> Result<String> {
        let value = self.text(path);
        if value.is_empty() {
            return Ok(value);
        }
        if !export_doc_domain::invoice::valid_date(&value) {
            return Err(invalid("报表日期必须是有效的业务自然日。"));
        }
        let (year, month, day) = (&value[..4], &value[5..7], &value[8..10]);
        Ok(if chinese {
            format!("{year}年{month}月{day}日")
        } else {
            format!("{month}/{day}/{year}")
        })
    }
    pub fn number(&self, path: &str, precision: u32) -> Result<String> {
        Ok(format_decimal(decimal(self.value(path, None))?, precision))
    }
    pub fn item_number(&self, item: &Value, field: &str, precision: u32) -> Result<String> {
        Ok(format_decimal(decimal(value_at(item, field))?, precision))
    }
    pub fn items(&self) -> &[Value] {
        self.root["items"]
            .as_array()
            .map(Vec::as_slice)
            .unwrap_or(&[])
    }
    pub fn unit_totals(&self, key: &str) -> String {
        self.root[key]
            .as_array()
            .map(|items| {
                items
                    .iter()
                    .map(|v| format!("{}{}", plain(&v["Value"]), plain(&v["Key"])))
                    .collect::<Vec<_>>()
                    .join("; ")
            })
            .unwrap_or_default()
    }
}

pub(crate) fn value_at<'a>(value: &'a Value, key: &str) -> &'a Value {
    value
        .as_object()
        .and_then(|v| {
            v.iter()
                .find(|(name, _)| name.eq_ignore_ascii_case(key))
                .map(|(_, v)| v)
        })
        .unwrap_or(&Value::Null)
}
pub(crate) fn plain(value: &Value) -> String {
    match value {
        Value::String(v) => v.clone(),
        Value::Number(v) => v.to_string(),
        Value::Bool(v) => if *v { "是" } else { "否" }.into(),
        _ => String::new(),
    }
}
pub(crate) fn decimal(value: &Value) -> Result<Decimal> {
    if value.is_null() || value.as_str() == Some("") {
        return Ok(Decimal::ZERO);
    }
    serde_json::from_value(value.clone()).map_err(|_| invalid("报表金额或数量不是有效十进制数。"))
}
pub(crate) fn format_decimal(value: Decimal, precision: u32) -> String {
    format!(
        "{:.*}",
        precision as usize,
        value.round_dp_with_strategy(
            precision,
            rust_decimal::RoundingStrategy::MidpointAwayFromZero
        )
    )
}

pub fn english_money(value: Decimal) -> Result<String> {
    use export_doc_domain::number::english_integer as whole;
    let cents = value
        .abs()
        .round_dp_with_strategy(2, rust_decimal::RoundingStrategy::MidpointAwayFromZero)
        .checked_mul(Decimal::from(100))
        .and_then(|v| v.to_u64())
        .ok_or_else(|| invalid("大写金额超出范围。"))?;
    let integer = cents / 100;
    let mut result = if integer == 0 {
        "ZERO".into()
    } else {
        whole(integer)
    };
    if cents % 100 > 0 {
        result.push_str(&format!(" AND CENTS {}", whole(cents % 100)));
    }
    if value.is_sign_negative() && cents > 0 {
        result.insert_str(0, "MINUS ");
    }
    Ok(result)
}

pub fn chinese_money(value: Decimal) -> Result<String> {
    const DIGITS: [&str; 10] = ["零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖"];
    fn section(mut number: u64) -> String {
        let mut result = String::new();
        let mut zero = false;
        for (base, label) in [(1000, "仟"), (100, "佰"), (10, "拾"), (1, "")] {
            let digit = number / base;
            number %= base;
            if digit == 0 {
                if !result.is_empty() && number > 0 {
                    zero = true;
                }
            } else {
                if zero {
                    result.push_str("零");
                    zero = false;
                }
                result.push_str(DIGITS[digit as usize]);
                result.push_str(label);
            }
        }
        result
    }
    let cents = value
        .abs()
        .round_dp_with_strategy(2, rust_decimal::RoundingStrategy::MidpointAwayFromZero)
        .checked_mul(Decimal::from(100))
        .and_then(|v| v.to_u64())
        .ok_or_else(|| invalid("人民币大写金额超出范围。"))?;
    if cents == 0 {
        return Ok("零元整".into());
    }
    let integer = cents / 100;
    let mut remainder = integer;
    let mut result = String::new();
    let mut zero = false;
    for (base, label) in [
        (10_000_000_000_000_000, "京"),
        (1_000_000_000_000, "兆"),
        (100_000_000, "亿"),
        (10_000, "万"),
        (1, ""),
    ] {
        let group = remainder / base;
        remainder %= base;
        if group == 0 {
            if !result.is_empty() && remainder > 0 {
                zero = true;
            }
            continue;
        }
        if !result.is_empty() && (zero || group < 1000) {
            result.push_str("零");
        }
        result.push_str(&section(group));
        result.push_str(label);
        zero = false;
    }
    if integer > 0 {
        result.push_str("元");
    }
    let jiao = (cents % 100) / 10;
    let fen = cents % 10;
    if jiao == 0 && fen == 0 {
        result.push_str("整");
    } else {
        if jiao > 0 {
            result.push_str(DIGITS[jiao as usize]);
            result.push_str("角");
        } else if integer > 0 {
            result.push_str("零");
        }
        if fen > 0 {
            result.push_str(DIGITS[fen as usize]);
            result.push_str("分");
        }
    }
    if value.is_sign_negative() {
        result.insert_str(0, "负");
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn written_amounts_keep_group_zeroes_and_round_without_float() {
        for (input, expected) in [
            ("0", "零元整"),
            ("0.05", "伍分"),
            ("10001.01", "壹万零壹元零壹分"),
            ("100100010.05", "壹亿零壹拾万零壹拾元零伍分"),
            ("-21.16", "负贰拾壹元壹角陆分"),
            ("1.999", "贰元整"),
        ] {
            assert_eq!(chinese_money(input.parse().unwrap()).unwrap(), expected);
        }
        assert_eq!(
            english_money("1250.26".parse().unwrap()).unwrap(),
            "ONE THOUSAND TWO HUNDRED AND FIFTY AND CENTS TWENTY-SIX"
        );
    }
}
