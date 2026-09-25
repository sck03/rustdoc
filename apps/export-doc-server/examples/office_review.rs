//! Loopback-only fixture for real React/HTTP office workflow verification.
use export_doc_engine::{engine::NativeService, generated_api::*, paths::RuntimePaths};
use serde_json::{Value, json};
use std::path::PathBuf;

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let data = std::env::args()
        .nth(1)
        .ok_or("provide isolated review data directory")?;
    let service = NativeService::open(RuntimePaths::server(&root, &PathBuf::from(data))?)?;
    let login: Value = serde_json::from_slice(&service.dispatch(
        LOGIN,
        &[],
        &[],
        Some(json!({"username":"admin","password":""})),
        "",
    )?)?;
    let token = login["accessToken"].as_str().ok_or("no token")?;
    let users: Value =
        serde_json::from_slice(&service.dispatch(LIST_USERS, &[], &[], None, token)?)?;
    if !users.to_string().contains("oa-review") {
        let body = export_doc_engine::contracts::overlay(
            export_doc_engine::contracts::object(CREATE_USER_ACCOUNT.id, true),
            &json!({"username":"oa-review","fullName":"验收管理员","role":"Admin","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Review-2026-Test"}),
        );
        service.dispatch(CREATE_USER_ACCOUNT, &[], &[], Some(body), token)?;
    }
    let people: Value =
        serde_json::from_slice(&service.dispatch(LIST_PERSONNEL, &[], &[], None, token)?)?;
    if people["totalCount"] == 0 {
        let body = export_doc_engine::contracts::overlay(
            export_doc_engine::contracts::object(CREATE_PERSONNEL.id, true),
            &json!({"requestKey":"review-employee","employeeNumber":"OA-001","departmentId":"GENERAL","jobTitle":"业务专员","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":"李明"}}),
        );
        service.dispatch(CREATE_PERSONNEL, &[], &[], Some(body), token)?;
    }
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await?;
    println!(
        "{}",
        json!({"url":format!("http://{}",listener.local_addr()?)})
    );
    axum::serve(
        listener,
        export_doc_server::router(
            service.clone(),
            Some(&root.join("apps/export-doc-web/dist")),
        ),
    )
    .with_graceful_shutdown(async {
        let _ = tokio::signal::ctrl_c().await;
    })
    .await?;
    service.close()?;
    Ok(())
}
