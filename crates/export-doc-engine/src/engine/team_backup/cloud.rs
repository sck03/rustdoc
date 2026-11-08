//! WebDAV 完整加密副本的配置、受控传输及响应边界。
//! 凭据来自密封存储，上传后流式回读校验；发布与失败清理由 recovery 管理。
use super::{
    error::{Result, invalid, unavailable},
    settings,
    store::Store,
};
use crate::api::ApiError;
use base64::{Engine, prelude::BASE64_STANDARD};
use serde_json::Value;
use std::{fs, io::Read, path::Path, time::Duration};

const REQUEST_TIMEOUT: Duration = Duration::from_secs(30);
const TRANSFER_TIMEOUT: Duration = Duration::from_secs(super::super::backup_io::TIMEOUT_SECONDS);
const ERROR_LIMIT: usize = 64 * 1024;
const TRANSFER_BUFFER: usize = 128 * 1024;
mod recovery;
pub(super) use recovery::{RecoveryTarget, validate_recovery_settings};

struct Config {
    enabled: bool,
    url: String,
    user: String,
    password: zeroize::Zeroizing<String>,
}

impl Config {
    /// 后台任务复用同一读取路径:任务开始时重新读取当前设置与受密封保护的密码。
    fn read_parts(store: &Store, protector: &crate::secrets::Protector) -> Result<Self> {
        let settings = settings::current(store)?;
        let web_dav = &settings["webDav"];
        let password = settings::credential(store, protector, "/webDav/password")?;
        Ok(Self {
            enabled: web_dav["enabled"].as_bool().unwrap_or(false),
            url: web_dav["url"].as_str().unwrap_or("").trim().to_string(),
            user: web_dav["userName"]
                .as_str()
                .unwrap_or("")
                .trim()
                .to_string(),
            password,
        })
    }

    fn configured(&self) -> bool {
        !self.url.is_empty() && !self.user.is_empty()
    }
}

fn require_enabled(config: &Config) -> Result<()> {
    if !config.enabled {
        return Err(invalid("WebDAV 云备份未启用,请先保存启用状态。"));
    }
    if !config.configured() {
        return Err(invalid("WebDAV 尚未配置,请先保存服务器地址和用户名。"));
    }
    Ok(())
}

struct Endpoint {
    url: url::Url,
}

fn parse_url(value: &str) -> Result<Endpoint> {
    let url = url::Url::parse(value).map_err(|_| invalid("WebDAV 地址无效。"))?;
    if !matches!(url.scheme(), "http" | "https")
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
    {
        return Err(invalid(
            "WebDAV 地址必须是不含内嵌账号的 http:// 或 https:// 地址。",
        ));
    }
    if url
        .host_str()
        .is_some_and(|host| host.chars().count() > 253)
    {
        return Err(invalid("WebDAV 主机名无效。"));
    }
    Ok(Endpoint { url })
}

impl Endpoint {
    fn path(&self) -> &str {
        if self.url.path().is_empty() {
            "/"
        } else {
            self.url.path()
        }
    }

    fn request_url(&self, suffix: &str) -> String {
        let mut url = self.url.clone();
        let base = self.path().trim_end_matches('/');
        url.set_path(&format!("{base}/{suffix}"));
        url.set_query(None);
        url.set_fragment(None);
        url.into()
    }

    fn propfind_url(&self) -> String {
        let mut url = self.url.clone();
        let base = self.path().trim_end_matches('/');
        url.set_path(&format!("{base}/"));
        url.set_query(None);
        url.set_fragment(None);
        url.into()
    }
}

fn percent_encode(value: &str) -> String {
    let mut output = String::new();
    for byte in value.as_bytes() {
        if byte.is_ascii_alphanumeric() || b"-_.~".contains(byte) {
            output.push(*byte as char);
        } else {
            output.push_str(&format!("%{byte:02X}"));
        }
    }
    output
}

fn authorization(config: &Config) -> (String, String) {
    (
        "Authorization".into(),
        format!(
            "Basic {}",
            BASE64_STANDARD.encode(format!("{}:{}", config.user, config.password.as_str()))
        ),
    )
}

fn remote_path(endpoint: &Endpoint, file_name: &str) -> Result<String> {
    if file_name.contains('/') || file_name.contains('\\') {
        return Err(invalid("远端备份文件名不能包含路径。"));
    }
    Ok(endpoint.request_url(&percent_encode(file_name)))
}

fn agent(timeout: Duration) -> ureq::Agent {
    export_doc_network::storage_agent(timeout, crate::operation::cancellation_flag())
}

fn read_limited(reader: &mut dyn Read, limit: usize, message: &'static str) -> Result<Vec<u8>> {
    let mut body = Vec::new();
    let mut buffer = [0u8; TRANSFER_BUFFER];
    loop {
        crate::operation::check()?;
        let count = reader.read(&mut buffer).map_err(|_| unavailable(message))?;
        if count == 0 {
            break;
        }
        if body.len() + count > limit {
            return Err(unavailable(message));
        }
        body.extend_from_slice(&buffer[..count]);
    }
    Ok(body)
}

struct Response {
    status: u16,
    body: Vec<u8>,
}

fn request(
    _endpoint: &Endpoint,
    method: &str,
    url: &str,
    headers: &[(&str, String)],
    body: &[u8],
    response_limit: usize,
    timeout: Duration,
) -> Result<Response> {
    crate::operation::check()?;
    let request = ureq::http::Request::builder()
        .method(method)
        .uri(url)
        .body(body.to_vec())
        .map_err(|_| invalid("WebDAV 请求地址无效。"))?;
    let mut request = request;
    for (name, value) in headers {
        request.headers_mut().insert(
            ureq::http::HeaderName::from_bytes(name.as_bytes())
                .map_err(|_| invalid("WebDAV 请求头无效。"))?,
            ureq::http::HeaderValue::from_str(value)
                .map_err(|_| invalid("WebDAV 请求头值无效。"))?,
        );
    }
    let mut response = agent(timeout).run(request).map_err(|error| match error {
        ureq::Error::Timeout(_) => unavailable("WebDAV 请求超时。"),
        _ => unavailable(format!("WebDAV 请求失败:{error}")),
    })?;
    let status = response.status().as_u16();
    let mut reader = response.body_mut().as_reader();
    let body = read_limited(&mut reader, response_limit, "WebDAV 响应超过容量上限。")?;
    Ok(Response { status, body })
}

fn failure(status: u16, body: &[u8]) -> ApiError {
    unavailable(format!(
        "WebDAV 请求失败(HTTP {status}):{}",
        String::from_utf8_lossy(&body[..body.len().min(ERROR_LIMIT)])
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn replica_urls_and_response_limits_are_bounded() {
        let endpoint = parse_url("https://dav.example.invalid/team/backups").unwrap();
        assert_eq!(
            endpoint.request_url("%E5%A4%87%E4%BB%BD.edmrecovery"),
            "https://dav.example.invalid/team/backups/%E5%A4%87%E4%BB%BD.edmrecovery"
        );
        assert!(parse_url("ftp://dav.example.invalid/backups").is_err());
        assert!(parse_url("https://user:secret@dav.example.invalid/backups").is_err());
        assert!(remote_path(&endpoint, "../escape.edmrecovery").is_err());
        assert!(read_limited(&mut std::io::Cursor::new(vec![0; 65]), 64, "too large").is_err());
        assert_eq!(
            read_limited(&mut std::io::Cursor::new(vec![0; 64]), 64, "too large")
                .unwrap()
                .len(),
            64
        );
    }
}
