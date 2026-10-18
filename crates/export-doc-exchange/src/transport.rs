use crate::{Check, Error, ErrorKind, Result, normalize};
use export_doc_network::AllowedResolver;
use std::{
    io::Read,
    time::{Duration, Instant},
};
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
        let started = Instant::now();
        for redirect in 0..=5 {
            check()?;
            let remaining = Duration::from_secs(30)
                .checked_sub(started.elapsed())
                .filter(|duration| !duration.is_zero())
                .ok_or_else(|| Error::new(ErrorKind::Timeout, "汇率获取超过 30 秒。"))?;
            // ICBC's public quotation endpoint uses an empty POST. Never POST
            // to a user-supplied endpoint or follow a POST redirect.
            let post = endpoint.as_str() == crate::banks::ICBC_API;
            let request = self
                .agent
                .get(endpoint.as_str())
                .config()
                .timeout_global(Some(remaining))
                .build()
                .header("User-Agent", "ExportDocManager/0.1 (exchange rate reader)");
            let response = if post {
                self.agent
                    .post(endpoint.as_str())
                    .config()
                    .timeout_global(Some(remaining))
                    .build()
                    .header("User-Agent", "ExportDocManager/0.1 (exchange rate reader)")
                    .header("Content-Type", "application/json")
                    .send_empty()
            } else {
                request.call()
            };
            let mut response = response.map_err(|error| {
                Error::new(
                    if matches!(error, ureq::Error::Timeout(_)) {
                        ErrorKind::Timeout
                    } else {
                        ErrorKind::Unavailable
                    },
                    "汇率源连接失败，请检查网络与来源服务。",
                )
            })?;
            if response.status().is_redirection() {
                if post {
                    return Err(Error::new(
                        ErrorKind::Unavailable,
                        "工商银行报价接口已跳转，请核对官方来源。",
                    ));
                }
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
                let mut cause = Error::new(
                    ErrorKind::Unavailable,
                    format!("汇率源返回 HTTP {}。", response.status().as_u16()),
                );
                cause.retry_after = response
                    .headers()
                    .get("retry-after")
                    .and_then(|v| v.to_str().ok())
                    .and_then(|v| {
                        v.parse::<u64>().ok().map(Duration::from_secs).or_else(|| {
                            chrono::DateTime::parse_from_rfc2822(v)
                                .ok()
                                .and_then(|time| {
                                    (time.with_timezone(&chrono::Utc) - chrono::Utc::now())
                                        .to_std()
                                        .ok()
                                })
                        })
                    });
                return Err(cause);
            }
            let mut bytes = vec![];
            let mut buffer = [0u8; 16 * 1024];
            let mut reader = response.body_mut().as_reader();
            loop {
                check()?;
                let count = reader.read(&mut buffer).map_err(|error| {
                    let timeout = error.kind() == std::io::ErrorKind::TimedOut
                        || error
                            .get_ref()
                            .and_then(|cause| cause.downcast_ref::<ureq::Error>())
                            .is_some_and(|cause| matches!(cause, ureq::Error::Timeout(_)));
                    Error::new(
                        if timeout {
                            ErrorKind::Timeout
                        } else {
                            ErrorKind::Unavailable
                        },
                        "汇率源响应读取失败，请检查网络或稍后重试。",
                    )
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
