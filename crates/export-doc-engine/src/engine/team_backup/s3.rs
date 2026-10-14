//! S3-compatible replicas: official AWS SigV4, explicit credentials and controlled transport.
use super::{
    error::{Result, invalid, unavailable},
    hex,
};
use crate::engine::backup_io;
use aws_credential_types::Credentials;
use aws_sigv4::{
    http_request::{self, SignableBody, SignableRequest, SigningSettings},
    sign::v4,
};
use export_doc_domain::backup_plan::S3Settings;
use sha2::{Digest, Sha256};
use std::{
    io::Read,
    path::Path,
    time::{Duration, SystemTime},
};
use ureq::http::{Request, Response};
mod multipart;

const CONTROL_TIMEOUT: Duration = Duration::from_secs(30);
const RESPONSE_LIMIT: u64 = 256 * 1024;
const EMPTY_HASH: &str = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

pub(super) fn validate(settings: &S3Settings) -> Result<url::Url> {
    let url = url::Url::parse(&settings.endpoint).map_err(|_| invalid("S3 端点地址无效。"))?;
    if !settings.path_style && matches!(url.host(), Some(url::Host::Ipv4(_) | url::Host::Ipv6(_))) {
        return Err(invalid("使用 IP 地址的 S3 端点请选择路径寻址。"));
    }
    if !matches!(url.scheme(), "https" | "http")
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
    {
        return Err(invalid(
            "S3 端点须为不含账号、查询参数或片段的 HTTP(S) 地址。",
        ));
    }
    if !(3..=63).contains(&settings.bucket.len())
        || settings.bucket.split('.').any(|part| {
            part.is_empty()
                || !part
                    .bytes()
                    .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == b'-')
                || !part.starts_with(|ch: char| ch.is_ascii_alphanumeric())
                || !part.ends_with(|ch: char| ch.is_ascii_alphanumeric())
        })
    {
        return Err(invalid("S3 桶名称须符合 DNS 命名规则，长度为 3–63。"));
    }
    if settings.region.is_empty()
        || settings.region.len() > 64
        || !settings
            .region
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || byte == b'-')
    {
        return Err(invalid("S3 区域名称无效。"));
    }
    if settings.prefix.len() > 2048
        || settings.prefix.contains('\\')
        || settings.prefix.chars().any(char::is_control)
        || settings
            .prefix
            .trim_matches('/')
            .split('/')
            .any(|part| matches!(part, "." | ".."))
    {
        return Err(invalid("S3 前缀包含无效路径。"));
    }
    if settings.access_key_id.is_empty()
        || settings.access_key_id.len() > 256
        || settings.access_key_id.chars().any(char::is_control)
    {
        return Err(invalid("S3 访问密钥 ID 无效。"));
    }
    Ok(url)
}

pub(super) struct Target {
    settings: S3Settings,
    endpoint: url::Url,
    repository: String,
    credentials: Credentials,
    agent: ureq::Agent,
}
impl Target {
    pub(super) fn new(
        settings: S3Settings,
        repository: &str,
        secret: &str,
        token: &str,
    ) -> Result<Self> {
        let endpoint = validate(&settings)?;
        if secret.is_empty() {
            return Err(invalid("请先保存 S3 密钥。"));
        }
        let credentials = Credentials::new(
            settings.access_key_id.clone(),
            secret,
            (!token.is_empty()).then(|| token.to_owned()),
            None,
            "exportdoc-backup",
        );
        Ok(Self {
            settings,
            endpoint,
            repository: repository.into(),
            credentials,
            agent: export_doc_network::storage_agent(
                Duration::from_secs(backup_io::TIMEOUT_SECONDS),
                crate::operation::cancellation_flag(),
            ),
        })
    }
    pub(super) fn identity(&self) -> String {
        format!(
            "S3:{}:{}",
            self.url("").expect("validated S3 target"),
            self.settings.access_key_id
        )
    }
    fn url(&self, name: &str) -> Result<url::Url> {
        if !name.is_empty() && !crate::paths::valid_file_name(name) {
            return Err(invalid("S3 对象文件名无效。"));
        }
        let mut url = self.endpoint.clone();
        if !self.settings.path_style {
            let host = format!("{}.{}", self.settings.bucket, url.host_str().unwrap());
            url.set_host(Some(&host))
                .map_err(|_| invalid("S3 虚拟主机地址无效，请使用路径寻址。"))?;
        }
        {
            let mut segments = url
                .path_segments_mut()
                .map_err(|_| invalid("S3 地址不能包含对象路径。"))?;
            segments.pop_if_empty();
            if self.settings.path_style {
                segments.push(&self.settings.bucket);
            }
            for part in self
                .settings
                .prefix
                .split('/')
                .filter(|part| !part.is_empty())
            {
                segments.push(part);
            }
            segments.push(&self.repository);
            if !name.is_empty() {
                segments.push(name);
            }
        }
        Ok(url)
    }
    fn request<B>(
        &self,
        method: &str,
        url: &url::Url,
        body: B,
        size: Option<u64>,
        hash: &str,
    ) -> Result<Request<B>> {
        crate::operation::check()?;
        let mut request = Request::builder().method(method).uri(url.as_str());
        if let Some(size) = size {
            request = request.header("content-length", size);
        }
        let mut request = request.body(body).map_err(|_| invalid("S3 请求无效。"))?;
        let mut settings = SigningSettings::default();
        settings.percent_encoding_mode = http_request::PercentEncodingMode::Single;
        settings.uri_path_normalization_mode = http_request::UriPathNormalizationMode::Disabled;
        settings.payload_checksum_kind = http_request::PayloadChecksumKind::XAmzSha256;
        let identity = self.credentials.clone().into();
        let params = v4::SigningParams::builder()
            .identity(&identity)
            .region(&self.settings.region)
            .name("s3")
            .time(SystemTime::now())
            .settings(settings)
            .build()
            .map_err(|_| invalid("S3 签名配置无效。"))?
            .into();
        let signable = SignableRequest::new(
            method,
            url.as_str(),
            request
                .headers()
                .iter()
                .map(|(key, value)| (key.as_str(), value.to_str().expect("generated headers"))),
            SignableBody::Precomputed(hash.into()),
        )
        .map_err(|_| invalid("S3 请求无法签名。"))?;
        http_request::sign(signable, &params)
            .map_err(|_| unavailable("S3 签名失败。"))?
            .into_parts()
            .0
            .apply_to_request_http1x(&mut request);
        Ok(request)
    }
    fn control(
        &self,
        agent: &ureq::Agent,
        method: &str,
        url: &url::Url,
        body: &[u8],
    ) -> Result<String> {
        let hash = hex(&Sha256::digest(body));
        let request = self.request(method, url, body, Some(body.len() as u64), &hash)?;
        let response = agent
            .run(request)
            .map_err(|_| unavailable("S3 请求失败或超时，请检查端点、连接和权限。"))?;
        response_text(response)
    }
    pub(super) fn delete(&self, name: &str) -> Result<()> {
        self.control(&self.agent, "DELETE", &self.url(name)?, &[])
            .map(|_| ())
    }
    pub(super) fn upload(
        &self,
        path: &Path,
        name: &str,
        size: u64,
        digest: &str,
        verify: bool,
    ) -> Result<()> {
        self.multipart(path, name, size)?;
        if verify {
            self.verify(name, size, digest)?;
        }
        Ok(())
    }
    fn verify(&self, name: &str, size: u64, digest: &str) -> Result<()> {
        let request = self.request("GET", &self.url(name)?, (), None, EMPTY_HASH)?;
        let mut response = self
            .agent
            .run(request)
            .map_err(|_| unavailable("S3 副本回读失败或超时。"))?;
        if response.status().as_u16() != 200 {
            return Err(unavailable("S3 副本无法完整回读。"));
        }
        let actual = backup_io::copy_and_hash(
            &mut response.body_mut().as_reader(),
            &mut std::io::sink(),
            size,
        )?;
        if actual != (size, digest.into()) {
            return Err(unavailable("S3 副本大小或 SHA-256 校验失败。"));
        }
        Ok(())
    }
}

fn response_text(mut response: Response<ureq::Body>) -> Result<String> {
    let status = response.status().as_u16();
    let mut bytes = Vec::new();
    response
        .body_mut()
        .as_reader()
        .take(RESPONSE_LIMIT + 1)
        .read_to_end(&mut bytes)
        .map_err(|_| unavailable("S3 响应读取失败。"))?;
    if bytes.len() as u64 > RESPONSE_LIMIT {
        return Err(unavailable("S3 控制响应过大。"));
    }
    let text = String::from_utf8(bytes).map_err(|_| unavailable("S3 控制响应编码无效。"))?;
    if !(200..300).contains(&status) || xml_root(&text)? == "Error" {
        // Never echo response bodies or credentials into persistent logs.
        let code = xml_value(&text, "Code").unwrap_or_default();
        let code: String = code
            .chars()
            .filter(char::is_ascii_alphanumeric)
            .take(64)
            .collect();
        return Err(unavailable(format!(
            "S3 操作失败（HTTP {status}，{code}）。"
        )));
    }
    Ok(text)
}
fn xml_root(xml: &str) -> Result<String> {
    let mut reader = quick_xml::Reader::from_str(xml);
    loop {
        match reader
            .read_event()
            .map_err(|_| unavailable("S3 响应 XML 无效。"))?
        {
            quick_xml::events::Event::Start(event) | quick_xml::events::Event::Empty(event) => {
                return Ok(event
                    .name()
                    .as_ref()
                    .rsplit(':')
                    .next()
                    .unwrap_or("")
                    .into());
            }
            quick_xml::events::Event::Eof => return Ok(String::new()),
            _ => {}
        }
    }
}
fn xml_value(xml: &str, name: &str) -> Result<String> {
    let mut reader = quick_xml::Reader::from_str(xml);
    loop {
        match reader
            .read_event()
            .map_err(|_| unavailable("S3 响应 XML 无效。"))?
        {
            quick_xml::events::Event::Start(event)
                if event.name().as_ref().rsplit(':').next() == Some(name) =>
            {
                let text = reader
                    .read_text(event.name())
                    .map_err(|_| unavailable("S3 响应字段无效。"))?;
                return quick_xml::escape::unescape(&text)
                    .map(|value| value.into_owned())
                    .map_err(|_| unavailable("S3 响应转义无效。"));
            }
            quick_xml::events::Event::Eof => {
                return Err(unavailable(format!("S3 响应缺少 {name}。")));
            }
            _ => {}
        }
    }
}
