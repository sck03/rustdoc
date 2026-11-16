use super::*;
use crate::{admission::Admission, shutdown::Shutdown, telemetry::Telemetry};
use export_doc_engine::{
    engine::NativeService,
    paths::{RuntimePaths, nonce},
};
use std::{future::Future, task::Poll};
use tokio::sync::Semaphore;
use tower::ServiceExt;

#[test]
fn canceled_upload_authentication_retains_admission_until_blocking_work_exits() {
    let workspace = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let root = workspace
        .join(".codex-runtime/http-auth-lifecycle-tests")
        .join(nonce().unwrap());
    let service = NativeService::open(RuntimePaths::server(&workspace, &root).unwrap()).unwrap();
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .max_blocking_threads(1)
        .enable_all()
        .build()
        .unwrap();
    runtime.block_on(async {
        for operation in [PREVIEW_CRM_CUSTOMER_IMPORT, STAGE_SERVER_MIGRATION_RESTORE] {
            let state = ServerState {
                telemetry: Arc::new(Telemetry::new(service.paths.log_root.clone())),
                service: service.clone(),
                requests: Arc::new(Admission::new(1, 0, Duration::from_secs(1))),
                bulk_uploads: Arc::new(Semaphore::new(2)),
                downloads: Arc::new(Semaphore::new(8)),
                processing: Arc::new(Semaphore::new(2)),
                body_resources: Arc::default(), preview_memory: Arc::default(),
                upload_space: service.paths.disk_space.clone(), tickets: Arc::default(),
                desktop_token: None, shutdown: Shutdown::default(),
            };
            let gate = state.requests.clone();
            let (release, blocked) = std::sync::mpsc::channel();
            let (started, ready) = tokio::sync::oneshot::channel();
            let blocker = tokio::task::spawn_blocking(move || {
                started.send(()).unwrap();
                blocked.recv_timeout(Duration::from_secs(10)).unwrap();
            });
            ready.await.unwrap();
            let app = axum::Router::new().route(operation.path, axum::routing::post(move |request| handle(state, operation, request)));
            let request = Request::builder().method("POST").uri(operation.path)
                .header("authorization", "Bearer invalid-session")
                .header("content-type", "application/octet-stream")
                .body(Body::empty()).unwrap();
            let mut response = Box::pin(app.oneshot(request));
            // Poll through the real router until it queues authentication on the occupied pool.
            std::future::poll_fn(|cx| {
                assert!(response.as_mut().poll(cx).is_pending());
                Poll::Ready(())
            }).await;
            assert_eq!(gate.available(), 0);
            drop(response);
            assert_eq!(gate.available(), 0, "{operation:?}: a disconnected request cannot release queued authentication capacity");
            release.send(()).unwrap(); blocker.await.unwrap();
            tokio::time::timeout(Duration::from_secs(5), async {
                while gate.available() == 0 { tokio::task::yield_now().await; }
            }).await.unwrap();
        }
    });
    drop(runtime);
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
