use super::*;
use chrono::Utc;
use serde_json::json;
use std::sync::atomic::{AtomicUsize, Ordering};
const HTML: &str = r#"<html><table><tr><th>货币名称</th><th>现汇买入价</th><th>现钞买入价</th><th>现汇卖出价</th><th>现钞卖出价</th><th>中行折算价</th><th>发布日期</th></tr><tr><td>美元</td><td>713.21</td><td>--</td><td>716.02</td><td>717.33</td><td>712.50</td><td>2026-09-17 09:00:00</td></tr><tr><td>日元</td><td>4.8532</td><td>4.6</td><td>4.99</td><td></td><td>4.8</td><td>2026-09-17 09:00:00</td></tr></table></html>"#;

#[test]
fn official_bank_adapters_preserve_prices_units_and_missing_quotations() {
    for (format, page, payload) in [
        (
            Format::Icbc,
            banks::ICBC_PAGE,
            json!({"code":0,"data":[{"currencyCHName":"日元","publishDate":"2026-10-18","publishTime":"10:20:30","foreignBuy":"4.3210","cashBuy":"0","foreignSell":"4.5678","cashSell":"4.6000"}]}),
        ),
        (
            Format::Cmb,
            banks::CMB_PAGE,
            json!({"returnCode":"SUC0000","body":[{"ccyNbr":"日元","ratDat":"2026年10月18日","ratTim":"10:20:30","rthBid":"4.3210","rtcBid":"--","rthOfr":"4.5678","rtcOfr":"4.6000"}]}),
        ),
    ] {
        let rates = banks::parse(&payload.to_string(), format).unwrap();
        assert_eq!(rates[0].buying_rate.unwrap().to_string(), "4.3210");
        assert!(rates[0].cash_buying_rate.is_none());
        assert!(rates[0].middle_rate.is_none());
        assert_eq!(rates[0].publish_time, "2026-10-18T10:20:30+08:00");
        assert!(banks::validate_source(page, format).is_ok());
        assert!(banks::validate_source("https://example.org/", format).is_err());
        let (rows, price) = if format == Format::Icbc {
            ("data", "foreignBuy")
        } else {
            ("body", "rthBid")
        };
        let mut invalid = payload.clone();
        invalid[rows][0][price] = json!(-1);
        assert!(banks::parse(&invalid.to_string(), format).is_err());
        invalid[rows][0][price] = json!("-1");
        assert!(banks::parse(&invalid.to_string(), format).is_err());
        invalid = payload;
        let duplicate = invalid[rows][0].clone();
        invalid[rows].as_array_mut().unwrap().push(duplicate);
        assert!(banks::parse(&invalid.to_string(), format).is_err());
    }
}
struct Fixture {
    calls: AtomicUsize,
}
impl Fetch for Fixture {
    fn html(&self, _: &str, check: Check<'_>) -> Result<String> {
        check()?;
        self.calls.fetch_add(1, Ordering::SeqCst);
        Ok(HTML.into())
    }
}
#[test]
fn exact_rates_cache_currency_order_refresh_and_cancellation_follow_the_contract() {
    let service = ExchangeRates::new(Fixture {
        calls: AtomicUsize::new(0),
    });
    let configuration = Configuration::read(&json!({"url":DEFAULT_URL,"cacheDurationMinutes":30,"selectedCurrencies":["日元","美元","日元"]})).unwrap();
    let now = Utc::now();
    let result = service
        .get(&configuration, false, false, now, &|| Ok(()))
        .unwrap();
    assert_eq!(result.rates[0].currency_name, "日元");
    assert_eq!(result.rates[0].buying_rate.unwrap().to_string(), "4.8532");
    assert!(result.rates[1].cash_buying_rate.is_none());
    let cached = service
        .get(
            &configuration,
            false,
            false,
            now + chrono::Duration::minutes(2),
            &|| Ok(()),
        )
        .unwrap();
    assert_eq!(cached.fetched_at, result.fetched_at);
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 1);
    service
        .get(
            &configuration,
            true,
            false,
            now + chrono::Duration::minutes(2),
            &|| Ok(()),
        )
        .unwrap();
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 2);
    assert!(
        service
            .get(&configuration, true, false, now, &|| Err(Error::new(
                ErrorKind::Cancelled,
                "取消"
            )))
            .is_err()
    );
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 2);
    assert!(parse("<table><tr><td>美元</td><td>713</td></tr></table>").is_err());
    let unrelated = format!(
        "{HTML}<table><tr><td>不是货币</td><td>123</td><td>123</td><td>123</td><td>123</td><td>123</td></tr></table>"
    );
    assert_eq!(parse(&unrelated).unwrap().len(), 2);
}
#[test]
fn public_https_policy_rejects_private_mapped_documentation_and_redirect_targets() {
    for endpoint in [
        "http://www.boc.cn/",
        "https://localhost/",
        "https://127.0.0.1/",
        "https://user:password@www.boc.cn/",
        "https://www.boc.cn/#section",
        "https://[::ffff:192.168.1.1]/",
    ] {
        assert!(normalize(endpoint).is_err(), "{endpoint}");
    }
    assert!(normalize("https://example.com/rates?base=CNY&unit=100").is_ok());
    for address in [
        "0.0.0.0",
        "10.1.1.1",
        "100.64.0.1",
        "172.16.1.1",
        "169.254.169.254",
        "192.0.2.1",
        "198.18.0.1",
        "203.0.113.1",
        "224.1.1.1",
        "::1",
        "fc00::1",
        "2001:db8::1",
        "2002::1",
    ] {
        assert!(!public_address(address.parse().unwrap()), "{address}");
    }
    for address in ["8.8.8.8", "1.1.1.1", "2001:4860:4860::8888"] {
        assert!(public_address(address.parse().unwrap()));
    }
}

fn configuration() -> Configuration {
    Configuration::read(&json!({"selectedCurrencies":["美元","日元"],"autoRefreshEnabled":true,"cacheDurationMinutes":30})).unwrap()
}

#[test]
fn failed_refresh_keeps_quotes_and_backs_off_even_for_forced_requests() {
    struct Changing(std::sync::atomic::AtomicBool, AtomicUsize);
    impl Fetch for Changing {
        fn html(&self, _: &str, _: Check<'_>) -> Result<String> {
            self.1.fetch_add(1, Ordering::SeqCst);
            if self.0.load(Ordering::SeqCst) {
                Err(Error::new(ErrorKind::Unavailable, "source offline"))
            } else {
                Ok(HTML.into())
            }
        }
    }
    let service = ExchangeRates::new(Changing(false.into(), 0.into()));
    let config = configuration();
    let now = Utc::now();
    let initial = service.get(&config, false, false, now, &|| Ok(())).unwrap();
    service.fetch.0.store(true, Ordering::SeqCst);
    let failed_at = now + chrono::Duration::minutes(31);
    let stale = service
        .get(&config, true, false, failed_at, &|| Ok(()))
        .unwrap();
    assert!(stale.is_stale);
    assert_eq!(stale.fetched_at, initial.fetched_at);
    assert_eq!(stale.last_error, "source offline");
    assert_eq!(stale.rates.len(), 2);
    assert!(stale.next_refresh_at.unwrap() >= failed_at + chrono::Duration::minutes(5));
    service
        .get(
            &config,
            true,
            true,
            failed_at + chrono::Duration::minutes(1),
            &|| Ok(()),
        )
        .unwrap();
    assert_eq!(service.fetch.1.load(Ordering::SeqCst), 2);
    let second = service
        .get(
            &config,
            false,
            false,
            stale.next_refresh_at.unwrap(),
            &|| Ok(()),
        )
        .unwrap();
    assert!(
        second.next_refresh_at.unwrap()
            >= stale.next_refresh_at.unwrap() + chrono::Duration::minutes(10)
    );
    service.fetch.0.store(false, Ordering::SeqCst);
    let recovered = service
        .get(
            &config,
            false,
            false,
            second.next_refresh_at.unwrap(),
            &|| Ok(()),
        )
        .unwrap();
    assert!(!recovered.is_stale);
    assert!(recovered.last_error.is_empty());
    assert!(recovered.fetched_at > initial.fetched_at);
}

#[test]
fn empty_selection_missing_currencies_and_configuration_changes_use_one_snapshot() {
    let service = ExchangeRates::new(Fixture {
        calls: AtomicUsize::new(0),
    });
    let mut config = configuration();
    let now = Utc::now();
    service.get(&config, false, false, now, &|| Ok(())).unwrap();
    config.selected = vec!["欧元".into(), "日元".into()];
    let quote = service.get(&config, false, false, now, &|| Ok(())).unwrap();
    assert_eq!(quote.missing, ["欧元"]);
    assert_eq!(quote.rates.len(), 1);
    config.selected.clear();
    let quote = service.get(&config, false, false, now, &|| Ok(())).unwrap();
    assert!(quote.rates.is_empty());
    assert_eq!(quote.available.len(), 2);
    config.minutes = 60;
    assert_eq!(
        service.next_update(&config, now).unwrap(),
        quote.fetched_at + chrono::Duration::minutes(60)
    );
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 1);
    config.source.format = Format::Json;
    assert!(
        service.get(&config, true, false, now, &|| Ok(())).is_err(),
        "a new source must never expose the previous source's cache"
    );
}

#[test]
fn concurrent_manual_refreshes_are_coalesced_and_cancellation_does_not_fetch() {
    let service = ExchangeRates::new(Fixture {
        calls: AtomicUsize::new(0),
    });
    let config = configuration();
    let start = std::sync::Barrier::new(8);
    let now = Utc::now();
    std::thread::scope(|scope| {
        for _ in 0..8 {
            scope.spawn(|| {
                start.wait();
                service.get(&config, true, false, now, &|| Ok(())).unwrap();
            });
        }
    });
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 1);
    assert!(
        service
            .get(&config, true, true, now, &|| Err(Error::new(
                ErrorKind::Cancelled,
                "cancelled"
            )))
            .is_err()
    );
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 1);
}

#[test]
fn json_and_auto_parsing_require_explicit_units_exact_prices_and_offset_times() {
    let value = json!({"baseCurrency":"CNY","unit":100,"rates":[{"currencyName":"美元","buyingRate":"700.12345678","publishTime":"2026-10-17T10:00:00+08:00"}]});
    for format in [Format::Json, Format::Auto] {
        let rates = parser::parse_source(&value.to_string(), format).unwrap();
        assert_eq!(rates[0].buying_rate.unwrap().to_string(), "700.12345678");
    }
    for (pointer, invalid) in [
        ("/unit", json!(1)),
        ("/baseCurrency", json!("USD")),
        ("/rates/0/buyingRate", json!(-1)),
        ("/rates/0/publishTime", json!("2026-10-17 10:00:00")),
    ] {
        let mut bad = value.clone();
        *bad.pointer_mut(pointer).unwrap() = invalid;
        assert!(
            parser::parse_source(&bad.to_string(), Format::Auto).is_err(),
            "{pointer}"
        );
    }
    assert!(parser::parse_source(HTML, Format::Auto).is_err());
    assert_eq!(
        parser::parse_source(
            &format!("<p>单位：100外币换算人民币</p>{HTML}"),
            Format::Auto
        )
        .unwrap()
        .len(),
        2
    );
}

#[test]
fn html_uses_named_columns_and_keeps_the_latest_published_quote() {
    let reordered = "<table><tr><th>货币名称</th><th>中行折算价</th><th>发布日期</th><th>现汇卖出价</th></tr><tr><td>美元</td><td>701</td><td>2026.10.17 10:00:00</td><td>702</td></tr><tr><td>美元</td><td>711</td><td>2026.10.17 11:00:00</td><td>712</td></tr></table>";
    let rate = &parse(reordered).unwrap()[0];
    assert_eq!(rate.middle_rate.unwrap().to_string(), "711");
    assert_eq!(rate.selling_rate.unwrap().to_string(), "712");
    assert!(rate.buying_rate.is_none());
    assert_eq!(rate.publish_time, "2026-10-17T11:00:00+08:00");
    assert!(parse(&reordered.replace("2026.10.17", "not-a-date")).is_err());
    for date in ["2026/10/17", "2026/10/17 10:30:00"] {
        let table = format!(
            "<table><thead><tr><th>货币名称</th><th>现汇买入价</th><th>发布日期</th><th>发布时间</th></tr></thead><tr><td>美元</td><td>700.25</td><td class='pjrq'>{date}</td><td>10:30:00</td></tr></table>"
        );
        assert_eq!(
            parse(&table).unwrap()[0].publish_time,
            "2026-10-17T10:30:00+08:00"
        );
    }
}

#[test]
fn automatic_interval_and_source_configuration_are_validated_before_use() {
    for minutes in [0, 1, 29, 1441] {
        assert!(
            Configuration::read(&json!({"autoRefreshEnabled":true,"cacheDurationMinutes":minutes}))
                .is_err()
        );
    }
    for minutes in [30, 60, 120, 1440] {
        assert!(
            Configuration::read(&json!({"autoRefreshEnabled":true,"cacheDurationMinutes":minutes}))
                .is_ok()
        );
    }
    assert!(Configuration::read(&json!({"url":"http://localhost/rates"})).is_err());
    assert!(Configuration::read(&json!({"sourceFormat":"Unknown"})).is_err());
    assert!(Configuration::read(&json!({"selectedCurrencies":vec!["a";16]})).is_err());
    for value in [
        json!(null),
        json!({"url":null}),
        json!({"sourceFormat":42}),
        json!({"cacheDurationMinutes":-1}),
        json!({"cacheDurationMinutes":1.5}),
        json!({"autoRefreshEnabled":"true"}),
        json!({"selectedCurrencies":"美元"}),
    ] {
        assert!(Configuration::read(&value).is_err(), "{value}");
    }
}
