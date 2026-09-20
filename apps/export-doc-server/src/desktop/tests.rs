use super::*;
use axum::{
    body::{Body, to_bytes},
    http::StatusCode,
};
use export_doc_engine::paths::RuntimePaths;
use serde_json::{Value, json};
use tower::ServiceExt;

struct Fixture {
    root: std::path::PathBuf,
    service: Arc<NativeService>,
}
impl Fixture {
    fn new() -> Self {
        let root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../../.codex-runtime/desktop-http-tests")
            .join(nonce().unwrap());
        std::fs::create_dir_all(&root).unwrap();
        let root = std::fs::canonicalize(root).unwrap();
        let service =
            NativeService::open(RuntimePaths::server(&root, &root.join("Data")).unwrap()).unwrap();
        Self { root, service }
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        self.service.close().unwrap();
        // The store lock is released with the final Arc; cleanup is intentionally
        // left to the workspace cleanup script on Windows.
        assert!(
            self.root.starts_with(
                std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                    .join("../..")
                    .canonicalize()
                    .unwrap()
            )
        );
    }
}

#[tokio::test]
async fn desktop_http_preserves_original_login_and_rejects_untrusted_callers() {
    let fixture = Fixture::new();
    let authority = "127.0.0.1:51888";
    let secret = "isolated-test-desktop-capability";
    let origin = "http://tauri.localhost";
    let app = desktop_router(fixture.service.clone(), secret, authority, false);
    for (host, origin, token, expected) in [
        (authority, origin, "", StatusCode::FORBIDDEN),
        (authority, origin, "wrong", StatusCode::FORBIDDEN),
        ("evil.example:51888", origin, secret, StatusCode::FORBIDDEN),
        (
            authority,
            "https://evil.example",
            secret,
            StatusCode::FORBIDDEN,
        ),
        (
            authority,
            "http://127.0.0.1:5173",
            secret,
            StatusCode::FORBIDDEN,
        ),
    ] {
        let request = Request::builder()
            .method("POST")
            .uri("/api/auth/login")
            .header(header::HOST, host)
            .header(header::ORIGIN, origin)
            .header("x-exportdocmanager-desktop-token", token)
            .header(header::CONTENT_TYPE, "application/json")
            .body(Body::from(
                json!({"username":"admin","password":""}).to_string(),
            ))
            .unwrap();
        assert_eq!(
            app.clone().oneshot(request).await.unwrap().status(),
            expected
        );
    }
    let preflight = Request::builder()
        .method("OPTIONS")
        .uri("/api/auth/login")
        .header(header::HOST, authority)
        .header(header::ORIGIN, origin)
        .header("access-control-request-method", "POST")
        .header(
            "access-control-request-headers",
            "content-type,x-exportdocmanager-desktop-token",
        )
        .body(Body::empty())
        .unwrap();
    let response = app.clone().oneshot(preflight).await.unwrap();
    assert!(response.status().is_success());
    assert_eq!(response.headers()["access-control-allow-origin"], origin);
    assert_eq!(
        response.headers()["access-control-allow-credentials"],
        "true"
    );

    let login = Request::builder()
        .method("POST")
        .uri("/api/auth/login")
        .header(header::HOST, authority)
        .header(header::ORIGIN, origin)
        .header("x-exportdocmanager-desktop-token", secret)
        .header(header::CONTENT_TYPE, "application/json")
        .body(Body::from(
            json!({"username":"admin","password":""}).to_string(),
        ))
        .unwrap();
    let response = app.clone().oneshot(login).await.unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    let body: Value =
        serde_json::from_slice(&to_bytes(response.into_body(), 1024 * 1024).await.unwrap())
            .unwrap();
    let session = body["accessToken"].as_str().unwrap();
    let image_path = fixture.root.join("preview.png");
    let png = b"\x89PNG\r\n\x1a\n";
    std::fs::write(&image_path, png).unwrap();
    let query = url::form_urlencoded::Serializer::new(String::new())
        .append_pair("filePath", &image_path.to_string_lossy())
        .finish();
    let preview_path = format!("/api/tools/ocr/preview-image?{query}");
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri(&preview_path)
                .header(header::HOST, authority)
                .header("x-exportdocmanager-desktop-token", secret)
                .header(header::AUTHORIZATION, format!("Bearer {session}"))
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    assert_eq!(response.headers()[header::CONTENT_TYPE], "image/png");
    assert_eq!(
        to_bytes(response.into_body(), 1024).await.unwrap().as_ref(),
        png
    );
    let network = crate::router(fixture.service.clone(), None);
    let denied = network
        .oneshot(
            Request::builder()
                .uri(&preview_path)
                .header(header::AUTHORIZATION, format!("Bearer {session}"))
                .header("x-exportdocmanager-desktop-token", secret)
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(denied.status(), StatusCode::FORBIDDEN);
    for authenticated in [false, true] {
        let mut request = Request::builder()
            .uri("/api/invoices")
            .header(header::HOST, authority)
            .header(header::ORIGIN, origin)
            .header("x-exportdocmanager-desktop-token", secret);
        if authenticated {
            request = request.header(header::AUTHORIZATION, format!("Bearer {session}"));
        }
        let response = app
            .clone()
            .oneshot(request.body(Body::empty()).unwrap())
            .await
            .unwrap();
        assert_eq!(
            response.status(),
            if authenticated {
                StatusCode::OK
            } else {
                StatusCode::UNAUTHORIZED
            }
        );
    }
    let local = Request::builder()
        .method("POST")
        .uri("/api/system/shutdown-maintenance")
        .header(header::HOST, authority)
        .body(Body::empty())
        .unwrap();
    assert_eq!(
        app.oneshot(local).await.unwrap().status(),
        StatusCode::FORBIDDEN
    );
}

#[test]
fn desktop_host_binds_loopback_and_stops_the_listener() {
    let fixture = Fixture::new();
    let host = DesktopHost::start(fixture.service.clone(), false).unwrap();
    let address: std::net::SocketAddr = host
        .api_base_url
        .strip_prefix("http://")
        .unwrap()
        .parse()
        .unwrap();
    assert!(address.ip().is_loopback());
    assert_ne!(address.port(), 0);
    assert!(host.access_token.len() >= 64);
    assert!(std::net::TcpStream::connect_timeout(&address, Duration::from_secs(2)).is_ok());
    host.stop().unwrap();
    assert!(std::net::TcpStream::connect_timeout(&address, Duration::from_secs(1)).is_err());
}
