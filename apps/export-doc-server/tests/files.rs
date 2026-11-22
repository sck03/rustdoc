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
use hyper::body::Body as HttpBody;
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
    // The generated client posts a JSON body; query parameters must not replace it.
    for (body, authenticated, expected) in [
        (
            json!({"reportType":"ExportDocument","templatePath":"builtin:Export/invoice_template.dtpl"}),
            true,
            StatusCode::OK,
        ),
        (
            json!({"reportType":"ExportDocument"}),
            true,
            StatusCode::BAD_REQUEST,
        ),
        (
            json!({"reportType":"ExportDocument","templatePath":17}),
            true,
            StatusCode::BAD_REQUEST,
        ),
        (
            json!({"reportType":"ExportDocument","templatePath":"builtin:Export/invoice_template.dtpl"}),
            false,
            StatusCode::UNAUTHORIZED,
        ),
    ] {
        let mut request = Request::builder()
            .method("POST")
            .uri("/api/reports/templates/file/download?templatePath=ignored.dtpl")
            .header("content-type", "application/json");
        if authenticated {
            request = request.header("authorization", format!("Bearer {token}"));
        }
        let response = app
            .clone()
            .oneshot(request.body(Body::from(body.to_string())).unwrap())
            .await
            .unwrap();
        assert_eq!(response.status(), expected);
        if expected == StatusCode::OK {
            assert_eq!(
                response.headers()["content-type"],
                "application/vnd.exportdoc.dtpl"
            );
            let encoded = response.headers()["content-disposition"]
                .to_str()
                .unwrap()
                .split("filename*=UTF-8''")
                .nth(1)
                .unwrap();
            let (filename, _) = url::form_urlencoded::parse(encoded.as_bytes())
                .next()
                .unwrap();
            assert_eq!(filename, "invoice_template.dtpl");
            let bytes = to_bytes(response.into_body(), 16 * 1024 * 1024)
                .await
                .unwrap();
            assert_eq!(
                bytes.as_ref(),
                std::fs::read(workspace.join("Templates/Export/invoice_template.dtpl")).unwrap()
            );
        }
    }
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

#[tokio::test]
async fn attachment_tickets_stream_ranges_and_recheck_deleted_sessions() {
    use export_doc_engine::{generated_api::*, invoice::InvoiceDraft};
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
    let (_, _, admin) = json_response(
        &app,
        "POST",
        "/api/auth/login",
        "",
        json!({"username":"admin","password":""}),
    )
    .await;
    let admin_token = admin["accessToken"].as_str().unwrap();
    let (_, _, invoice) = json_response(
        &app,
        "POST",
        "/api/invoices",
        admin_token,
        json!(
            InvoiceDraft::demo("2026-11-22", "TICKET-FILE")
                .build()
                .unwrap()
        ),
    )
    .await;
    let (_, _, category) = json_response(
        &app,
        "POST",
        "/api/business-attachment-categories",
        admin_token,
        json!({"companyScope":"DEFAULT","name":"下载回归"}),
    )
    .await;
    let content: Vec<u8> = (0..2 * 1024 * 1024 + 17)
        .map(|index| b' ' + (index % 95) as u8)
        .collect();
    let attachment = service
        .upload(
            UPLOAD_BUSINESS_ATTACHMENT,
            &[("invoiceId", invoice["id"].to_string())],
            json!({"title":"分块下载","categoryId":category["id"],"uploadKey":nonce().unwrap()}),
            "original.txt",
            &content,
            admin_token,
        )
        .unwrap();
    let issue_path = CREATE_BUSINESS_ATTACHMENT_DOWNLOAD_TICKET
        .path
        .replace("{id}", &attachment["id"].to_string())
        .replace("{revision}", "1");
    assert_eq!(
        json_response(&app, "POST", &issue_path, "", json!({}))
            .await
            .0,
        StatusCode::UNAUTHORIZED
    );
    let account_body = json!({"username":"file-reader","fullName":"原件读取验证","role":"User","companyScope":"DEFAULT",
        "departmentId":"GENERAL","isActive":true,"resetPassword":"File-Reader-2026","permissionGrants":[
        {"resourceKey":"document.invoices","action":"view","dataScope":"all"}]});
    let (status, _, account) =
        json_response(&app, "POST", "/api/users", admin_token, account_body).await;
    assert_eq!(status, StatusCode::OK, "{account}");
    let (_, _, login) = json_response(
        &app,
        "POST",
        "/api/auth/login",
        "",
        json!({"username":"file-reader","password":"File-Reader-2026"}),
    )
    .await;
    let token = login["accessToken"].as_str().unwrap();
    let (status, headers, ticket) =
        json_response(&app, "POST", &issue_path, token, json!({})).await;
    assert_eq!(status, StatusCode::OK, "{ticket}");
    let cookie = headers["set-cookie"]
        .to_str()
        .unwrap()
        .split(';')
        .next()
        .unwrap();
    let url = ticket["downloadUrl"].as_str().unwrap();
    for binding in [
        "",
        "ExportDocManager.DownloadSession=0000000000000000000000000000000000000000000000000000000000000000",
    ] {
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(url)
                    .header("cookie", binding)
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::NOT_FOUND);
    }
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
        response.headers()["content-length"],
        content.len().to_string()
    );
    let mut body = response.into_body();
    let mut offset = 0;
    let mut frames = 0;
    while let Some(frame) =
        std::future::poll_fn(|context| std::pin::Pin::new(&mut body).poll_frame(context)).await
    {
        if let Ok(bytes) = frame.unwrap().into_data() {
            assert!(
                bytes.len() <= 64 * 1024,
                "file delivery must remain chunked"
            );
            assert_eq!(bytes.as_ref(), &content[offset..offset + bytes.len()]);
            offset += bytes.len();
            frames += 1;
        }
    }
    assert_eq!(offset, content.len());
    assert!(frames > 1);
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri(url)
                .header("cookie", cookie)
                .header("range", "bytes=1000-1999")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::PARTIAL_CONTENT);
    assert_eq!(
        to_bytes(response.into_body(), 2000).await.unwrap().as_ref(),
        &content[1000..2000]
    );
    let wrong_route = DOWNLOAD_JOB_RESULT_WITH_TICKET
        .path
        .replace("{token}", ticket["token"].as_str().unwrap());
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri(wrong_route)
                .header("cookie", cookie)
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
    let delete = format!(
        "/api/users/{}?expectedVersion={}",
        account["user"]["id"], account["user"]["versionNumber"]
    );
    assert_eq!(
        json_response(&app, "DELETE", &delete, admin_token, json!({}))
            .await
            .0,
        StatusCode::OK
    );
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
    assert_eq!(
        response.status(),
        StatusCode::UNAUTHORIZED,
        "a valid cookie cannot revive a deleted account"
    );
    assert_eq!(
        json_response(&app, "POST", "/api/auth/logout", token, json!({}))
            .await
            .0,
        StatusCode::UNAUTHORIZED
    );
    drop(body);
    drop(app);
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
