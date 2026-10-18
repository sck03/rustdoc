use crate::{Error, ErrorKind, Format, Result};
use export_doc_contracts::generated_api::ApiExchangeRateDto;
use rust_decimal::Decimal;
use scraper::{Html, Selector};
use std::{collections::BTreeSet, str::FromStr};

pub(crate) fn parse_source(body: &str, format: Format) -> Result<Vec<ApiExchangeRateDto>> {
    if matches!(format, Format::Icbc | Format::Cmb) {
        return crate::banks::parse(body, format);
    }
    if format == Format::BankOfChinaHtml
        || (format == Format::Auto && body.trim_start().starts_with('<'))
    {
        return parse_html(body, format == Format::Auto);
    }
    let invalid = || {
        Error::new(
            ErrorKind::Unavailable,
            "自定义汇率须为 JSON：baseCurrency 为 CNY、unit 为 100、rates 为有效报价数组。时间须含时区，价格须为正数。",
        )
    };
    if body.len() > 5 * 1024 * 1024 {
        return Err(invalid());
    }
    let value: serde_json::Value = serde_json::from_str(body).map_err(|_| invalid())?;
    if value["baseCurrency"] != "CNY" || value["unit"].as_u64() != Some(100) {
        return Err(invalid());
    }
    let rows = value["rates"]
        .as_array()
        .filter(|rows| !rows.is_empty() && rows.len() <= 128)
        .ok_or_else(invalid)?;
    let mut names = BTreeSet::new();
    rows.iter()
        .map(|row| {
            let mut rate: ApiExchangeRateDto =
                serde_json::from_value(row.clone()).map_err(|_| invalid())?;
            rate.currency_name = rate.currency_name.trim().to_owned();
            if rate.currency_name.is_empty()
                || rate.currency_name.chars().count() > 80
                || !names.insert(rate.currency_name.clone())
                || chrono::DateTime::parse_from_rfc3339(&rate.publish_time).is_err()
            {
                return Err(invalid());
            }
            let prices = [
                rate.buying_rate,
                rate.cash_buying_rate,
                rate.selling_rate,
                rate.cash_selling_rate,
                rate.middle_rate,
            ];
            if prices.iter().all(Option::is_none)
                || prices.iter().flatten().any(|p| *p <= Decimal::ZERO)
            {
                return Err(invalid());
            }
            rate.extra.clear();
            Ok(rate)
        })
        .collect()
}

pub fn parse(html: &str) -> Result<Vec<ApiExchangeRateDto>> {
    parse_html(html, false)
}

fn parse_html(html: &str, require_unit: bool) -> Result<Vec<ApiExchangeRateDto>> {
    if html.len() > 5 * 1024 * 1024 {
        return Err(Error::new(ErrorKind::Unavailable, "汇率页面超过 5 MiB。"));
    }
    let document = Html::parse_document(html);
    if require_unit {
        let text: String = document
            .root_element()
            .text()
            .flat_map(str::chars)
            .filter(|c| !c.is_whitespace())
            .collect();
        if ![
            "100外币换算人民币",
            "100外币兑换人民币",
            "人民币/100外币",
            "人民币／100外币",
        ]
        .iter()
        .any(|unit| text.contains(unit))
        {
            return Err(Error::new(
                ErrorKind::Unavailable,
                "无法确认网页报价单位。自动识别要求明确标注每 100 外币折算人民币，请选择有明确单位的数据来源。",
            ));
        }
    }
    let table_selector = Selector::parse("table").expect("constant selector");
    let row_selector = Selector::parse("tr").expect("constant selector");
    let cell_selector = Selector::parse("td, th").expect("constant selector");
    let mut rates: Vec<ApiExchangeRateDto> = vec![];
    let mut names = std::collections::BTreeMap::<String, usize>::new();
    for table in document.select(&table_selector) {
        let mut header: Option<Vec<String>> = None;
        for row in table.select(&row_selector).take(5000) {
            let cells: Vec<_> = row
                .select(&cell_selector)
                .map(|cell| {
                    cell.text()
                        .collect::<String>()
                        .replace('\u{a0}', " ")
                        .trim()
                        .to_owned()
                })
                .collect();
            if cells.iter().any(|cell| cell == "货币名称") {
                header = Some(cells);
                continue;
            }
            let Some(header) = header.as_ref() else {
                continue;
            };
            let cell = |name: &str| {
                header
                    .iter()
                    .position(|label| label == name)
                    .and_then(|i| cells.get(i))
                    .map(String::as_str)
                    .unwrap_or("")
            };
            let name = cell("货币名称");
            if name.is_empty() || name.chars().count() > 80 {
                continue;
            }
            let number = |name| {
                Decimal::from_str(&cell(name).replace(',', ""))
                    .ok()
                    .filter(|value| *value > Decimal::ZERO)
            };
            // Sources may supply a complete timestamp in the date column, or
            // split it across date/time columns. Do not append a second time.
            let combined = format!("{} {}", cell("发布日期"), cell("发布时间"));
            let published = [cell("发布日期"), combined.trim()].iter().find_map(|text| {
                [
                    "%Y.%m.%d %H:%M:%S",
                    "%Y-%m-%d %H:%M:%S",
                    "%Y/%m/%d %H:%M:%S",
                ]
                .iter()
                .find_map(|format| chrono::NaiveDateTime::parse_from_str(text, format).ok())
            });
            let Some(published) = published else {
                continue;
            };
            let publish_time = published
                .and_local_timezone(chrono::FixedOffset::east_opt(8 * 3600).unwrap())
                .single()
                .unwrap()
                .to_rfc3339();
            let rate = ApiExchangeRateDto {
                currency_name: name.to_owned(),
                buying_rate: number("现汇买入价"),
                cash_buying_rate: number("现钞买入价"),
                selling_rate: number("现汇卖出价"),
                cash_selling_rate: number("现钞卖出价"),
                middle_rate: number("中行折算价"),
                publish_time,
                extra: Default::default(),
            };
            if [
                rate.buying_rate,
                rate.cash_buying_rate,
                rate.selling_rate,
                rate.cash_selling_rate,
                rate.middle_rate,
            ]
            .iter()
            .any(Option::is_some)
            {
                if let Some(index) = names.get(&rate.currency_name) {
                    if rate.publish_time > rates[*index].publish_time {
                        rates[*index] = rate;
                    }
                } else {
                    names.insert(rate.currency_name.clone(), rates.len());
                    rates.push(rate);
                }
                if rates.len() > 128 {
                    return Err(Error::new(
                        ErrorKind::Unavailable,
                        "汇率源货币数量超过 128 种。",
                    ));
                }
            }
        }
    }
    if rates.is_empty() {
        return Err(Error::new(
            ErrorKind::Unavailable,
            "汇率源未返回有效的中国银行报价表，请检查源网址或稍后重试。",
        ));
    }
    Ok(rates)
}
