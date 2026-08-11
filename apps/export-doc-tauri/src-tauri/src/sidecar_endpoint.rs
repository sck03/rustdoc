use std::{
    error::Error,
    fs,
    io::{Read, Write},
    net::TcpStream,
    path::{Path, PathBuf},
    process::Child,
    thread,
    time::{Duration, Instant},
};

use crate::{runtime_paths::RuntimePaths, sidecar::DESKTOP_ACCESS_TOKEN_ENV};

const MAX_ENDPOINT_PUBLICATION_BYTES: u64 = 4096;

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct SidecarEndpointPublication {
    schema_version: u32,
    api_base_url: String,
    process_id: u32,
}

pub(crate) fn create_sidecar_endpoint_file(
    paths: &RuntimePaths,
) -> Result<PathBuf, Box<dyn Error>> {
    let endpoint_root = paths.data_root.join("Cache").join("Sidecar");
    fs::create_dir_all(&endpoint_root)?;
    restrict_endpoint_directory(&endpoint_root)?;
    let mut nonce = [0_u8; 16];
    getrandom::getrandom(&mut nonce)
        .map_err(|error| format!("Failed to create sidecar endpoint nonce: {error}"))?;
    Ok(endpoint_root.join(format!("endpoint-{}.json", to_hex(&nonce))))
}

pub(crate) fn resolve_desktop_access_token() -> Result<String, Box<dyn Error>> {
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

pub(crate) fn wait_for_endpoint_and_health(
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
            // The API writes the endpoint publication atomically, but antivirus
            // and network filesystems can still expose a short transient read.
            // Keep polling until the launch deadline instead of terminating a
            // sidecar that is already healthy.
            Err(error) if is_transient_publication_error(&error) => {}
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
    if !metadata.is_file() || metadata.len() == 0 {
        return Ok(None);
    }
    if metadata.len() > MAX_ENDPOINT_PUBLICATION_BYTES {
        return Err("API endpoint file exceeds the size limit.".to_owned());
    }
    restrict_endpoint_file(endpoint_file)
        .map_err(|error| format!("Failed to restrict API endpoint file permissions: {error}"))?;

    let content = fs::read_to_string(endpoint_file)
        .map_err(|error| format!("Failed to read API endpoint file: {error}"))?;
    let json = content.strip_prefix('\u{feff}').unwrap_or(&content);
    let publication: SidecarEndpointPublication = serde_json::from_str(json)
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

fn is_transient_publication_error(error: &str) -> bool {
    error.starts_with("Failed to read API endpoint file:")
        || error.starts_with("API endpoint file is invalid:")
}

pub(crate) fn resolve_loopback_authority(api_base_url: &str) -> Result<String, String> {
    let trimmed = api_base_url.trim().trim_end_matches('/');
    let Some(without_scheme) = trimmed.strip_prefix("http://") else {
        return Err("Sidecar communication only supports local HTTP.".to_owned());
    };
    let authority = without_scheme.split('/').next().unwrap_or_default().trim();
    if !authority.starts_with("127.0.0.1:") {
        return Err(format!(
            "Sidecar authority must be 127.0.0.1, got '{authority}'."
        ));
    }
    authority
        .parse::<std::net::SocketAddr>()
        .map_err(|error| format!("Invalid sidecar authority '{authority}': {error}"))?;
    Ok(authority.to_owned())
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

fn to_hex(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut output = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        output.push(HEX[(byte >> 4) as usize] as char);
        output.push(HEX[(byte & 0x0f) as usize] as char);
    }
    output
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

        fs::write(&path, b"\xef\xbb\xbf{\"schemaVersion\":1,\"apiBaseUrl\":\"http://127.0.0.1:5200\",\"processId\":42}").unwrap();
        assert_eq!(
            read_endpoint_publication(&path, 42).unwrap(),
            Some("http://127.0.0.1:5200".to_owned())
        );
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn treats_empty_or_partial_publication_as_transient() {
        let root =
            std::env::temp_dir().join(format!("exportdoc-sidecar-partial-{}", std::process::id()));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("endpoint.json");
        fs::write(&path, []).unwrap();
        assert_eq!(read_endpoint_publication(&path, 42).unwrap(), None);
        fs::write(&path, br#"{"schemaVersion":1"#).unwrap();
        let error = read_endpoint_publication(&path, 42).unwrap_err();
        assert!(is_transient_publication_error(&error));
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
    fn accepts_only_loopback_sidecar_authorities() {
        assert_eq!(
            resolve_loopback_authority("http://127.0.0.1:5188/").unwrap(),
            "127.0.0.1:5188"
        );
        assert!(resolve_loopback_authority("http://192.168.1.20:5188")
            .unwrap_err()
            .contains("127.0.0.1"));
    }
}
