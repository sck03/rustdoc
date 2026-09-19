use axum::{
    body::{Body, to_bytes},
    http::{Request, StatusCode},
};
use export_doc_engine::{
    engine::NativeService,
    generated_api::{EXPORT_CRM_CUSTOMERS, IMPORT_CRM_CUSTOMERS, PREVIEW_CRM_CUSTOMER_IMPORT},
    paths::{RuntimePaths, nonce},
};
use export_doc_server::router;
use serde_json::{Value, json};
use std::{
    path::PathBuf,
    time::{Duration, Instant},
};
use tower::ServiceExt;

async fn json_response(
    app: &axum::Router,
    method: &str,
    path: &str,
    token: &str,
    body: Value,
) -> (StatusCode, axum::http::HeaderMap, Value) {
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method(method)
                .uri(path)
                .header("authorization", format!("Bearer {token}"))
                .header("content-type", "application/json")
                .body(Body::from(body.to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let headers = response.headers().clone();
    let value = serde_json::from_slice(&to_bytes(response.into_body(), 1024 * 1024).await.unwrap())
        .unwrap();
    (status, headers, value)
}

#[tokio::test]
async fn directory_upload_preview_commit_and_xlsx_download_use_the_existing_browser_contract() {
    let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let root = workspace
        .join(".codex-runtime/http-file-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(RuntimePaths::server(&workspace, &root.join("Data")).unwrap()).unwrap();
    let app = router(service.clone(), None);
    let (_, _, login) = json_response(
        &app,
        "POST",
        "/api/auth/login",
        "",
        json!({"username":"admin","password":""}),
    )
    .await;
    let token = login["accessToken"].as_str().unwrap();
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri(format!(
                    "{}?fileName=customers.csv",
                    PREVIEW_CRM_CUSTOMER_IMPORT.path
                ))
                .header("authorization", format!("Bearer {token}"))
                .header("content-type", "application/octet-stream")
                .body(Body::from(
                    "客户名称,联系人,邮箱\nHTTP客户,张经理,zhang@example.test",
                ))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    let preview: Value =
        serde_json::from_slice(&to_bytes(response.into_body(), 1024 * 1024).await.unwrap())
            .unwrap();
    assert_eq!(preview["validRows"], 1);
    let (status, _, result) = json_response(
        &app,
        "POST",
        IMPORT_CRM_CUSTOMERS.path,
        token,
        json!({"previewId":preview["previewId"]}),
    )
    .await;
    assert_eq!(status, StatusCode::OK);
    assert_eq!(result["createdContacts"], 1);
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri(EXPORT_CRM_CUSTOMERS.path)
                .header("authorization", format!("Bearer {token}"))
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    assert_eq!(
        response.headers()["content-type"],
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    );
    assert!(
        response.headers()["content-disposition"]
            .to_str()
            .unwrap()
            .contains("filename*=")
    );
    let workbook = to_bytes(response.into_body(), 32 * 1024 * 1024)
        .await
        .unwrap();
    assert!(workbook.starts_with(b"PK"));
    drop(app);
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn browser_downloads_require_bound_tickets_and_path_operations_stay_local() {
    let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let root = workspace
        .join(".codex-runtime/http-file-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(RuntimePaths::server(&workspace, &root.join("Data")).unwrap()).unwrap();
    let app = router(service.clone(), None);
    let (status, _, login) = json_response(
        &app,
        "POST",
        "/api/auth/login",
        "",
        json!({"username":"admin","password":""}),
    )
    .await;
    assert_eq!(status, StatusCode::OK);
    let token = login["accessToken"].as_str().unwrap();
    let (status, _, job) = json_response(
        &app,
        "POST",
        "/api/tools/excel/template/download",
        token,
        json!({}),
    )
    .await;
    assert_eq!(status, StatusCode::ACCEPTED);
    let id = job["jobId"].as_str().unwrap();
    let deadline = Instant::now() + Duration::from_secs(10);
    loop {
        let (_, _, job) =
            json_response(&app, "GET", &format!("/api/jobs/{id}"), token, Value::Null).await;
        if job["status"] == "Succeeded" {
            break;
        }
        assert_ne!(job["status"], "Failed", "{job}");
        assert!(Instant::now() < deadline, "{job}");
        tokio::time::sleep(Duration::from_millis(20)).await;
    }
    let (status, headers, ticket) = json_response(
        &app,
        "POST",
        &format!("/api/jobs/{id}/download-ticket"),
        token,
        json!({}),
    )
    .await;
    assert_eq!(status, StatusCode::OK);
    let cookie = headers["set-cookie"]
        .to_str()
        .unwrap()
        .split(';')
        .next()
        .unwrap();
    let url = ticket["downloadUrl"].as_str().unwrap();
    let response = app
        .clone()
        .oneshot(Request::builder().uri(url).body(Body::empty()).unwrap())
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri(url)
                .header("cookie", cookie)
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    assert_eq!(
        response.headers()["content-type"],
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    );
    assert!(
        response.headers()["content-disposition"]
            .to_str()
            .unwrap()
            .contains("filename*=UTF-8''")
    );
    let bytes = to_bytes(response.into_body(), 25 * 1024 * 1024)
        .await
        .unwrap();
    assert!(bytes.starts_with(b"PK"));
    // Upload is a raw octet stream in the original generated React contract.
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/api/tools/excel/import-preview-upload?fileName=blank.xlsx")
                .header("authorization", format!("Bearer {token}"))
                .header("content-type", "application/octet-stream")
                .body(Body::from(bytes))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    let preview: Value =
        serde_json::from_slice(&to_bytes(response.into_body(), 1024 * 1024).await.unwrap())
            .unwrap();
    export_doc_contracts::validation::response("PreviewUploadedExcelImport", &preview).unwrap();
    let selected = root.join("not-created.xlsx");
    let (status, _, _) = json_response(
        &app,
        "POST",
        "/api/tools/excel/template/save-to-path",
        token,
        json!({"destinationPath":selected}),
    )
    .await;
    assert_eq!(status, StatusCode::FORBIDDEN);
    assert!(!selected.exists());
    let (status, _, _) = json_response(&app, "POST", "/api/auth/logout", token, json!({})).await;
    assert_eq!(status, StatusCode::OK);
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri(url)
                .header("cookie", cookie)
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
    drop(app);
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
