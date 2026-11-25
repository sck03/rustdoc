use axum::{
    body::{Body, to_bytes},
    http::{Request, StatusCode},
};
use export_doc_engine::{
    engine::NativeService,
    generated_api::*,
    paths::{RuntimePaths, nonce},
};
use serde_json::{Value, json};
use tower::ServiceExt;

#[tokio::test]
async fn uploaded_preview_enforces_auth_size_and_reports_missing_worker() {
    let root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/http-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(RuntimePaths::server(&root, &root.join("Data")).unwrap()).unwrap();
    let app = export_doc_server::router(service.clone(), None);
    let send = |token: &str, body: Vec<u8>| {
        Request::builder()
            .method("POST")
            .uri(PREVIEW_UPLOADED_OCR_IMAGE.path)
            .header("authorization", format!("Bearer {token}"))
            .header("content-type", "application/octet-stream")
            .body(Body::from(body))
            .unwrap()
    };
    let login: Value = serde_json::from_slice(
        &service
            .dispatch(
                LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    let token = login["accessToken"].as_str().unwrap();
    for (identity, bytes, expected) in [
        ("invalid", b"image".to_vec(), StatusCode::UNAUTHORIZED),
        (
            token,
            vec![0; 25 * 1024 * 1024 + 1],
            StatusCode::PAYLOAD_TOO_LARGE,
        ),
        (token, b"image".to_vec(), StatusCode::SERVICE_UNAVAILABLE),
    ] {
        let response = app.clone().oneshot(send(identity, bytes)).await.unwrap();
        assert_eq!(response.status(), expected);
        let body = to_bytes(response.into_body(), 65536).await.unwrap();
        assert!(!body.is_empty());
    }
    service.close().unwrap();
    drop((app, service));
    std::fs::remove_dir_all(root).unwrap();
}
