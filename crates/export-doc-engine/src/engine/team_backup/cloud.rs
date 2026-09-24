//! WebDAV 云备份。配置来自数据库设置,密码保存在受密封凭证区。
//! 传输复用受控 ureq 客户端:HTTPS 使用平台证书校验,解析地址受策略限制,
//! 流式下载仍写入受管暂存文件并在失败时清理。
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
    path::{Path, PathBuf},
    time::Duration,
};

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

    /// 后台任务复用同一读取路径:任务开始时重新读取当前设置与受密封保护的密码。
    fn read_parts(store: &Store, protector: &crate::secrets::Protector) -> Result<Self> {
        let settings = settings::current(store)?;
        let web_dav = &settings["webDav"];
        let password = settings::credential(store, protector, "/webDav/password")?.to_string();
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

fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut output = Vec::with_capacity(bytes.len());
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

fn authorization(config: &Config) -> (String, String) {
    (
        "Authorization".into(),
        format!(
            "Basic {}",
            BASE64_STANDARD.encode(format!("{}:{}", config.user, config.password))
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
    export_doc_network::controlled_agent(true, timeout)
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

// --- WebDAV 操作。 ---

use crate::api::ApiError;

struct CloudItem {
    file_name: String,
    size_bytes: u64,
    last_modified: String,
}

/// PROPFIND 列出远端备份。
fn propfind(config: &Config) -> Result<Vec<CloudItem>> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config);
    let body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>";
    let response = request(
        &endpoint,
        "PROPFIND",
        &endpoint.propfind_url(),
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

/// 只解析 displayname / getcontentlength / getlastmodified / resourcetype 四个本地名,
/// 命名空间前缀(d:、D: 或默认)不影响匹配。
fn parse_propfind(xml: &str) -> Result<Vec<CloudItem>> {
    use quick_xml::Reader;
    use quick_xml::events::Event;

    struct Pending {
        display_name: String,
        href: String,
        size_bytes: u64,
        last_modified: String,
        collection: bool,
    }

    impl Default for Pending {
        fn default() -> Self {
            Self {
                display_name: String::new(),
                href: String::new(),
                size_bytes: 0,
                last_modified: String::new(),
                collection: false,
            }
        }
    }

    fn local_name(name: &str) -> &str {
        name.rsplit(':').next().unwrap_or(name)
    }

    let mut reader = Reader::from_str(xml);
    reader.config_mut().trim_text(true);
    let mut response: Option<Pending> = None;
    let mut text_target: Option<String> = None;
    let mut items = Vec::new();
    loop {
        match reader
            .read_event()
            .map_err(|_| unavailable("WebDAV PROPFIND 返回了无效 XML。"))?
        {
            Event::Start(event) => {
                let name = local_name(event.name().as_ref()).to_string();
                if name == "response" {
                    response = Some(Pending::default());
                }
                if response.is_some()
                    && matches!(
                        name.as_str(),
                        "displayname"
                            | "href"
                            | "getcontentlength"
                            | "getlastmodified"
                            | "resourcetype"
                    )
                {
                    text_target = Some(name);
                }
            }
            Event::Empty(event) => {
                let qualified = event.name();
                let name = local_name(qualified.as_ref());
                if response.is_some() && name == "collection" {
                    if let Some(pending) = response.as_mut() {
                        pending.collection = true;
                    }
                }
            }
            Event::Text(event) => {
                let Some(target) = text_target.as_deref() else {
                    continue;
                };
                let value = event.as_ref().trim().to_string();
                if let Some(pending) = response.as_mut() {
                    match target {
                        "displayname" => pending.display_name = value,
                        "href" => pending.href = value,
                        "getcontentlength" => {
                            pending.size_bytes = value.parse().unwrap_or(0);
                        }
                        "getlastmodified" => {
                            pending.last_modified = chrono::DateTime::parse_from_rfc2822(&value)
                                .map(|time| time.with_timezone(&chrono::Utc).to_rfc3339())
                                .unwrap_or_default();
                        }
                        "resourcetype" if value.contains("collection") => {
                            pending.collection = true;
                        }
                        _ => {}
                    }
                }
            }
            Event::End(event) => {
                let qualified = event.name();
                let name = local_name(qualified.as_ref());
                if matches!(
                    name,
                    "displayname"
                        | "href"
                        | "getcontentlength"
                        | "getlastmodified"
                        | "resourcetype"
                ) {
                    text_target = None;
                }
                if name == "response" {
                    let Some(pending) = response.take() else {
                        continue;
                    };
                    if pending.collection {
                        continue;
                    }
                    let file_name = if !pending.display_name.trim().is_empty() {
                        pending.display_name.trim().to_string()
                    } else {
                        percent_decode(pending.href.trim())
                    };
                    if file_name.is_empty() || !file_name.ends_with(".sqlite3") {
                        continue;
                    }
                    items.push(CloudItem {
                        file_name,
                        size_bytes: pending.size_bytes,
                        last_modified: pending.last_modified,
                    });
                }
            }
            Event::Eof => break,
            _ => {}
        }
    }
    items.sort_by(|left, right| {
        right
            .last_modified
            .cmp(&left.last_modified)
            .then_with(|| right.file_name.cmp(&left.file_name))
    });
    Ok(items)
}

/// 上传一个本地备份文件到 WebDAV。流式写入仍保留取消边界。
fn upload_file(config: &Config, path: &Path, file_name: &str) -> Result<()> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config);
    let metadata = fs::metadata(path)?;
    if !metadata.is_file() || metadata.len() == 0 {
        return Err(invalid("要上传的本地备份文件为空或不存在。"));
    }
    if metadata.len() > MAX_TRANSFER_BYTES {
        return Err(invalid("本地备份超过 4 GiB 上传上限。"));
    }
    let remote = remote_path(&endpoint, file_name)?;
    let mut reader = std::io::BufReader::new(fs::File::open(path)?);
    let mut response = agent(TRANSFER_TIMEOUT)
        .put(&remote)
        .header(name.as_str(), value)
        .header("Content-Type", "application/octet-stream")
        .send(ureq::SendBody::from_reader(&mut reader))
        .map_err(|error| match error {
            ureq::Error::Timeout(_) => unavailable("WebDAV 上传超时。"),
            _ => unavailable(format!("WebDAV 上传失败:{error}")),
        })?;
    let status = response.status().as_u16();
    let mut body_reader = response.body_mut().as_reader();
    let body = read_limited(&mut body_reader, ERROR_LIMIT, "WebDAV 响应超过容量上限。")?;
    if !(200..300).contains(&status) {
        return Err(failure(status, &body));
    }
    Ok(())
}

/// 下载远端备份到受管暂存目录,返回暂存路径与字节数。失败时清理暂存文件。
fn download_file(config: &Config, file_name: &str, staging_root: &Path) -> Result<(PathBuf, u64)> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config);
    let remote = remote_path(&endpoint, file_name)?;
    let temporary = staging_root.join(format!(
        ".download-{}.tmp",
        crate::paths::nonce().map_err(unavailable)?
    ));
    crate::paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
    let result = (|| {
        let mut response = agent(TRANSFER_TIMEOUT)
            .get(&remote)
            .header(name.as_str(), value)
            .call()
            .map_err(|error| match error {
                ureq::Error::Timeout(_) => unavailable("WebDAV 下载超时。"),
                _ => unavailable(format!("WebDAV 下载失败:{error}")),
            })?;
        let status = response.status().as_u16();
        let mut reader = response.body_mut().as_reader();
        if !(200..300).contains(&status) {
            let head = read_limited(&mut reader, ERROR_LIMIT, "WebDAV 错误响应超过容量上限。")?;
            return Err(failure(status, &head));
        }
        let mut writer = fs::File::create(&temporary)?;
        let mut buffer = [0u8; TRANSFER_BUFFER];
        let mut total = 0u64;
        loop {
            crate::operation::check()?;
            let length = reader
                .read(&mut buffer)
                .map_err(|_| unavailable("读取 WebDAV 下载流失败。"))?;
            if length == 0 {
                break;
            }
            total = total
                .checked_add(length as u64)
                .filter(|total| *total <= MAX_TRANSFER_BYTES)
                .ok_or_else(|| invalid("WebDAV 备份超过 4 GiB 下载上限。"))?;
            writer
                .write_all(&buffer[..length])
                .map_err(|_| unavailable("写入暂存文件失败。"))?;
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
        return Err(invalid("WebDAV 尚未配置,请先保存服务器地址和用户名。"));
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
        .ok_or_else(|| error(404, "当前没有可上传的数据库备份,请先创建本地备份。"))?;
    if size == 0 {
        return Err(invalid("要上传的本地备份文件为空。"));
    }
    let store = service.store.clone();
    let protector = service.protector.clone();
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
                detail: format!("已上传 {name}({size} 字节)到 WebDAV。"),
                destination: None,
                directory: None,
                managed_file: None,
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
                detail: format!("{remote_name}({size} 字节)已通过校验并导入受管备份目录。"),
                destination: Some(target),
                directory: None,
                managed_file: None,
            })
        },
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn https_paths_namespaced_dav_xml_and_size_limits_are_enforced() {
        let endpoint = parse_url("https://dav.example.invalid/team/backups").unwrap();
        assert_eq!(
            endpoint.request_url("%E5%A4%87%E4%BB%BD.sqlite3"),
            "https://dav.example.invalid/team/backups/%E5%A4%87%E4%BB%BD.sqlite3"
        );
        assert_eq!(
            endpoint.propfind_url(),
            "https://dav.example.invalid/team/backups/"
        );
        assert!(parse_url("ftp://dav.example.invalid/backups").is_err());
        assert!(parse_url("https://user:secret@dav.example.invalid/backups").is_err());
        assert!(remote_path(&endpoint, "../escape.sqlite3").is_err());
        let xml = r#"<D:multistatus xmlns:D="DAV:"><D:response><D:href>/team/backups/2026-09-20.sqlite3</D:href><D:propstat><D:prop><D:displayname>2026-09-20.sqlite3</D:displayname><D:getcontentlength>4096</D:getcontentlength><D:getlastmodified>Sun, 20 Sep 2026 08:00:00 GMT</D:getlastmodified></D:prop></D:propstat></D:response><D:response><D:resourcetype><D:collection/></D:resourcetype></D:response></D:multistatus>"#;
        let items = parse_propfind(xml).unwrap();
        assert_eq!(items.len(), 1);
        assert_eq!(
            (
                items[0].file_name.as_str(),
                items[0].size_bytes,
                items[0].last_modified.as_str()
            ),
            ("2026-09-20.sqlite3", 4096, "2026-09-20T08:00:00+00:00")
        );
        let mut reader = std::io::Cursor::new(vec![0u8; 65]);
        assert!(read_limited(&mut reader, 64, "too large").is_err());
        let mut reader = std::io::Cursor::new(vec![0u8; 64]);
        assert_eq!(
            read_limited(&mut reader, 64, "too large").unwrap().len(),
            64
        );
    }
}
