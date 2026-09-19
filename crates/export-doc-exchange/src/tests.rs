use super::*;
use serde_json::json;
use std::sync::atomic::{AtomicUsize, Ordering};
const HTML: &str = r#"<html><table><tr><th>货币名称</th><th>现汇买入价</th><th>现钞买入价</th><th>现汇卖出价</th><th>现钞卖出价</th><th>中行折算价</th><th>发布日期</th></tr><tr><td>美元</td><td>713.21</td><td>--</td><td>716.02</td><td>717.33</td><td>712.50</td><td>2026-09-17 09:00:00</td></tr><tr><td>日元</td><td>4.8532</td><td>4.6</td><td>4.99</td><td></td><td>4.8</td><td>2026-09-17 09:00:00</td></tr></table></html>"#;
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
    let configuration = json!({"url":DEFAULT_URL,"cacheDurationMinutes":30,"selectedCurrencies":["日元","美元","日元"]});
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
    assert_eq!(cached.fetched_at, now);
    assert_eq!(service.fetch.calls.load(Ordering::SeqCst), 1);
    service
        .get(&configuration, true, false, now, &|| Ok(()))
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
        "https://www.boc.cn/?token=x",
        "https://www.boc.cn/#section",
        "https://[::ffff:192.168.1.1]/",
    ] {
        assert!(normalize(endpoint).is_err(), "{endpoint}");
    }
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
