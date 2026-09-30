use axum::{
    body::{Body, to_bytes},
    http::{Request, StatusCode},
};
use export_doc_engine::{
    contracts,
    engine::NativeService,
    generated_api::{ApiRuntimeMetricsResponse, CREATE_USER_ACCOUNT},
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
    let (tokens,metrics_request_id)=runtime.block_on(async {
        for (method, path, status) in [
            ("GET", "/api/invoices", StatusCode::UNAUTHORIZED),
            ("GET", "/api/diagnostics/metrics", StatusCode::UNAUTHORIZED),
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
        let login_request_id=response.headers()["x-request-id"].to_str().unwrap().to_owned();
        let body: Value =
            serde_json::from_slice(&to_bytes(response.into_body(), 65536).await.unwrap()).unwrap();
        let token = body["accessToken"].as_str().unwrap();
        let response=app.clone().oneshot(Request::builder().uri("/api/diagnostics/metrics")
            .header("authorization",format!("Bearer {token}")).header("x-request-id","caller-controlled-id").body(Body::empty()).unwrap()).await.unwrap();
        assert_eq!(response.status(),StatusCode::OK);
        let metrics_request_id=response.headers()["x-request-id"].to_str().unwrap().to_owned();
        assert_ne!(metrics_request_id,login_request_id);assert_ne!(metrics_request_id,"caller-controlled-id");
        let metrics:Value=serde_json::from_slice(&to_bytes(response.into_body(),65536).await.unwrap()).unwrap();
        serde_json::from_value::<ApiRuntimeMetricsResponse>(metrics.clone()).unwrap();
        assert_eq!(metrics["storage"]["capacity"],1);
        assert!(metrics["http"]["requests"]["completed"].as_u64().unwrap()>=3);
        assert!(metrics["storage"]["operations"]["completed"].as_u64().unwrap()>0);
        service.dispatch(CREATE_USER_ACCOUNT,&[],&[],Some(contracts::overlay(contracts::object(CREATE_USER_ACCOUNT.id,true),
            &json!({"username":"metrics-staff","fullName":"Metrics staff","role":"OfficeManager","departmentId":"GENERAL","companyScope":"DEFAULT","isActive":true,"resetPassword":"Do-Not-Log-Metrics-2026"}))),token).unwrap();
        let response=app.clone().oneshot(Request::builder().method("POST").uri("/api/auth/login").header("content-type","application/json")
            .body(Body::from(json!({"username":"metrics-staff","password":"Do-Not-Log-Metrics-2026"}).to_string())).unwrap()).await.unwrap();
        let staff:Value=serde_json::from_slice(&to_bytes(response.into_body(),65536).await.unwrap()).unwrap();
        let staff_token=staff["accessToken"].as_str().unwrap();
        let denied=app.clone().oneshot(Request::builder().uri("/api/diagnostics/metrics").header("authorization",format!("Bearer {staff_token}"))
            .body(Body::empty()).unwrap()).await.unwrap();assert_eq!(denied.status(),StatusCode::FORBIDDEN);
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
        (vec![token.to_owned(),staff_token.to_owned()],metrics_request_id)
    });
    drop(app);
    let log = std::fs::read_to_string(root.join("Data/Logs/requests.jsonl")).unwrap();
    assert!(!log.contains("Do-Not-Log-Metrics-2026"));
    for token in tokens {
        assert!(!log.contains(&token));
    }
    assert!(
        log.lines()
            .map(|line| serde_json::from_str::<Value>(line).unwrap())
            .any(|event| event["requestId"] == metrics_request_id
                && event["operation"] == "GetRuntimeMetrics"
                && event["status"] == 200)
    );
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
