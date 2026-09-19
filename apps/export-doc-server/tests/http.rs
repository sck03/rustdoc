use axum::{
    body::{Body, to_bytes},
    http::{Request, StatusCode},
};
use export_doc_engine::{
    engine::NativeService,
    paths::{RuntimePaths, nonce},
};
use export_doc_server::router;
use serde_json::{Value, json};
use std::path::PathBuf;
use tower::ServiceExt;

#[test]
fn http_uses_generated_routes_and_denies_unauthenticated_or_local_operations() {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime")
        .join("http-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(RuntimePaths::server(&root, &root.join("Data")).unwrap()).unwrap();
    let app = router(service.clone(), None);
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .unwrap();
    runtime.block_on(async {
        for (method, path, status) in [
            ("GET", "/api/invoices", StatusCode::UNAUTHORIZED),
            (
                "POST",
                "/api/system/shutdown-maintenance",
                StatusCode::FORBIDDEN,
            ),
            ("GET", "/route-that-does-not-exist", StatusCode::NOT_FOUND),
        ] {
            let response = app
                .clone()
                .oneshot(
                    Request::builder()
                        .method(method)
                        .uri(path)
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), status, "{path}");
        }
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri("/api/auth/login")
                    .header("content-type", "application/json")
                    .body(Body::from(
                        json!({"username":"admin","password":""}).to_string(),
                    ))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(response.headers()["cache-control"], "no-store");
        let body: Value =
            serde_json::from_slice(&to_bytes(response.into_body(), 65536).await.unwrap()).unwrap();
        let token = body["accessToken"].as_str().unwrap();
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri("/api/invoices")
                    .header("authorization", format!("Bearer {token}"))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::OK);
        let body: Value =
            serde_json::from_slice(&to_bytes(response.into_body(), 65536).await.unwrap()).unwrap();
        assert_eq!(body["totalCount"], 0);
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri("/api/auth/login")
                    .body(Body::from("{invalid"))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::BAD_REQUEST);
    });
    drop(app);
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
