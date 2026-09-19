//! Invoked only on an explicit review action. No retry can duplicate disclosure.
use export_doc_network::{AllowedResolver, loopback_host, public_address};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{
    io::{Read, Write},
    net::IpAddr,
    time::Duration,
};
use ureq::unversioned::transport::DefaultConnector;
use zeroize::Zeroize;

#[derive(Serialize, Deserialize)]
pub struct Request {
    pub endpoint: String,
    pub model: String,
    pub api_key: String,
    pub system_prompt: String,
    pub content: String,
}
impl Drop for Request {
    fn drop(&mut self) {
        self.api_key.zeroize();
    }
}
#[derive(Debug, Serialize, Deserialize)]
pub struct Error {
    pub status: u16,
    pub message: String,
}
type Result<T> = std::result::Result<T, Error>;
fn error(status: u16, message: impl Into<String>) -> Error {
    Error {
        status,
        message: message.into(),
    }
}
pub fn endpoint(value: &str) -> Result<url::Url> {
    let value = if value.trim().is_empty() {
        "https://api.deepseek.com/v1/chat/completions"
    } else {
        value.trim()
    };
    let url = url::Url::parse(value).map_err(|_| error(400, "AI 接口地址无效。"))?;
    let host = url
        .host_str()
        .ok_or_else(|| error(400, "AI 接口缺少主机。"))?;
    if value.len() > 2048
        || !["http", "https"].contains(&url.scheme())
        || !url.username().is_empty()
        || url.password().is_some()
        || url.fragment().is_some()
        || (url.scheme() == "http" && !loopback_host(host))
        || (!loopback_host(host)
            && host
                .trim_matches(['[', ']'])
                .parse::<IpAddr>()
                .is_ok_and(|ip| !public_address(ip)))
    {
        return Err(error(
            400,
            "AI 接口须使用公开 HTTPS，或显式 localhost／回环地址；不允许账号信息或片段。",
        ));
    }
    Ok(url)
}
pub fn complete(request: &Request) -> Result<String> {
    let endpoint = endpoint(&request.endpoint)?;
    if request.api_key.trim().is_empty() && !endpoint.host_str().is_some_and(loopback_host) {
        return Err(error(400, "请先设置 AI API 密钥。"));
    }
    if request.api_key.len() > 16 * 1024
        || request.api_key.contains(['\r', '\n'])
        || request.content.len() > 512 * 1024
        || request.system_prompt.len() > 64 * 1024
        || request.model.len() > 256
    {
        return Err(error(400, "AI 请求或配置超过容量限制。"));
    }
    let model = if request.model.trim().is_empty() {
        "deepseek-chat"
    } else {
        request.model.trim()
    };
    let config = ureq::Agent::config_builder()
        .proxy(None)
        .max_redirects(0)
        .http_status_as_error(false)
        .timeout_global(Some(Duration::from_secs(120)))
        .timeout_resolve(Some(Duration::from_secs(5)))
        .timeout_connect(Some(Duration::from_secs(10)))
        .tls_config(
            ureq::tls::TlsConfig::builder()
                .provider(ureq::tls::TlsProvider::NativeTls)
                .root_certs(ureq::tls::RootCerts::PlatformVerifier)
                .build(),
        )
        .build();
    let agent = ureq::Agent::with_parts(
        config,
        DefaultConnector::new(),
        AllowedResolver {
            allow_explicit_loopback: true,
        },
    );
    let mut call = agent.post(endpoint.as_str());
    if !request.api_key.trim().is_empty() {
        call = call.header(
            "Authorization",
            format!("Bearer {}", request.api_key.trim()),
        );
    }
    let mut response = call.send_json(json!({"model":model,"messages":[{"role":"system","content":request.system_prompt},{"role":"user","content":format!("请输出结构化的信用证合规审查报告。\n\n{}",request.content)}],"temperature":0.3}))
        .map_err(|e| error(if matches!(e, ureq::Error::Timeout(_)) {504} else {503}, "AI 服务连接失败或超过时限。"))?;
    if !response.status().is_success() {
        return Err(error(
            503,
            format!("AI 服务返回 HTTP {}。", response.status().as_u16()),
        ));
    }
    let mut bytes = Vec::new();
    response
        .body_mut()
        .as_reader()
        .take(4 * 1024 * 1024 + 1)
        .read_to_end(&mut bytes)
        .map_err(|_| error(503, "AI 响应读取失败。"))?;
    if bytes.len() > 4 * 1024 * 1024 {
        return Err(error(503, "AI 响应超过 4 MiB。"));
    }
    let value: Value =
        serde_json::from_slice(&bytes).map_err(|_| error(503, "AI 服务返回无效 JSON。"))?;
    let content = &value["choices"][0]["message"]["content"];
    let text = if let Some(text) = content.as_str() {
        text.to_owned()
    } else if let Some(parts) = content.as_array() {
        parts
            .iter()
            .filter_map(|p| p.as_str().or_else(|| p["text"].as_str()))
            .collect::<Vec<_>>()
            .join("\n")
    } else {
        return Err(error(503, "AI 服务返回无法解析的正文。"));
    };
    if text.trim().is_empty() {
        return Err(error(503, "AI 服务未返回审查报告。"));
    }
    Ok(text)
}
pub fn worker() -> std::result::Result<(), String> {
    let mut input = Vec::new();
    std::io::stdin()
        .take(640 * 1024 + 1)
        .read_to_end(&mut input)
        .map_err(|e| e.to_string())?;
    if input.len() > 640 * 1024 {
        input.zeroize();
        return Err("AI 工作进程输入超限。".into());
    }
    let request = serde_json::from_slice::<Request>(&input);
    input.zeroize();
    let result = request
        .map_err(|_| error(400, "AI 工作进程输入无效。"))
        .and_then(|request| complete(&request));
    let output = serde_json::to_vec(&result).map_err(|e| e.to_string())?;
    std::io::stdout()
        .write_all(&output)
        .map_err(|e| e.to_string())
}
