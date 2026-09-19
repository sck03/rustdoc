use super::*;
use crate::{DataRow, Exchange};
use export_doc_engine::workspace;
use serde_json::Value;
impl Desktop {
    pub fn exchange_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        if action == "currencies" {
            self.request(
                LIST_AVAILABLE_EXCHANGE_RATE_CURRENCIES,
                0,
                vec![],
                None,
                "exchange-currencies",
            );
        } else {
            self.request(
                LIST_EXCHANGE_RATES,
                0,
                vec![("forceRefresh", (action == "force").to_string())],
                None,
                "exchange-rates",
            );
        }
    }
    pub fn exchange_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let exchange = ui.global::<Exchange>();
        exchange.set_source(value["sourceUrl"].as_str().unwrap_or("").into());
        let fetched = value["fetchedAt"].as_str().unwrap_or("");
        let fetched = self
            .user
            .as_ref()
            .and_then(|user| {
                export_doc_engine::clock::BusinessClock::new(&user.business_time_zone).ok()
            })
            .and_then(|clock| clock.local_input(fetched).ok())
            .unwrap_or_else(|| fetched.into());
        exchange.set_fetched(format!("数据获取时间：{fetched}").into());
        let currency_key = if reply == "exchange-currencies" {
            "currencies"
        } else {
            "selectedCurrencies"
        };
        exchange.set_currencies(
            value[currency_key]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(Value::as_str)
                .collect::<Vec<_>>()
                .join("、")
                .into(),
        );
        if reply == "exchange-currencies" {
            self.status("可用币种已读取");
            return;
        }
        let rows = value["rates"].as_array().cloned().unwrap_or_default();
        exchange.set_summary(format!("{} 种货币", rows.len()).into());
        let app = ui.global::<App>();
        app.set_columns(model(
            [
                "货币",
                "现汇买入价",
                "现钞买入价",
                "现汇卖出价",
                "现钞卖出价",
                "中行折算价",
                "发布时间",
            ]
            .into_iter()
            .map(Into::into)
            .collect(),
        ));
        app.set_records(model(
            rows.iter()
                .enumerate()
                .map(|(index, row)| DataRow {
                    id: index as i32 + 1,
                    cells: model(
                        [
                            "currencyName",
                            "buyingRate",
                            "cashBuyingRate",
                            "sellingRate",
                            "cashSellingRate",
                            "middleRate",
                            "publishTime",
                        ]
                        .iter()
                        .map(|key| workspace::display(&row[*key]).into())
                        .collect(),
                    ),
                })
                .collect(),
        ));
        self.status(value["statusText"].as_str().unwrap_or("汇率已更新"));
    }
}
