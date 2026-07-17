use std::{
    error::Error,
    fs,
    io::{Read, Write},
    net::{TcpListener, TcpStream},
    path::Path,
    process::{Child, Command, Stdio},
    sync::Mutex,
    thread,
    time::{Duration, Instant},
};

use tauri::Manager;

use crate::runtime_paths::RuntimePaths;

pub(crate) const DESKTOP_ACCESS_TOKEN_ENV: &str = "EXPORTDOCMANAGER_DESKTOP_TOKEN";
const SHUTDOWN_MAINTENANCE_PATH: &str = "/api/system/shutdown-maintenance";

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DesktopRuntimeContext {
    api_base_url: String,
    desktop_access_token: String,
    product_edition: &'static str,
}

pub(crate) struct SidecarState {
    child: Mutex<Option<Child>>,
    context: DesktopRuntimeContext,
}

impl SidecarState {
    pub(crate) fn new(child: Child, api_base_url: String, desktop_access_token: String) -> Self {
        Self {
            child: Mutex::new(Some(child)),
            context: DesktopRuntimeContext {
                api_base_url,
                desktop_access_token,
                product_edition: resolve_product_edition(),
            },
        }
    }

    fn runtime_context(&self) -> DesktopRuntimeContext {
        self.context.clone()
    }
}

pub(crate) struct SidecarLaunch {
    pub(crate) api_base_url: String,
    pub(crate) desktop_access_token: String,
    pub(crate) child: Child,
}

pub(crate) fn start_sidecar(paths: &RuntimePaths) -> Result<SidecarLaunch, Box<dyn Error>> {
    let port = reserve_loopback_port()?;
    let listen_url = format!("http://127.0.0.1:{port}");
    let desktop_access_token = resolve_desktop_access_token()?;
    let stdout_log_path = paths.log_root.join("api-sidecar.stdout.log");
    let stderr_log_path = paths.log_root.join("api-sidecar.stderr.log");
    let mut stdout_log = open_append_log_file(&stdout_log_path)?;
    let mut stderr_log = open_append_log_file(&stderr_log_path)?;

    write_sidecar_launch_marker(&mut stdout_log, &listen_url, paths)?;
    write_sidecar_launch_marker(&mut stderr_log, &listen_url, paths)?;

    let mut command = Command::new(&paths.sidecar_path);
    command
        .arg("--urls")
        .arg(&listen_url)
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

    let mut child = command.spawn().map_err(|error| {
        format!(
            "Failed to start API sidecar at '{}': {error}",
            paths.sidecar_path.display()
        )
    })?;

    if let Err(error) = wait_for_health(port, Duration::from_secs(20)) {
        let _ = child.kill();
        let _ = child.wait();
        return Err(format!(
            "{error}. See sidecar logs: '{}' and '{}'.",
            stdout_log_path.display(),
            stderr_log_path.display()
        )
        .into());
    }

    Ok(SidecarLaunch {
        api_base_url: listen_url,
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
        if let Ok(mut child) = state.child.lock() {
            if let Some(mut process) = child.take() {
                let _ = process.kill();
                let _ = process.wait();
            }
        }
    }
}

pub(crate) fn run_shutdown_maintenance(app: &tauri::AppHandle) {
    let Some(state) = app.try_state::<SidecarState>() else {
        return;
    };

    if let Err(error) = post_shutdown_maintenance(&state.runtime_context(), Duration::from_secs(8))
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

fn open_append_log_file(path: &Path) -> Result<fs::File, Box<dyn Error>> {
    fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
        .map_err(|error| format!("Failed to open sidecar log '{}': {error}", path.display()).into())
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

fn reserve_loopback_port() -> Result<u16, Box<dyn Error>> {
    let listener = TcpListener::bind(("127.0.0.1", 0))?;
    let port = listener.local_addr()?.port();
    drop(listener);
    Ok(port)
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

fn wait_for_health(port: u16, timeout: Duration) -> Result<(), String> {
    let deadline = Instant::now() + timeout;
    while Instant::now() < deadline {
        if probe_health(port).unwrap_or(false) {
            return Ok(());
        }

        thread::sleep(Duration::from_millis(250));
    }

    Err(format!(
        "API sidecar did not become healthy on 127.0.0.1:{port}"
    ))
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

    let mut buffer = [0_u8; 512];
    let read = stream
        .read(&mut buffer)
        .map_err(|error| format!("Failed to read shutdown maintenance response: {error}"))?;
    if read == 0 {
        return Err("Shutdown maintenance returned an empty response.".to_owned());
    }

    let response = String::from_utf8_lossy(&buffer[..read]);
    if response.starts_with("HTTP/1.1 200") || response.starts_with("HTTP/1.0 200") {
        Ok(())
    } else {
        Err(format!(
            "Shutdown maintenance returned unexpected status: {}",
            response.lines().next().unwrap_or("empty response")
        ))
    }
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

fn probe_health(port: u16) -> std::io::Result<bool> {
    let mut stream = TcpStream::connect_timeout(
        &format!("127.0.0.1:{port}")
            .parse()
            .expect("valid loopback address"),
        Duration::from_millis(300),
    )?;
    stream.set_read_timeout(Some(Duration::from_millis(500)))?;
    stream.write_all(
        format!("GET /healthz HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n")
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
    fn normalizes_product_edition_for_sidecar_launch() {
        assert_eq!(normalize_product_edition(Some("Document")), "Document");
        assert_eq!(normalize_product_edition(Some("Sales")), "Sales");
        assert_eq!(normalize_product_edition(Some("Full")), "Full");
        assert_eq!(normalize_product_edition(Some("unknown")), "Full");
        assert_eq!(normalize_product_edition(None), "Full");
    }
}
