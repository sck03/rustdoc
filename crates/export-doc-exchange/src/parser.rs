use crate::{Error, ErrorKind, Result};
use export_doc_contracts::generated_api::ApiExchangeRateDto;
use rust_decimal::Decimal;
use scraper::{Html, Selector};
use std::{collections::BTreeSet, str::FromStr};

pub fn parse(html: &str) -> Result<Vec<ApiExchangeRateDto>> {
    if html.len() > 5 * 1024 * 1024 {
        return Err(Error::new(ErrorKind::Unavailable, "汇率页面超过 5 MiB。"));
    }
    let document = Html::parse_document(html);
    let table_selector = Selector::parse("table").expect("constant selector");
    let row_selector = Selector::parse("tr").expect("constant selector");
    let cell_selector = Selector::parse("td, th").expect("constant selector");
    let mut rates = vec![];
    let mut names = BTreeSet::new();
    for table in document.select(&table_selector) {
        let mut valid_header = false;
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
            if cells.len() < 6 {
                continue;
            }
            if cells[0] == "货币名称" {
                valid_header = cells[1].contains("现汇买入")
                    && cells[2].contains("现钞买入")
                    && cells[3].contains("现汇卖出");
                continue;
            }
            if !valid_header
                || cells[0].is_empty()
                || cells[0].chars().count() > 80
                || names.contains(&cells[0])
            {
                continue;
            }
            let number = |index: usize| {
                Decimal::from_str(&cells[index].replace(',', ""))
                    .ok()
                    .filter(|value| *value > Decimal::ZERO)
            };
            let rate = ApiExchangeRateDto {
                currency_name: cells[0].clone(),
                buying_rate: number(1),
                cash_buying_rate: number(2),
                selling_rate: number(3),
                cash_selling_rate: number(4),
                middle_rate: number(5),
                publish_time: cells
                    .iter()
                    .skip(6)
                    .map(String::as_str)
                    .collect::<Vec<_>>()
                    .join(" "),
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
                names.insert(rate.currency_name.clone());
                rates.push(rate);
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
