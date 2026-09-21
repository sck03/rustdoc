//! Public third-party reference pages are evidence, never authoritative tariffs.
pub mod lookup;
pub mod parser;
use export_doc_contracts::generated_api::ApiHsCodeDto;
use std::{io::Read, time::Duration};
pub const SOURCE: &str = "i5a6";
const ORIGIN: &str = "https://www.i5a6.com";
const MAX_PAGE: usize = 4 * 1024 * 1024;
// Match the original provider's public-page negotiation. The source rejects
// the product-only user agent with HTTP 403, even for static search pages.
const USER_AGENT: &str = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

pub fn trusted_url(value: &str) -> Result<url::Url, String> {
    let url = url::Url::parse(ORIGIN)
        .unwrap()
        .join(value)
        .map_err(|_| "参考页面地址无效。")?;
    if url.scheme() != "https"
        || url.host_str() != Some("www.i5a6.com")
        || url.port_or_known_default() != Some(443)
        || !url.username().is_empty()
        || url.password().is_some()
        || !url.path().starts_with("/hscode/")
    {
        return Err("只允许读取受控的 i5a6 HS 参考页面。".into());
    }
    Ok(url)
}
fn read(url: url::Url, check: &dyn Fn() -> Result<(), String>) -> Result<String, String> {
    check()?;
    let agent: ureq::Agent = ureq::Agent::config_builder()
        .timeout_global(Some(Duration::from_secs(20)))
        .max_redirects(0)
        .tls_config(
            ureq::tls::TlsConfig::builder()
                .provider(ureq::tls::TlsProvider::NativeTls)
                .root_certs(ureq::tls::RootCerts::PlatformVerifier)
                .build(),
        )
        .build()
        .into();
    let response = agent
        .get(url.as_str())
        .header("User-Agent", USER_AGENT)
        .header("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7")
        .call()
        .map_err(|e| format!("参考数据源访问失败：{e}"))?;
    let mut reader = response.into_body().into_reader();
    let mut bytes = vec![];
    let mut buffer = [0_u8; 16 * 1024];
    loop {
        check()?;
        let count = reader
            .read(&mut buffer)
            .map_err(|e| format!("参考页面读取失败：{e}"))?;
        if count == 0 {
            break;
        }
        if bytes.len() + count > MAX_PAGE {
            return Err("参考页面超过 4 MiB。".into());
        }
        bytes.extend_from_slice(&buffer[..count]);
    }
    String::from_utf8(bytes).map_err(|_| "参考页面不是有效的 UTF-8 文本。".into())
}
pub fn search(
    query: &str,
    observed: &str,
    check: &dyn Fn() -> Result<(), String>,
) -> Result<parser::SearchBundle, String> {
    let query = query.trim();
    if query.is_empty() || query.chars().count() > 500 {
        return Err("请输入 1 至 500 字的检索条件。".into());
    }
    let mut url = url::Url::parse(ORIGIN).unwrap();
    url.path_segments_mut()
        .map_err(|_| "参考地址无效")?
        .extend(["hscode", "key", query]);
    let html = read(url, check)?;
    let bundle = parser::search(&html, observed)?;
    if bundle.records.is_empty() && !parser::empty_result(&html) {
        return Err("参考页面没有可读取的静态结果，数据源可能需要交互验证或已改变格式。".into());
    }
    Ok(bundle)
}
pub fn detail(
    seed: &ApiHsCodeDto,
    observed: &str,
    check: &dyn Fn() -> Result<(), String>,
) -> Result<parser::DetailBundle, String> {
    let url = trusted_url(&seed.detail_url)?;
    if !url.path().starts_with("/hscode/detail/") {
        return Err("请选择有效的 HS 详情页面。".into());
    }
    parser::detail(&read(url, check)?, seed, observed)
}
pub fn health(check: &dyn Fn() -> Result<(), String>) -> Result<(), String> {
    read(trusted_url("/hscode/")?, check).map(|_| ())
}
