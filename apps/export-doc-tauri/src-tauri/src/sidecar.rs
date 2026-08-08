use std::{
    error::Error,
    fs,
    io::{Read, Write},
    net::TcpStream,
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    sync::{
        atomic::{AtomicBool, Ordering},
        Mutex,
    },
    thread,
    time::{Duration, Instant},
};

use tauri::Manager;

use crate::runtime_paths::RuntimePaths;

pub(crate) const DESKTOP_ACCESS_TOKEN_ENV: &str = "EXPORTDOCMANAGER_DESKTOP_TOKEN";
const SHUTDOWN_MAINTENANCE_PATH: &str = "/api/system/shutdown-maintenance";
const MAX_SHUTDOWN_RESPONSE_BYTES: u64 = 1024 * 1024;
const MAX_ENDPOINT_PUBLICATION_BYTES: u64 = 4096;

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DesktopRuntimeContext {
    api_base_url: String,
    desktop_access_token: String,
    product_edition: &'static str,
    platform: &'static str,
    single_window_station_capable: bool,
}

pub(crate) struct SidecarState {
    child: Mutex<Option<Child>>,
    context: DesktopRuntimeContext,
    shutdown_maintenance_started: AtomicBool,
}

impl SidecarState {
    pub(crate) fn new(child: Child, api_base_url: String, desktop_access_token: String) -> Self {
        Self {
            child: Mutex::new(Some(child)),
            context: DesktopRuntimeContext {
                api_base_url,
                desktop_access_token,
                product_edition: resolve_product_edition(),
                platform: std::env::consts::OS,
                single_window_station_capable: cfg!(target_os = "windows"),
            },
            shutdown_maintenance_started: AtomicBool::new(false),
        }
    }

    fn runtime_context(&self) -> DesktopRuntimeContext {
        self.context.clone()
    }

    fn try_begin_shutdown_maintenance(&self) -> bool {
        !self
            .shutdown_maintenance_started
            .swap(true, Ordering::AcqRel)
    }
}

pub(crate) struct SidecarLaunch {
    pub(crate) api_base_url: String,
    pub(crate) desktop_access_token: String,
    pub(crate) child: Child,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct SidecarEndpointPublication {
    schema_version: u32,
    api_base_url: String,
    process_id: u32,
}

pub(crate) fn start_sidecar(paths: &RuntimePaths) -> Result<SidecarLaunch, Box<dyn Error>> {
    let listen_url = "http://127.0.0.1:0";
    let desktop_access_token = resolve_desktop_access_token()?;
    let endpoint_file = create_sidecar_endpoint_file(paths)?;
    let stdout_log_path = paths.log_root.join("api-sidecar.stdout.log");
    let stderr_log_path = paths.log_root.join("api-sidecar.stderr.log");
    let mut stdout_log = crate::log_rotation::open_append_log_file(&stdout_log_path)?;
    let mut stderr_log = crate::log_rotation::open_append_log_file(&stderr_log_path)?;

    write_sidecar_launch_marker(&mut stdout_log, listen_url, paths)?;
    write_sidecar_launch_marker(&mut stderr_log, listen_url, paths)?;

    let mut command = Command::new(&paths.sidecar_path);
    command
        .arg("--urls")
        .arg(listen_url)
        .arg("--endpoint-file")
        .arg(&endpoint_file)
        .arg("--app-root")
        .arg(&paths.app_root)
        .arg("--data-root")
        .arg(&paths.data_root)
        .arg("--product-edition")
        .arg(resolve_product_edition())
        .env("EXPORTDOCMANAGER_DATA_ROOT", &paths.data_root)
        .env(DESKTOP_ACCESS_TOKEN_ENV, &desktop_access_token)
        .stdin(Stdio::null())
        .stdout(Stdio::from(stdout_log))
        .stderr(Stdio::from(stderr_log));

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        command.creation_flags(CREATE_NO_WINDOW);
    }

    let mut child = match command.spawn() {
        Ok(child) => child,
        Err(error) => {
            let _ = fs::remove_file(&endpoint_file);
            return Err(format!(
                "Failed to start API sidecar at '{}': {error}",
                paths.sidecar_path.display()
            )
            .into());
        }
    };

    let api_base_url =
        wait_for_endpoint_and_health(&mut child, &endpoint_file, Duration::from_secs(20));
    let _ = fs::remove_file(&endpoint_file);
    let api_base_url = match api_base_url {
        Ok(value) => value,
        Err(error) => {
            let _ = child.kill();
            let _ = child.wait();
            return Err(format!(
                "{error}. See sidecar logs: '{}' and '{}'.",
                stdout_log_path.display(),
                stderr_log_path.display()
            )
            .into());
        }
    };

    Ok(SidecarLaunch {
        api_base_url,
        desktop_access_token,
        child,
    })
}

fn resolve_product_edition() -> &'static str {
    normalize_product_edition(option_env!("EXPORTDOCMANAGER_PRODUCT_EDITION"))
}

fn normalize_product_edition(value: Option<&str>) -> &'static str {
    match value {
        Some("Document") => "Document",
        Some("Sales") => "Sales",
        _ => "Full",
    }
}

pub(crate) fn stop_sidecar(app: &tauri::AppHandle) {
    if let Some(state) = app.try_state::<SidecarState>() {
        state
            .shutdown_maintenance_started
            .store(true, Ordering::Release);
        if let Ok(mut child) = state.child.lock() {
            if let Some(mut process) = child.take() {
                let _ = process.kill();
                let _ = process.wait();
            }
        }
    }
}

pub(crate) fn begin_graceful_shutdown(app: &tauri::AppHandle) -> bool {
    let Some(state) = app.try_state::<SidecarState>() else {
        app.exit(0);
        return true;
    };
    if !state.try_begin_shutdown_maintenance() {
        return false;
    }

    let context = state.runtime_context();
    let app_handle = app.clone();
    thread::spawn(move || {
        if let Err(error) = post_shutdown_maintenance(&context, Duration::from_secs(30)) {
            eprintln!("Shutdown maintenance skipped or failed: {error}");
        }

        stop_sidecar(&app_handle);
        app_handle.exit(0);
    });
    true
}

/// Runs the shutdown maintenance synchronously for updater callbacks.
///
/// The normal window-close path uses [`begin_graceful_shutdown`] so the UI
/// thread is never blocked. Tauri's updater callback, however, is invoked as
/// part of the restart handshake and must finish the API cleanup before the
/// process is replaced, so it keeps the small synchronous wrapper that the
/// updater module can call.
pub(crate) fn run_shutdown_maintenance(app: &tauri::AppHandle) {
    let Some(state) = app.try_state::<SidecarState>() else {
        return;
    };
    if !state.try_begin_shutdown_maintenance() {
        return;
    }

    if let Err(error) = post_shutdown_maintenance(&state.runtime_context(), Duration::from_secs(30))
    {
        eprintln!("Shutdown maintenance skipped or failed: {error}");
    }
}

#[tauri::command]
pub(crate) fn get_desktop_runtime_context(
    state: tauri::State<'_, SidecarState>,
) -> DesktopRuntimeContext {
    state.runtime_context()
}

fn write_sidecar_launch_marker(
    log: &mut fs::File,
    listen_url: &str,
    paths: &RuntimePaths,
) -> Result<(), Box<dyn Error>> {
    writeln!(
        log,
        "\n=== Starting API sidecar at {listen_url}; app_root='{}'; data_root='{}' ===",
        paths.app_root.display(),
        paths.data_root.display()
    )
    .map_err(|error| format!("Failed to write sidecar launch marker: {error}").into())
}

fn create_sidecar_endpoint_file(paths: &RuntimePaths) -> Result<PathBuf, Box<dyn Error>> {
    let endpoint_root = paths.data_root.join("Cache").join("Sidecar");
    fs::create_dir_all(&endpoint_root)?;
    restrict_endpoint_directory(&endpoint_root)?;
    let mut nonce = [0_u8; 16];
    getrandom::getrandom(&mut nonce)
        .map_err(|error| format!("Failed to create sidecar endpoint nonce: {error}"))?;
    Ok(endpoint_root.join(format!("endpoint-{}.json", to_hex(&nonce))))
}

#[cfg(unix)]
fn restrict_endpoint_directory(path: &Path) -> Result<(), Box<dyn Error>> {
    use std::os::unix::fs::PermissionsExt;
    fs::set_permissions(path, fs::Permissions::from_mode(0o700))?;
    Ok(())
}

#[cfg(not(unix))]
fn restrict_endpoint_directory(_path: &Path) -> Result<(), Box<dyn Error>> {
    Ok(())
}

#[cfg(unix)]
fn restrict_endpoint_file(path: &Path) -> Result<(), Box<dyn Error>> {
    use std::os::unix::fs::PermissionsExt;
    fs::set_permissions(path, fs::Permissions::from_mode(0o600))?;
    Ok(())
}

#[cfg(not(unix))]
fn restrict_endpoint_file(_path: &Path) -> Result<(), Box<dyn Error>> {
    Ok(())
}

fn resolve_desktop_access_token() -> Result<String, Box<dyn Error>> {
    if let Some(token) = std::env::var_os(DESKTOP_ACCESS_TOKEN_ENV)
        .map(|value| value.to_string_lossy().trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        return Ok(token);
    }

    let mut bytes = [0_u8; 32];
    getrandom::getrandom(&mut bytes)
        .map_err(|error| format!("Failed to generate desktop access token: {error}"))?;
    Ok(to_hex(&bytes))
}

fn to_hex(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut output = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        output.push(HEX[(byte >> 4) as usize] as char);
        output.push(HEX[(byte & 0x0f) as usize] as char);
    }

    output
}

fn wait_for_endpoint_and_health(
    child: &mut Child,
    endpoint_file: &Path,
    timeout: Duration,
) -> Result<String, String> {
    let deadline = Instant::now() + timeout;
    while Instant::now() < deadline {
        if let Some(status) = child
            .try_wait()
            .map_err(|error| format!("Failed to inspect API sidecar process: {error}"))?
        {
            return Err(format!(
                "API sidecar exited before publishing its endpoint: {status}"
            ));
        }

        match read_endpoint_publication(endpoint_file, child.id()) {
            Ok(Some(api_base_url)) => {
                let authority = resolve_loopback_authority(&api_base_url)?;
                if probe_health(&authority).unwrap_or(false) {
                    return Ok(api_base_url.trim_end_matches('/').to_owned());
                }
            }
            Ok(None) => {}
            Err(error) => return Err(error),
        }

        thread::sleep(Duration::from_millis(250));
    }

    Err(format!(
        "API sidecar did not publish a healthy loopback endpoint within {} seconds",
        timeout.as_secs()
    ))
}

fn read_endpoint_publication(
    endpoint_file: &Path,
    expected_process_id: u32,
) -> Result<Option<String>, String> {
    let metadata = match fs::metadata(endpoint_file) {
        Ok(value) => value,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(format!("Failed to inspect API endpoint file: {error}")),
    };
    if !metadata.is_file() || metadata.len() == 0 || metadata.len() > MAX_ENDPOINT_PUBLICATION_BYTES
    {
        return Err("API endpoint file has an invalid size or type.".to_owned());
    }
    restrict_endpoint_file(endpoint_file)
        .map_err(|error| format!("Failed to restrict API endpoint file permissions: {error}"))?;

    let content = fs::read_to_string(endpoint_file)
        .map_err(|error| format!("Failed to read API endpoint file: {error}"))?;
    let publication: SidecarEndpointPublication = serde_json::from_str(&content)
        .map_err(|error| format!("API endpoint file is invalid: {error}"))?;
    if publication.schema_version != 1 {
        return Err(format!(
            "Unsupported API endpoint file schema version: {}",
            publication.schema_version
        ));
    }
    if publication.process_id != expected_process_id {
        return Err("API endpoint file belongs to a different process.".to_owned());
    }
    let authority = resolve_loopback_authority(&publication.api_base_url)?;
    let address: std::net::SocketAddr = authority
        .parse()
        .map_err(|error| format!("API endpoint authority is invalid: {error}"))?;
    if address.port() == 0 {
        return Err("API endpoint file still contains the dynamic port placeholder.".to_owned());
    }
    Ok(Some(publication.api_base_url))
}

fn post_shutdown_maintenance(
    context: &DesktopRuntimeContext,
    timeout: Duration,
) -> Result<(), String> {
    let authority = resolve_loopback_authority(&context.api_base_url)?;
    let address = authority
        .parse()
        .map_err(|error| format!("Invalid sidecar authority '{authority}': {error}"))?;
    let mut stream = TcpStream::connect_timeout(&address, timeout).map_err(|error| {
        format!("Failed to connect to sidecar for shutdown maintenance: {error}")
    })?;
    stream
        .set_write_timeout(Some(timeout))
        .map_err(|error| format!("Failed to set shutdown maintenance write timeout: {error}"))?;
    stream
        .set_read_timeout(Some(timeout))
        .map_err(|error| format!("Failed to set shutdown maintenance read timeout: {error}"))?;

    let request = build_shutdown_maintenance_request(&authority, &context.desktop_access_token)?;
    stream
        .write_all(request.as_bytes())
        .map_err(|error| format!("Failed to send shutdown maintenance request: {error}"))?;

    let mut response_bytes = Vec::new();
    stream
        .take(MAX_SHUTDOWN_RESPONSE_BYTES + 1)
        .read_to_end(&mut response_bytes)
        .map_err(|error| format!("Failed to read shutdown maintenance response: {error}"))?;
    if response_bytes.is_empty() {
        return Err("Shutdown maintenance returned an empty response.".to_owned());
    }
    if response_bytes.len() as u64 > MAX_SHUTDOWN_RESPONSE_BYTES {
        return Err("Shutdown maintenance response exceeded the 1 MB safety limit.".to_owned());
    }

    validate_shutdown_maintenance_response(&response_bytes)
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct ShutdownMaintenanceResponse {
    success: bool,
    #[serde(default)]
    message: String,
}

fn validate_shutdown_maintenance_response(response: &[u8]) -> Result<(), String> {
    let header_end = find_subslice(response, b"\r\n\r\n")
        .ok_or_else(|| "Shutdown maintenance returned an invalid HTTP response.".to_owned())?;
    let header_bytes = &response[..header_end];
    let header_text = std::str::from_utf8(header_bytes)
        .map_err(|_| "Shutdown maintenance returned invalid HTTP headers.".to_owned())?;
    let status_line = header_text.lines().next().unwrap_or_default();
    let status_code = status_line
        .split_whitespace()
        .nth(1)
        .and_then(|value| value.parse::<u16>().ok())
        .ok_or_else(|| "Shutdown maintenance returned an invalid HTTP status line.".to_owned())?;
    if !(200..300).contains(&status_code) {
        return Err(format!(
            "Shutdown maintenance returned unexpected status: {status_line}"
        ));
    }

    let body = decode_http_response_body(header_text, &response[header_end + 4..])?;
    let payload: ShutdownMaintenanceResponse = serde_json::from_slice(&body)
        .map_err(|error| format!("Shutdown maintenance returned invalid JSON: {error}"))?;
    if !payload.success {
        return Err(if payload.message.trim().is_empty() {
            "Shutdown maintenance reported failure.".to_owned()
        } else {
            format!(
                "Shutdown maintenance reported failure: {}",
                payload.message.trim()
            )
        });
    }

    Ok(())
}

fn decode_http_response_body(headers: &str, body: &[u8]) -> Result<Vec<u8>, String> {
    let is_chunked = headers.lines().skip(1).any(|line| {
        line.split_once(':').is_some_and(|(name, value)| {
            name.trim().eq_ignore_ascii_case("transfer-encoding")
                && value
                    .split(',')
                    .any(|encoding| encoding.trim().eq_ignore_ascii_case("chunked"))
        })
    });
    if is_chunked {
        return decode_chunked_body(body);
    }

    let content_length = headers.lines().skip(1).find_map(|line| {
        let (name, value) = line.split_once(':')?;
        name.trim()
            .eq_ignore_ascii_case("content-length")
            .then(|| value.trim().parse::<usize>().ok())
            .flatten()
    });
    if let Some(expected) = content_length {
        if body.len() < expected {
            return Err("Shutdown maintenance response body was truncated.".to_owned());
        }
        return Ok(body[..expected].to_vec());
    }

    Ok(body.to_vec())
}

fn decode_chunked_body(body: &[u8]) -> Result<Vec<u8>, String> {
    let mut cursor = 0usize;
    let mut decoded = Vec::new();
    loop {
        let line_end = find_subslice(&body[cursor..], b"\r\n")
            .map(|offset| cursor + offset)
            .ok_or_else(|| "Shutdown maintenance returned malformed chunked data.".to_owned())?;
        let size_text = std::str::from_utf8(&body[cursor..line_end])
            .map_err(|_| "Shutdown maintenance returned an invalid chunk size.".to_owned())?;
        let size_token = size_text.split(';').next().unwrap_or_default().trim();
        let chunk_size = usize::from_str_radix(size_token, 16)
            .map_err(|_| "Shutdown maintenance returned an invalid chunk size.".to_owned())?;
        cursor = line_end + 2;
        if chunk_size == 0 {
            return Ok(decoded);
        }
        let chunk_end = cursor
            .checked_add(chunk_size)
            .ok_or_else(|| "Shutdown maintenance chunk size overflowed.".to_owned())?;
        if chunk_end + 2 > body.len() || &body[chunk_end..chunk_end + 2] != b"\r\n" {
            return Err("Shutdown maintenance returned truncated chunked data.".to_owned());
        }
        decoded.extend_from_slice(&body[cursor..chunk_end]);
        if decoded.len() as u64 > MAX_SHUTDOWN_RESPONSE_BYTES {
            return Err("Shutdown maintenance response exceeded the 1 MB safety limit.".to_owned());
        }
        cursor = chunk_end + 2;
    }
}

fn find_subslice(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack
        .windows(needle.len())
        .position(|window| window == needle)
}

fn resolve_loopback_authority(api_base_url: &str) -> Result<String, String> {
    let trimmed = api_base_url.trim().trim_end_matches('/');
    let Some(without_scheme) = trimmed.strip_prefix("http://") else {
        return Err("Shutdown maintenance only supports the local HTTP sidecar.".to_owned());
    };
    let authority = without_scheme.split('/').next().unwrap_or_default().trim();

    if !authority.starts_with("127.0.0.1:") {
        return Err(format!(
            "Shutdown maintenance sidecar authority must be 127.0.0.1, got '{authority}'."
        ));
    }

    authority
        .parse::<std::net::SocketAddr>()
        .map_err(|error| format!("Invalid sidecar authority '{authority}': {error}"))?;

    Ok(authority.to_owned())
}

fn build_shutdown_maintenance_request(
    host: &str,
    desktop_access_token: &str,
) -> Result<String, String> {
    if host.contains(['\r', '\n']) || desktop_access_token.contains(['\r', '\n']) {
        return Err("Shutdown maintenance headers contain invalid control characters.".to_owned());
    }

    Ok(format!(
        "POST {SHUTDOWN_MAINTENANCE_PATH} HTTP/1.1\r\n\
         Host: {host}\r\n\
         X-ExportDocManager-Desktop-Token: {desktop_access_token}\r\n\
         Content-Length: 0\r\n\
         Connection: close\r\n\
         \r\n"
    ))
}

fn probe_health(authority: &str) -> std::io::Result<bool> {
    let mut stream = TcpStream::connect_timeout(
        &authority.parse().expect("validated loopback authority"),
        Duration::from_millis(300),
    )?;
    stream.set_read_timeout(Some(Duration::from_millis(500)))?;
    stream.write_all(
        format!("GET /healthz HTTP/1.1\r\nHost: {authority}\r\nConnection: close\r\n\r\n")
            .as_bytes(),
    )?;

    let mut buffer = [0_u8; 256];
    let read = stream.read(&mut buffer)?;
    let response = String::from_utf8_lossy(&buffer[..read]);
    Ok(response.starts_with("HTTP/1.1 200") || response.starts_with("HTTP/1.0 200"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encodes_desktop_access_token_bytes_as_hex() {
        assert_eq!(to_hex(&[0x00, 0x5a, 0xff]), "005aff");
    }

    #[test]
    fn reads_process_bound_sidecar_endpoint_publication() {
        let root =
            std::env::temp_dir().join(format!("exportdoc-sidecar-endpoint-{}", std::process::id()));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("endpoint.json");
        fs::write(
            &path,
            r#"{"schemaVersion":1,"apiBaseUrl":"http://127.0.0.1:5199","processId":42}"#,
        )
        .unwrap();

        assert_eq!(
            read_endpoint_publication(&path, 42).unwrap(),
            Some("http://127.0.0.1:5199".to_owned())
        );
        assert!(read_endpoint_publication(&path, 43)
            .unwrap_err()
            .contains("different process"));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn rejects_unresolved_dynamic_sidecar_endpoint() {
        let root = std::env::temp_dir().join(format!(
            "exportdoc-sidecar-endpoint-zero-{}",
            std::process::id()
        ));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("endpoint.json");
        fs::write(
            &path,
            r#"{"schemaVersion":1,"apiBaseUrl":"http://127.0.0.1:0","processId":42}"#,
        )
        .unwrap();

        assert!(read_endpoint_publication(&path, 42)
            .unwrap_err()
            .contains("dynamic port placeholder"));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn resolves_shutdown_maintenance_authority_for_loopback_sidecar() {
        assert_eq!(
            resolve_loopback_authority("http://127.0.0.1:5188/").unwrap(),
            "127.0.0.1:5188"
        );
    }

    #[test]
    fn rejects_shutdown_maintenance_authority_outside_loopback_sidecar() {
        let error = resolve_loopback_authority("http://192.168.1.20:5188").unwrap_err();

        assert!(error.contains("127.0.0.1"));
    }

    #[test]
    fn builds_shutdown_maintenance_desktop_token_request() {
        let request =
            build_shutdown_maintenance_request("127.0.0.1:5188", "desktop-secret").unwrap();

        assert!(request.starts_with("POST /api/system/shutdown-maintenance HTTP/1.1\r\n"));
        assert!(request.contains("Host: 127.0.0.1:5188\r\n"));
        assert!(request.contains("X-ExportDocManager-Desktop-Token: desktop-secret\r\n"));
        assert!(request.ends_with("\r\n\r\n"));
    }

    #[test]
    fn validates_complete_shutdown_maintenance_json_response() {
        let body = br#"{"success":true,"message":"done"}"#;
        let response = format!(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {}\r\n\r\n{}",
            body.len(),
            String::from_utf8_lossy(body)
        );

        validate_shutdown_maintenance_response(response.as_bytes()).unwrap();
    }

    #[test]
    fn rejects_http_200_when_shutdown_maintenance_reports_failure() {
        let body = br#"{"success":false,"message":"backup failed"}"#;
        let response = format!(
            "HTTP/1.1 200 OK\r\nContent-Length: {}\r\n\r\n{}",
            body.len(),
            String::from_utf8_lossy(body)
        );

        let error = validate_shutdown_maintenance_response(response.as_bytes()).unwrap_err();
        assert!(error.contains("backup failed"));
    }

    #[test]
    fn decodes_chunked_shutdown_maintenance_response() {
        let response = b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nf\r\n{\"success\":true\r\n1\r\n}\r\n0\r\n\r\n";

        validate_shutdown_maintenance_response(response).unwrap();
    }

    #[test]
    fn normalizes_product_edition_for_sidecar_launch() {
        assert_eq!(normalize_product_edition(Some("Document")), "Document");
        assert_eq!(normalize_product_edition(Some("Sales")), "Sales");
        assert_eq!(normalize_product_edition(Some("Full")), "Full");
        assert_eq!(normalize_product_edition(Some("unknown")), "Full");
        assert_eq!(normalize_product_edition(None), "Full");
    }
}
