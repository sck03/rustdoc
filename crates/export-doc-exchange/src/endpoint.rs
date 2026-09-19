use crate::{Error, ErrorKind, Result};
use std::net::IpAddr;
use url::Url;
pub const DEFAULT_URL: &str = "https://www.boc.cn/sourcedb/whpj/";
pub fn normalize(value: &str) -> Result<Url> {
    let value = if value.trim().is_empty() {
        DEFAULT_URL
    } else {
        value.trim()
    };
    let invalid = || {
        Error::new(
            ErrorKind::Invalid,
            "汇率源必须使用公开 HTTPS 地址，不能包含账号、查询参数、片段或内网地址。",
        )
    };
    let url = Url::parse(value).map_err(|_| invalid())?;
    if value.len() > 2048
        || url.scheme() != "https"
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
        || url.host_str().is_some_and(|host| {
            host.eq_ignore_ascii_case("localhost")
                || host
                    .trim_matches(['[', ']'])
                    .parse::<IpAddr>()
                    .is_ok_and(|ip| !public_address(ip))
        })
    {
        return Err(invalid());
    }
    Ok(url)
}
pub use export_doc_network::public_address;
