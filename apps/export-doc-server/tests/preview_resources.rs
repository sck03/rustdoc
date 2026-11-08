use axum::{
    body::{Body, to_bytes},
    http::{Request, StatusCode},
};
use export_doc_engine::{
    engine::NativeService,
    generated_api::PREVIEW_REPORT_TEMPLATE_CONTENT,
    paths::{RuntimePaths, nonce},
};
use serde_json::{Value, json};
use std::{path::PathBuf, time::Duration};
use tower::ServiceExt;

#[test]
fn slow_preview_readers_are_bounded_and_disconnect_releases_capacity() {
    let repo = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .canonicalize()
        .unwrap();
    let root = repo
        .join(".codex-runtime/http-tests")
        .join(nonce().unwrap());
    let service =
        NativeService::open(RuntimePaths::server(&repo, &root.join("Data")).unwrap()).unwrap();
    let app = export_doc_server::router(service.clone(), None);
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .unwrap();
    runtime.block_on(async {
        let response = app.clone().oneshot(Request::builder().method("POST").uri("/api/auth/login")
            .header("content-type", "application/json").body(Body::from(json!({"username":"admin","password":""}).to_string())).unwrap()).await.unwrap();
        assert_eq!(response.status(), StatusCode::OK);
        let login: Value = serde_json::from_slice(&to_bytes(response.into_body(), 65536).await.unwrap()).unwrap();
        let token = login["accessToken"].as_str().unwrap();
        let template = app.clone().oneshot(Request::builder()
            .uri("/api/reports/templates/content?reportType=ExportDocument&templatePath=Templates%2FExport%2Finvoice_template.dtpl")
            .header("authorization",format!("Bearer {token}")).body(Body::empty()).unwrap()).await.unwrap();
        assert_eq!(template.status(),StatusCode::OK);
        let template: Value = serde_json::from_slice(&to_bytes(template.into_body(),10*1024*1024).await.unwrap()).unwrap();
        let preview = || Request::builder().method("POST").uri(PREVIEW_REPORT_TEMPLATE_CONTENT.path)
            .header("authorization", format!("Bearer {token}")).header("content-type", "application/json")
            .body(Body::from(json!({"reportType":"ExportDocument","content":template["content"],"sampleProfile":"exportLongItems","withSeal":false}).to_string())).unwrap();
        let held = app.clone().oneshot(preview()).await.unwrap();
        if held.status() != StatusCode::OK {
            panic!("preview failed: {:?}", to_bytes(held.into_body(),65536).await.unwrap());
        }
        assert!(held.headers()["content-length"].to_str().unwrap().parse::<usize>().unwrap() > 65536);
        let second = app.clone().oneshot(preview()).await.unwrap();
        assert_eq!(second.status(),StatusCode::OK);
        let rejected = app.clone().oneshot(preview()).await.unwrap();
        assert_eq!(rejected.status(), StatusCode::TOO_MANY_REQUESTS);
        let health = app.clone().oneshot(Request::builder().uri("/api/invoices").header("authorization",format!("Bearer {token}")).body(Body::empty()).unwrap()).await.unwrap();
        assert_eq!(health.status(), StatusCode::OK);
        drop((held, second));
        tokio::time::sleep(Duration::from_millis(50)).await;
        let released = app.clone().oneshot(preview()).await.unwrap();
        assert_eq!(released.status(), StatusCode::OK);
        let body: Value = serde_json::from_slice(&to_bytes(released.into_body(), 64*1024*1024).await.unwrap()).unwrap();
        assert!(body["html"].as_str().unwrap().contains("<svg"));
    });
    service.close().unwrap();
    drop((app, service));
    std::fs::remove_dir_all(root).unwrap();
}
