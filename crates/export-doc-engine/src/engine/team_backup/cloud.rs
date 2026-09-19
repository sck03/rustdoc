//! WebDAV 云备份。配置来自保存在数据库设置中的 WebDAV 项；未配置时状态接口
//! 返回明确状态，其余操作返回 400。传输层是一个只支持 http:// 的最小
//! HTTP/1.1 客户端：不引入新依赖，也不默认把密码或业务数据写到系统目录。
use super::{
    CLOUD_STORAGE_POLICY, NativeService, auth,
    error::{Result, error, invalid, unavailable},
    settings,
    store::{Actor, Store},
    tasks::{FileOutput, TaskOutput},
};
use crate::{contracts, generated_api::*};
use base64::{Engine, prelude::BASE64_STANDARD};
use serde_json::{Value, json};
use std::{
    fs,
    io::{Read, Write},
    net::{TcpStream, ToSocketAddrs},
    path::{Path, PathBuf},
    time::Duration,
};

const CONNECT_TIMEOUT: Duration = Duration::from_secs(15);
const REQUEST_TIMEOUT: Duration = Duration::from_secs(30);
const TRANSFER_TIMEOUT: Duration = Duration::from_secs(600);
const PROP_FIND_LIMIT: usize = 2 * 1024 * 1024;
const ERROR_LIMIT: usize = 64 * 1024;
const TRANSFER_BUFFER: usize = 128 * 1024;
const MAX_TRANSFER_BYTES: u64 = 4 * 1024 * 1024 * 1024;
const SQLITE_MAGIC: &[u8] = b"SQLite format 3\0";

struct Config {
    enabled: bool,
    url: String,
    user: String,
    password: String,
}
impl Config {
    fn read(service: &NativeService) -> Result<Self> {
        Self::read_parts(&service.store, &service.protector)
    }
    /// 后台任务复用同一读取路径：任务开始时重新读取当前设置与受密封保护的密码。
    fn read_parts(store: &Store, protector: &crate::secrets::Protector) -> Result<Self> {
        let settings = settings::current(store)?;
        let web_dav = &settings["webDav"];
        let password = settings::credential(store, protector, "/webDav/password")
            .map(|value| value.to_string())
            .unwrap_or_default();
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
        return Err(invalid("WebDAV 云备份未启用，请先保存启用状态。"));
    }
    if !config.configured() {
        return Err(invalid("WebDAV 尚未配置，请先保存服务器地址和用户名。"));
    }
    Ok(())
}

// --- 最小 HTTP/1.1 客户端（只支持明文 http://）。 ---

struct Endpoint {
    host: String,
    port: u16,
    path: String,
}
fn parse_url(url: &str) -> Result<Endpoint> {
    let rest = url.strip_prefix("http://").ok_or_else(|| {
        invalid("WebDAV 地址只支持 http:// 明文通道；https 端点需要服务器启用 TLS 传输构建。")
    })?;
    let (authority, path) = match rest.split_once(['/', '?', '#']) {
        Some((authority, rest)) => (
            authority,
            format!("/{}", rest.split(['?', '#']).next().unwrap_or("")),
        ),
        None => (rest, "/".to_string()),
    };
    if authority.is_empty() || authority.contains('@') {
        return Err(invalid("WebDAV 地址缺少主机或包含多余的用户信息。"));
    }
    let (host, port) = match authority.split_once(':') {
        Some((host, port)) => (
            host.to_string(),
            port.parse::<u16>()
                .map_err(|_| invalid("WebDAV 端口号无效。"))?,
        ),
        None => (authority.to_string(), 80),
    };
    if host.is_empty() || host.chars().count() > 253 {
        return Err(invalid("WebDAV 主机名无效。"));
    }
    Ok(Endpoint { host, port, path })
}
struct Response {
    status: u16,
    body: Vec<u8>,
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
fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut output: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'%' && index + 2 < bytes.len() {
            let hex = String::from_utf8_lossy(&bytes[index + 1..index + 3]);
            if let Ok(byte) = u8::from_str_radix(&hex, 16) {
                output.push(byte);
                index += 3;
                continue;
            }
        }
        output.push(bytes[index]);
        index += 1;
    }
    String::from_utf8_lossy(&output).into_owned()
}
fn connect(endpoint: &Endpoint, timeout: Duration) -> Result<TcpStream> {
    let address = (endpoint.host.as_str(), endpoint.port)
        .to_socket_addrs()
        .map_err(|_| unavailable(format!("无法解析 WebDAV 地址：{}", endpoint.host)))?
        .next()
        .ok_or_else(|| unavailable("WebDAV 地址没有可用的解析结果。"))?;
    let stream = TcpStream::connect_timeout(&address, CONNECT_TIMEOUT)?;
    stream.set_write_timeout(Some(timeout))?;
    stream.set_read_timeout(Some(timeout))?;
    Ok(stream)
}
fn host_header(endpoint: &Endpoint) -> String {
    format!(
        "{}{}",
        endpoint.host,
        if endpoint.port == 80 {
            String::new()
        } else {
            format!(":{}", endpoint.port)
        }
    )
}
/// 分块写入，保留可取消边界。
fn write_all(stream: &mut TcpStream, bytes: &[u8]) -> Result<()> {
    let mut written = 0;
    while written < bytes.len() {
        crate::operation::check()?;
        let end = (written + TRANSFER_BUFFER).min(bytes.len());
        stream
            .write_all(&bytes[written..end])
            .map_err(|_| unavailable("写入 WebDAV 连接失败。"))?;
        stream
            .flush()
            .map_err(|_| unavailable("刷新 WebDAV 连接失败。"))?;
        written = end;
    }
    Ok(())
}
fn read_head(stream: &mut TcpStream, limit: usize) -> Result<(String, Vec<u8>)> {
    let mut buffer = Vec::new();
    let mut chunk = [0u8; TRANSFER_BUFFER];
    loop {
        crate::operation::check()?;
        let length = stream
            .read(&mut chunk)
            .map_err(|_| unavailable("读取 WebDAV 响应失败。"))?;
        if length == 0 {
            break;
        }
        buffer.extend_from_slice(&chunk[..length]);
        if buffer.windows(4).any(|window| window == b"\r\n\r\n") {
            break;
        }
        if buffer.len() > limit + 8 * 1024 {
            return Err(unavailable("WebDAV 响应头超过容量上限。"));
        }
    }
    let split = buffer
        .windows(4)
        .position(|window| window == b"\r\n\r\n")
        .ok_or_else(|| unavailable("WebDAV 响应头不完整。"))?;
    let head = String::from_utf8_lossy(&buffer[..split]).to_string();
    Ok((head, buffer[split + 4..].to_vec()))
}
fn parse_status(head: &str) -> Result<(u16, Option<usize>, bool)> {
    let mut lines = head.split("\r\n");
    let status = lines
        .next()
        .and_then(|line| line.split_whitespace().nth(1))
        .and_then(|value| value.parse::<u16>().ok())
        .ok_or_else(|| unavailable("WebDAV 响应状态无效。"))?;
    let mut content_length = None;
    let mut chunked = false;
    for line in lines {
        if let Some((name, value)) = line.split_once(':') {
            let name = name.trim().to_ascii_lowercase();
            let value = value.trim();
            if name == "content-length" {
                content_length = value.parse::<usize>().ok();
            } else if name == "transfer-encoding" && value.eq_ignore_ascii_case("chunked") {
                chunked = true;
            }
        }
    }
    Ok((status, content_length, chunked))
}
fn read_body(stream: &mut TcpStream, initial: &[u8], limit: usize) -> Result<Vec<u8>> {
    let mut body = initial.to_vec();
    let mut chunk = vec![0u8; TRANSFER_BUFFER];
    while body.len() < limit {
        crate::operation::check()?;
        let length = stream
            .read(&mut chunk)
            .map_err(|_| unavailable("读取 WebDAV 响应体失败。"))?;
        if length == 0 {
            break;
        }
        body.extend_from_slice(&chunk[..length]);
    }
    body.truncate(limit);
    Ok(body)
}
fn request(
    endpoint: &Endpoint,
    method: &str,
    path: &str,
    extra_headers: &[(&str, String)],
    body: &[u8],
    response_limit: usize,
    timeout: Duration,
) -> Result<Response> {
    crate::operation::check()?;
    let mut stream = connect(endpoint, timeout)?;
    let mut head = format!(
        "{method} {path} HTTP/1.1\r\nHost: {}\r\nConnection: close\r\n",
        host_header(endpoint)
    );
    for (name, value) in extra_headers {
        head.push_str(&format!("{name}: {value}\r\n"));
    }
    head.push_str(&format!("Content-Length: {}\r\n\r\n", body.len()));
    let mut payload = Vec::with_capacity(head.len() + body.len());
    payload.extend_from_slice(head.as_bytes());
    payload.extend_from_slice(body);
    write_all(&mut stream, &payload)?;
    let (head, body) = read_head(&mut stream, response_limit)?;
    let (status, content_length, chunked) = parse_status(&head)?;
    if chunked {
        return Err(unavailable(
            "WebDAV 服务器使用分块传输，当前传输通道不支持。",
        ));
    }
    let body = match content_length {
        Some(length) if length > response_limit => {
            return Err(unavailable("WebDAV 响应超过容量上限。"));
        }
        Some(length) => read_body(&mut stream, &body, length)?,
        None => body,
    };
    Ok(Response { status, body })
}
fn failure(status: u16, body: &[u8]) -> ApiError {
    unavailable(format!(
        "WebDAV 请求失败（HTTP {status}）：{}",
        String::from_utf8_lossy(&body[..body.len().min(ERROR_LIMIT)])
    ))
}

// --- WebDAV 操作。 ---

use crate::api::ApiError;

fn authorization(config: &Config) -> Result<(String, String)> {
    Ok((
        "Authorization".into(),
        format!(
            "Basic {}",
            BASE64_STANDARD.encode(format!("{}:{}", config.user, config.password))
        ),
    ))
}
fn remote_path(endpoint: &Endpoint, file_name: &str) -> Result<String> {
    if file_name.contains('/') || file_name.contains('\\') {
        return Err(invalid("远端备份文件名不能包含路径。"));
    }
    Ok(format!(
        "{}/{}",
        endpoint.path.trim_end_matches('/'),
        percent_encode(file_name)
    ))
}
struct CloudItem {
    file_name: String,
    size_bytes: u64,
    last_modified: String,
}
/// PROPFIND 列出远端备份。
fn propfind(config: &Config) -> Result<Vec<CloudItem>> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config)?;
    let body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>";
    let response = request(
        &endpoint,
        "PROPFIND",
        &format!("{}/", endpoint.path.trim_end_matches('/')),
        &[
            (name.as_str(), value),
            ("Depth", "1".into()),
            ("Content-Type", "application/xml; charset=utf-8".into()),
        ],
        body.as_bytes(),
        PROP_FIND_LIMIT,
        REQUEST_TIMEOUT,
    )?;
    if !(200..300).contains(&response.status) {
        return Err(failure(response.status, &response.body));
    }
    parse_propfind(&String::from_utf8_lossy(&response.body))
}
/// 只解析 displayname / getcontentlength / getlastmodified / resourcetype 四个本地名，
/// 命名空间前缀（d:、D: 或默认）不影响匹配。
fn parse_propfind(xml: &str) -> Result<Vec<CloudItem>> {
    let mut items = Vec::new();
    for segment in xml_segments(xml, "response") {
        if tag_text(&segment, "resourcetype")
            .map(|value| value.contains("collection"))
            .unwrap_or(false)
        {
            continue;
        }
        let display_name = tag_text(&segment, "displayname");
        let href = tag_text(&segment, "href");
        let file_name = match display_name {
            Some(name) if !name.trim().is_empty() => name.trim().to_string(),
            _ => percent_decode(href.as_deref().unwrap_or("").trim()),
        };
        if file_name.is_empty() || !file_name.ends_with(".sqlite3") {
            continue;
        }
        let size_bytes = tag_text(&segment, "getcontentlength")
            .and_then(|value| value.trim().parse::<u64>().ok())
            .unwrap_or(0);
        let last_modified = tag_text(&segment, "getlastmodified")
            .and_then(|value| chrono::DateTime::parse_from_rfc2822(value.trim()).ok())
            .map(|time| time.with_timezone(&chrono::Utc).to_rfc3339())
            .unwrap_or_default();
        items.push(CloudItem {
            file_name,
            size_bytes,
            last_modified,
        });
    }
    items.sort_by(|left, right| {
        right
            .last_modified
            .cmp(&left.last_modified)
            .then_with(|| right.file_name.cmp(&left.file_name))
    });
    Ok(items)
}
fn local_name(tag: &str) -> &str {
    let inner = tag.trim_start_matches('<').trim_end_matches('>').trim();
    let name = inner.split([' ', '/', '>']).next().unwrap_or("");
    name.rsplit_once(':').map(|(_, name)| name).unwrap_or(name)
}
/// 取出某个本地标签的成对内容（开始标签到结束标签之间）。
fn xml_segments(xml: &str, local: &str) -> Vec<String> {
    let mut segments = Vec::new();
    let bytes = xml.as_bytes();
    let mut index = 0;
    while let Some(start) = xml[index..].find('<') {
        let absolute = index + start;
        let end = bytes[absolute..]
            .iter()
            .position(|byte| *byte == b'>')
            .map(|position| absolute + position + 1);
        match end {
            Some(end)
                if local_name(&xml[absolute..end]) == local
                    && !xml[absolute..end].ends_with("/>") =>
            {
                let close = format!("</{local}");
                if let Some(close_start) = xml[end..].find(&close) {
                    let close_start = end + close_start;
                    segments.push(xml[end..close_start].to_string());
                    index = xml[close_start..]
                        .find('>')
                        .map(|position| close_start + position + 1)
                        .unwrap_or(xml.len());
                    continue;
                }
            }
            _ => {}
        }
        index = end.unwrap_or(xml.len());
    }
    segments
}
fn tag_text(segment: &str, local: &str) -> Option<String> {
    xml_segments(segment, local).into_iter().next()
}

/// 上传一个本地备份文件到 WebDAV。文件内容按块流式写入，保留取消边界。
fn upload_file(config: &Config, path: &Path, file_name: &str) -> Result<()> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config)?;
    let metadata = fs::metadata(path)?;
    if !metadata.is_file() || metadata.len() == 0 {
        return Err(invalid("要上传的本地备份文件为空或不存在。"));
    }
    if metadata.len() > MAX_TRANSFER_BYTES {
        return Err(invalid("本地备份超过 4 GiB 上传上限。"));
    }
    let remote = remote_path(&endpoint, file_name)?;
    let mut stream = connect(&endpoint, TRANSFER_TIMEOUT)?;
    let head = format!(
        "PUT {remote} HTTP/1.1\r\nHost: {}\r\nConnection: close\r\n{name}: {value}\r\nContent-Type: application/octet-stream\r\nContent-Length: {}\r\n\r\n",
        host_header(&endpoint),
        metadata.len()
    );
    write_all(&mut stream, head.as_bytes())?;
    let mut reader = std::io::BufReader::new(fs::File::open(path)?);
    let mut buffer = vec![0u8; TRANSFER_BUFFER];
    loop {
        crate::operation::check()?;
        let length = reader
            .read(&mut buffer)
            .map_err(|_| unavailable("读取本地备份失败。"))?;
        if length == 0 {
            break;
        }
        write_all(&mut stream, &buffer[..length])?;
    }
    let (head, body) = read_head(&mut stream, ERROR_LIMIT)?;
    let (status, _, _) = parse_status(&head)?;
    if !(200..300).contains(&status) {
        return Err(failure(status, &body));
    }
    Ok(())
}
/// 下载远端备份到受管暂存目录，返回暂存路径与字节数。失败时清理暂存文件。
fn download_file(config: &Config, file_name: &str, staging_root: &Path) -> Result<(PathBuf, u64)> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config)?;
    let remote = remote_path(&endpoint, file_name)?;
    let temporary = staging_root.join(format!(
        ".download-{}.tmp",
        crate::paths::nonce().map_err(|cause| unavailable(cause))?
    ));
    crate::paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
    let result = (|| {
        let mut stream = connect(&endpoint, TRANSFER_TIMEOUT)?;
        let head = format!(
            "GET {remote} HTTP/1.1\r\nHost: {}\r\nConnection: close\r\n{name}: {value}\r\n\r\n",
            host_header(&endpoint)
        );
        write_all(&mut stream, head.as_bytes())?;
        let mut writer = fs::File::create(&temporary)?;
        let mut buffer = vec![0u8; TRANSFER_BUFFER];
        let mut total: u64 = 0;
        let mut head_buffer = Vec::new();
        let mut content_length: Option<u64> = None;
        let mut header_done = false;
        loop {
            crate::operation::check()?;
            let length = stream
                .read(&mut buffer)
                .map_err(|_| unavailable("读取 WebDAV 下载流失败。"))?;
            if length == 0 {
                break;
            }
            let mut chunk = &buffer[..length];
            if !header_done {
                head_buffer.extend_from_slice(chunk);
                if let Some(split) = head_buffer
                    .windows(4)
                    .position(|window| window == b"\r\n\r\n")
                {
                    let head = String::from_utf8_lossy(&head_buffer[..split]).to_string();
                    let status: u16 = head
                        .split("\r\n")
                        .next()
                        .and_then(|line| line.split_whitespace().nth(1))
                        .and_then(|value| value.parse::<u16>().ok())
                        .ok_or_else(|| unavailable("WebDAV 响应状态无效。"))?;
                    for line in head.split("\r\n").skip(1) {
                        if let Some((key, value)) = line.split_once(':') {
                            if key.trim().eq_ignore_ascii_case("content-length") {
                                content_length = value.trim().parse::<u64>().ok();
                            }
                        }
                    }
                    if !(200..300).contains(&status) {
                        return Err(failure(status, &head_buffer[split + 4..]));
                    }
                    chunk = &head_buffer[split + 4..];
                    header_done = true;
                } else {
                    if head_buffer.len() > 64 * 1024 {
                        return Err(unavailable("WebDAV 响应头超过容量上限。"));
                    }
                    continue;
                }
            }
            if !chunk.is_empty() {
                total = total
                    .checked_add(chunk.len() as u64)
                    .filter(|total| *total <= MAX_TRANSFER_BYTES)
                    .ok_or_else(|| invalid("WebDAV 备份超过 4 GiB 下载上限。"))?;
                writer
                    .write_all(chunk)
                    .map_err(|_| unavailable("写入暂存文件失败。"))?;
            }
            if let Some(length) = content_length {
                if total >= length {
                    break;
                }
            }
        }
        writer
            .sync_all()
            .map_err(|_| unavailable("暂存文件同步失败。"))?;
        if total == 0 {
            return Err(unavailable("WebDAV 返回了空备份。"));
        }
        Ok(total)
    })();
    match result {
        Ok(total) => Ok((temporary, total)),
        Err(error) => {
            let _ = fs::remove_file(&temporary);
            Err(error)
        }
    }
}

// --- API 操作。 ---

pub(super) fn status(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let config = Config::read(service)?;
    let latest = super::latest_local_backup(&service.paths)?;
    let mut response = contracts::initial(contracts::schema("ApiCloudBackupStatusResponse"));
    response["enabled"] = json!(config.enabled);
    response["isConfigured"] = json!(config.configured());
    response["url"] = json!(config.url);
    response["userName"] = json!(config.user);
    response["latestBackupFileName"] = json!(
        latest
            .as_ref()
            .map(|(name, _, _)| name.as_str())
            .unwrap_or("")
    );
    response["latestBackupSizeBytes"] =
        json!(latest.as_ref().map(|(_, size, _)| *size).unwrap_or(0));
    response["backupRoot"] = json!(super::backup_root(&service.paths)?);
    response["storagePolicy"] = json!(CLOUD_STORAGE_POLICY);
    Ok(response)
}
pub(super) fn test_connection(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let config = Config::read(service)?;
    if !config.configured() {
        return Err(invalid("WebDAV 尚未配置，请先保存服务器地址和用户名。"));
    }
    propfind(&config)?;
    let mut response = contracts::initial(contracts::schema("ApiCloudBackupCommandResponse"));
    response["success"] = json!(true);
    response["message"] = json!("WebDAV 连接测试成功。");
    response["backupRoot"] = json!(super::backup_root(&service.paths)?);
    response["storagePolicy"] = json!(CLOUD_STORAGE_POLICY);
    Ok(response)
}
pub(super) fn upload_latest(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let config = Config::read(service)?;
    require_enabled(&config)?;
    let (name, size, path) = super::latest_local_backup(&service.paths)?
        .ok_or_else(|| error(404, "当前没有可上传的数据库备份，请先创建本地备份。"))?;
    if size == 0 {
        return Err(invalid("要上传的本地备份文件为空。"));
    }
    let store = service.store.clone();
    let protector = service.protector.clone();
    let _paths = service.paths.clone();
    let actor_id = actor.id;
    service.jobs.start(
        actor,
        "CloudBackupUpload",
        "上传最新数据库备份到 WebDAV",
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, UPLOAD_LATEST_DATABASE_BACKUP_TO_CLOUD, &[])?;
            let config = Config::read_parts(&store, &protector)?;
            require_enabled(&config)?;
            upload_file(&config, &path, &name)?;
            Ok(TaskOutput {
                file: None,
                detail: format!("已上传 {name}（{size} 字节）到 WebDAV。"),
                destination: None,
                directory: None,
            })
        },
    )
}
pub(super) fn list(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let config = Config::read(service)?;
    require_enabled(&config)?;
    let backups = propfind(&config)?
        .into_iter()
        .map(|item| {
            let mut value = contracts::initial(contracts::schema("ApiCloudBackupItemDto"));
            value["fileName"] = json!(item.file_name);
            value["sizeBytes"] = json!(item.size_bytes);
            value["lastModified"] = json!(item.last_modified);
            value
        })
        .collect::<Vec<_>>();
    let mut response = contracts::initial(contracts::schema("ApiCloudBackupListResponse"));
    response["backups"] = json!(backups);
    response["backupRoot"] = json!(super::backup_root(&service.paths)?);
    response["storagePolicy"] = json!(CLOUD_STORAGE_POLICY);
    Ok(response)
}
pub(super) fn download(service: &NativeService, actor: &Actor, body: &Value) -> Result<Value> {
    auth::authorize(actor, "system.backup", "manage")?;
    let config = Config::read(service)?;
    require_enabled(&config)?;
    let remote_name = super::records_text(body, "remoteFileName");
    if !crate::paths::valid_file_name(&remote_name) || !remote_name.ends_with(".sqlite3") {
        return Err(invalid(
            "只能下载当前 WebDAV 云备份列表中的 SQLite 备份文件。",
        ));
    }
    let store = service.store.clone();
    let protector = service.protector.clone();
    let paths = service.paths.clone();
    let actor_id = actor.id;
    let remote_name = remote_name.to_string();
    service.jobs.start(
        actor,
        "CloudBackupDownload",
        "下载并验证 WebDAV 数据库备份",
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, DOWNLOAD_CLOUD_DATABASE_BACKUP, &[])?;
            let config = Config::read_parts(&store, &protector)?;
            require_enabled(&config)?;
            let staging_root = super::cloud_staging_root(&paths)?;
            let imported = (|| {
                let (temporary, size) = download_file(&config, &remote_name, &staging_root)?;
                if size == 0 {
                    return Err(invalid("下载到的备份为空。"));
                }
                let bytes = fs::read(&temporary)?;
                if !bytes.starts_with(SQLITE_MAGIC) {
                    return Err(invalid("下载到的文件不是有效的 SQLite 备份。"));
                }
                if store.provider()? == "SQLite" {
                    store.connection()?.verify_backup(&temporary)?;
                }
                let target =
                    super::managed_file(&super::backup_root(&paths)?, &remote_name, "sqlite3")?;
                super::sealed::atomic_write(&target, &bytes)?;
                Ok((size, target))
            })();
            let _ = fs::remove_file(staging_root.join(format!(
                ".download-{}.tmp",
                crate::paths::nonce().unwrap_or_default()
            )));
            let (size, target) = imported?;
            Ok(TaskOutput {
                file: Some(FileOutput {
                    file_name: remote_name.clone(),
                    media_type: "application/x-sqlite3".into(),
                    content: Vec::new(),
                }),
                detail: format!("{remote_name}（{size} 字节）已通过校验并导入受管备份目录。"),
                destination: Some(target),
                directory: None,
            })
        },
    )
}
