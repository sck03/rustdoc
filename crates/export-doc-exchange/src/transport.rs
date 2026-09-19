use crate::{Check, Error, ErrorKind, Result, normalize};
use export_doc_network::AllowedResolver;
use std::{io::Read, time::Duration};
use ureq::unversioned::transport::DefaultConnector;

pub trait Fetch: Send + Sync {
    fn html(&self, url: &str, check: Check<'_>) -> Result<String>;
}
pub struct Https {
    agent: ureq::Agent,
}
impl Default for Https {
    fn default() -> Self {
        let config = ureq::Agent::config_builder()
            .proxy(None)
            .max_redirects(0)
            .http_status_as_error(false)
            .timeout_global(Some(Duration::from_secs(30)))
            .timeout_resolve(Some(Duration::from_secs(5)))
            .timeout_connect(Some(Duration::from_secs(5)))
            .timeout_recv_response(Some(Duration::from_secs(10)))
            .timeout_recv_body(Some(Duration::from_secs(10)))
            .tls_config(
                ureq::tls::TlsConfig::builder()
                    .provider(ureq::tls::TlsProvider::NativeTls)
                    .root_certs(ureq::tls::RootCerts::PlatformVerifier)
                    .build(),
            )
            .build();
        Self {
            agent: ureq::Agent::with_parts(
                config,
                DefaultConnector::new(),
                AllowedResolver {
                    allow_explicit_loopback: false,
                },
            ),
        }
    }
}
impl Fetch for Https {
    fn html(&self, url: &str, check: Check<'_>) -> Result<String> {
        let mut endpoint = normalize(url)?;
        for redirect in 0..=5 {
            check()?;
            let mut response = self
                .agent
                .get(endpoint.as_str())
                .header("User-Agent", "ExportDocManager/0.1 (exchange rate reader)")
                .call()
                .map_err(|error| {
                    Error::new(
                        if matches!(error, ureq::Error::Timeout(_)) {
                            ErrorKind::Timeout
                        } else {
                            ErrorKind::Unavailable
                        },
                        format!("汇率源连接失败：{error}"),
                    )
                })?;
            if response.status().is_redirection() {
                if redirect == 5 {
                    return Err(Error::new(ErrorKind::Unavailable, "汇率源重定向超过上限。"));
                }
                let location = response
                    .headers()
                    .get("location")
                    .and_then(|value| value.to_str().ok())
                    .ok_or_else(|| {
                        Error::new(ErrorKind::Unavailable, "汇率源重定向缺少有效地址。")
                    })?;
                endpoint = normalize(
                    endpoint
                        .join(location)
                        .map_err(|_| Error::new(ErrorKind::Invalid, "汇率跳转地址无效。"))?
                        .as_str(),
                )?;
                continue;
            }
            if !response.status().is_success() {
                return Err(Error::new(
                    ErrorKind::Unavailable,
                    format!("汇率源返回 HTTP {}。", response.status().as_u16()),
                ));
            }
            let mut bytes = vec![];
            let mut buffer = [0u8; 16 * 1024];
            let mut reader = response.body_mut().as_reader();
            loop {
                check()?;
                let count = reader.read(&mut buffer).map_err(|error| {
                    Error::new(ErrorKind::Unavailable, format!("汇率页面读取失败：{error}"))
                })?;
                if count == 0 {
                    break;
                }
                if bytes.len() + count > 5 * 1024 * 1024 {
                    return Err(Error::new(ErrorKind::Unavailable, "汇率页面超过 5 MiB。"));
                }
                bytes.extend_from_slice(&buffer[..count]);
            }
            return String::from_utf8(bytes).map_err(|_| {
                Error::new(ErrorKind::Unavailable, "汇率页面不是有效的 UTF-8 文本。")
            });
        }
        Err(Error::new(ErrorKind::Unavailable, "汇率源不可用。"))
    }
}
