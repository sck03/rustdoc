use super::*;
use crate::{ServerState, admission::Admission, shutdown::Shutdown, telemetry::Telemetry};
use export_doc_engine::{
    engine::NativeService,
    paths::{RuntimePaths, nonce},
};
use std::{sync::Arc, time::Duration};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::TcpStream,
    sync::Semaphore,
};

async fn headers(socket: &mut TcpStream) -> String {
    tokio::time::timeout(Duration::from_secs(10), async {
        let mut bytes = vec![];
        while !bytes.ends_with(b"\r\n\r\n") {
            bytes.push(socket.read_u8().await.unwrap());
            assert!(bytes.len() < 8192);
        }
        String::from_utf8(bytes).unwrap()
    })
    .await
    .unwrap()
}

async fn check_command_admission(state: &ServerState) {
    use axum::{Router, http::Request, routing::post};
    use export_doc_engine::generated_api::{
        CREATE_CRM_CUSTOMER, CREATE_MEETING_ROOM, DELETE_MEETING_ROOM, LOGIN, QUERY_CRM_CUSTOMERS,
    };
    use tower::ServiceExt;
    let token: serde_json::Value = serde_json::from_slice(
        &state
            .service
            .dispatch(
                LOGIN,
                &[],
                &[],
                Some(serde_json::json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    let token = token["accessToken"].as_str().unwrap();
    let count = || {
        let result = state
            .service
            .dispatch(
                QUERY_CRM_CUSTOMERS,
                &[],
                &[("keyword", "budget-test".into())],
                None,
                token,
            )
            .unwrap();
        serde_json::from_slice::<serde_json::Value>(&result).unwrap()["totalCount"]
            .as_u64()
            .unwrap()
    };
    let command_state = state.clone();
    let app = Router::new().route(
        CREATE_CRM_CUSTOMER.path,
        post(move |request| {
            crate::request::handle(command_state.clone(), CREATE_CRM_CUSTOMER, request)
        }),
    );
    let request = || {
        Request::post(CREATE_CRM_CUSTOMER.path)
            .header("content-type", "application/json")
            .header("authorization", format!("Bearer {token}"))
            .body(Body::from(
                serde_json::json!({"id":0,"name":"budget-test"}).to_string(),
            ))
            .unwrap()
    };
    let busy = state
        .response_memory
        .reserve_working(128 * 1024 * 1024)
        .unwrap();
    assert_eq!(app.clone().oneshot(request()).await.unwrap().status(), 429);
    assert_eq!(count(), 0);
    drop(busy);
    let response = app.oneshot(request()).await.unwrap();
    let status = response.status();
    let bytes = axum::body::to_bytes(response.into_body(), 1024 * 1024)
        .await
        .unwrap();
    assert_eq!(status, 201, "{}", String::from_utf8_lossy(&bytes));
    assert_eq!(count(), 1);
    let room: serde_json::Value = serde_json::from_slice(&state.service.dispatch(CREATE_MEETING_ROOM, &[], &[],
        Some(serde_json::json!({"name":"empty-response-budget","capacity":4,"maximumBookingHours":8,"advanceBookingDays":90,"isActive":true})), token).unwrap()).unwrap();
    let delete_state = state.clone();
    let app = Router::new().route(
        DELETE_MEETING_ROOM.path,
        axum::routing::delete(move |request| {
            crate::request::handle(delete_state.clone(), DELETE_MEETING_ROOM, request)
        }),
    );
    let busy = state
        .response_memory
        .reserve_working(128 * 1024 * 1024)
        .unwrap();
    let response = app
        .oneshot(
            Request::delete(
                DELETE_MEETING_ROOM
                    .path
                    .replace("{id}", &room["id"].to_string()),
            )
            .header("content-type", "application/json")
            .header("authorization", format!("Bearer {token}"))
            .body(Body::from(
                serde_json::json!({"expectedVersion":room["versionNumber"]}).to_string(),
            ))
            .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), 204);
    assert!(
        axum::body::to_bytes(response.into_body(), 1024)
            .await
            .unwrap()
            .is_empty()
    );
    assert_eq!(
        state.response_memory.snapshot()["reservedBytes"],
        128 * 1024 * 1024
    );
    drop(busy);
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn slow_json_sockets_are_bounded_and_disconnect_releases_the_budget() {
    let workspace = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .canonicalize()
        .unwrap();
    let root = workspace
        .join(".codex-runtime/http-json-budget-tests")
        .join(nonce().unwrap());
    let service = NativeService::open(RuntimePaths::server(&workspace, &root).unwrap()).unwrap();
    let state = ServerState {
        telemetry: Arc::new(Telemetry::new(service.paths.log_root.clone())),
        service: service.clone(),
        requests: Arc::new(Admission::new(16, 16, Duration::from_secs(5))),
        bulk_uploads: Arc::new(Semaphore::new(2)),
        downloads: Arc::new(Semaphore::new(8)),
        previews: Arc::new(Semaphore::new(2)),
        processing: Arc::new(Semaphore::new(2)),
        body_resources: Arc::default(),
        response_memory: Arc::default(),
        upload_space: service.paths.disk_space.clone(),
        tickets: Arc::default(),
        desktop_token: None,
        shutdown: Shutdown::default(),
    };
    let large_state = state.clone();
    check_command_admission(&state).await;
    let app = axum::Router::new().route(
        "/large",
        axum::routing::get(move || {
            let state = large_state.clone();
            async move {
                let mut bytes =
                    vec![b'x'; export_doc_contracts::request_limits::MAX_JSON_RESPONSE_BYTES];
                bytes[0] = b'"';
                *bytes.last_mut().unwrap() = b'"';
                success(
                    export_doc_contracts::generated_api::GET_INVOICE,
                    Reply::bytes(bytes),
                    &state,
                    "slow-json",
                    None,
                )
                .await
            }
        }),
    );
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = listener.local_addr().unwrap();
    let shutdown = state.shutdown.clone();
    let server = tokio::spawn(crate::serve(
        listener,
        app,
        shutdown.clone(),
        std::future::pending(),
    ));
    let mut sockets = vec![];
    for expected in [200, 200, 429] {
        let mut socket = TcpStream::connect(address).await.unwrap();
        socket
            .write_all(b"GET /large HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")
            .await
            .unwrap();
        let head = headers(&mut socket).await;
        assert!(head.starts_with(&format!("HTTP/1.1 {expected}")), "{head}");
        sockets.push(socket);
    }
    let reserved = state.response_memory.snapshot();
    assert_eq!(reserved["reservedBytes"], 128 * 1024 * 1024);
    assert_eq!(
        state.downloads.available_permits(),
        8,
        "ordinary JSON cannot exhaust file download slots"
    );
    drop(sockets);
    tokio::time::timeout(Duration::from_secs(5), async {
        while state.response_memory.snapshot()["reservedBytes"] != 0 {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    let normal = success(
        export_doc_contracts::generated_api::GET_INVOICE,
        Reply::bytes(b"{}".to_vec()),
        &state,
        "after-disconnect",
        None,
    )
    .await;
    assert_eq!(normal.status(), 200);
    assert_eq!(
        axum::body::to_bytes(normal.into_body(), 1024)
            .await
            .unwrap()
            .as_ref(),
        b"{}"
    );
    shutdown.stop();
    server.await.unwrap().unwrap();
    eprintln!(
        "HTTP_JSON_BUDGET {{\"accepted\":2,\"rejected\":1,\"retainedBytes\":134217728,\"releasedAfterDisconnect\":true}}"
    );
    drop(state);
    service.close().unwrap();
    drop(service);
    assert!(root.starts_with(workspace.join(".codex-runtime/http-json-budget-tests")));
    std::fs::remove_dir_all(root).unwrap();
}
