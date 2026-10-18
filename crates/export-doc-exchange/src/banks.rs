//! Official public quotation adapters. The source page documents CNY / 100 FX;
//! no browser, login, undocumented middle rate or inferred timestamp is used.
use crate::{Error, ErrorKind, Format, Result, Source};
use export_doc_contracts::generated_api::ApiExchangeRateDto;
use rust_decimal::Decimal;
use serde_json::Value;
use std::{collections::BTreeSet, str::FromStr};

pub const ICBC_PAGE: &str = "https://www.icbc.com.cn/column/1438058341489590354.html";
pub const CMB_PAGE: &str = "https://fx.cmbchina.com/hq/";
pub const ICBC_API: &str = "https://papi.icbc.com.cn/exchanges/ns/getLatest";
const CMB_API: &str = "https://fx.cmbchina.com/api/v1/fx/rate";

pub fn validate_source(url: &str, format: Format) -> Result<()> {
    let expected = match format {
        Format::Icbc => ICBC_PAGE,
        Format::Cmb => CMB_PAGE,
        _ => return Ok(()),
    };
    if url.trim_end_matches('/') != expected.trim_end_matches('/') {
        return Err(Error::new(
            ErrorKind::Invalid,
            "银行预设须使用对应的官方牌价网址；其它地址请选择自定义来源。",
        ));
    }
    Ok(())
}

pub fn endpoint(source: &Source) -> &str {
    match source.format {
        Format::Icbc => ICBC_API,
        Format::Cmb => CMB_API,
        _ => &source.url,
    }
}

pub fn parse(body: &str, format: Format) -> Result<Vec<ApiExchangeRateDto>> {
    let invalid = || {
        Error::new(
            ErrorKind::Unavailable,
            "银行未返回可核验的有效牌价，请稍后重试或核对官方来源。",
        )
    };
    if body.len() > 5 * 1024 * 1024 {
        return Err(invalid());
    }
    let value: Value = serde_json::from_str(body).map_err(|_| invalid())?;
    let icbc = format == Format::Icbc;
    let rows = if icbc && value["code"] == 0 {
        &value["data"]
    } else if !icbc && value["returnCode"] == "SUC0000" {
        &value["body"]
    } else {
        return Err(invalid());
    };
    let rows = rows
        .as_array()
        .filter(|r| !r.is_empty() && r.len() <= 128)
        .ok_or_else(invalid)?;
    let mut names = BTreeSet::new();
    rows.iter()
        .map(|row| {
            let text = |key: &str| row[key].as_str().unwrap_or("").trim();
            let raw_name = text(if icbc { "currencyCHName" } else { "ccyNbr" });
            let name: String = raw_name.chars().filter(|c| !c.is_whitespace()).collect();
            let name = match name.as_str() {
                "韩元" => "韩国元",
                "马来西亚林吉特" => "林吉特",
                _ => &name,
            }
            .to_owned();
            if name.is_empty() || name.chars().count() > 80 || !names.insert(name.clone()) {
                return Err(invalid());
            }
            let stamp = format!(
                "{} {}",
                text(if icbc { "publishDate" } else { "ratDat" }),
                text(if icbc { "publishTime" } else { "ratTim" })
            );
            let date = chrono::NaiveDateTime::parse_from_str(
                &stamp,
                if icbc {
                    "%Y-%m-%d %H:%M:%S"
                } else {
                    "%Y年%m月%d日 %H:%M:%S"
                },
            )
            .map_err(|_| invalid())?;
            let price = |key: &str| -> Result<Option<Decimal>> {
                if !row[key].is_null() && !row[key].is_string() {
                    return Err(invalid());
                }
                let raw = text(key);
                if row[key].is_null() || ["", "--", "-"].contains(&raw) {
                    return Ok(None);
                }
                let number = Decimal::from_str(raw).map_err(|_| invalid())?;
                // Banks use zero for unavailable cash quotations.
                if number < Decimal::ZERO {
                    return Err(invalid());
                }
                Ok((number > Decimal::ZERO).then_some(number))
            };
            let rate = ApiExchangeRateDto {
                currency_name: name,
                buying_rate: price(if icbc { "foreignBuy" } else { "rthBid" })?,
                cash_buying_rate: price(if icbc { "cashBuy" } else { "rtcBid" })?,
                selling_rate: price(if icbc { "foreignSell" } else { "rthOfr" })?,
                cash_selling_rate: price(if icbc { "cashSell" } else { "rtcOfr" })?,
                middle_rate: None,
                publish_time: date
                    .and_local_timezone(chrono::FixedOffset::east_opt(8 * 3600).unwrap())
                    .single()
                    .ok_or_else(invalid)?
                    .to_rfc3339(),
                extra: Default::default(),
            };
            if [
                rate.buying_rate,
                rate.cash_buying_rate,
                rate.selling_rate,
                rate.cash_selling_rate,
            ]
            .iter()
            .all(Option::is_none)
            {
                return Err(invalid());
            }
            Ok(rate)
        })
        .collect()
}
