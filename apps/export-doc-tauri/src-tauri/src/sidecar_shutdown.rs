use std::{
    io::{Read, Write},
    net::TcpStream,
    time::Duration,
};

use crate::sidecar_endpoint::resolve_loopback_authority;

const SHUTDOWN_MAINTENANCE_PATH: &str = "/api/system/shutdown-maintenance";
const MAX_SHUTDOWN_RESPONSE_BYTES: u64 = 1024 * 1024;

pub(crate) fn post_shutdown_maintenance(
    api_base_url: &str,
    desktop_access_token: &str,
    timeout: Duration,
) -> Result<(), String> {
    let authority = resolve_loopback_authority(api_base_url)?;
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

    let request = build_shutdown_maintenance_request(&authority, desktop_access_token)?;
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
    let header_text = std::str::from_utf8(&response[..header_end])
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

#[cfg(test)]
mod tests {
    use super::*;

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
        assert!(validate_shutdown_maintenance_response(response.as_bytes())
            .unwrap_err()
            .contains("backup failed"));
    }

    #[test]
    fn decodes_chunked_shutdown_maintenance_response() {
        let response = b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nf\r\n{\"success\":true\r\n1\r\n}\r\n0\r\n\r\n";
        validate_shutdown_maintenance_response(response).unwrap();
    }
}
