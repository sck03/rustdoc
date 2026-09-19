pub mod configuration;
mod downloads;
mod request;
mod response;

use axum::{
    Router,
    extract::{DefaultBodyLimit, Request, State},
    routing::{MethodFilter, get, on},
};
use export_doc_contracts::generated_api::*;
use export_doc_engine::engine::NativeService;
use std::{path::Path, sync::Arc};
use tokio::sync::Semaphore;
use tower_http::services::{ServeDir, ServeFile};

#[derive(Clone)]
pub struct ServerState {
    pub service: Arc<NativeService>,
    requests: Arc<Semaphore>,
    bulk_uploads: Arc<Semaphore>,
    tickets: Arc<downloads::Tickets>,
}

pub fn router(service: Arc<NativeService>, web_root: Option<&Path>) -> Router {
    let state = ServerState {
        service,
        requests: Arc::new(Semaphore::new(16)),
        bulk_uploads: Arc::new(Semaphore::new(2)),
        tickets: Arc::default(),
    };
    let mut router = Router::new().route(
        "/openapi/v1.json",
        get(|| async {
            (
                [
                    ("content-type", "application/json; charset=utf-8"),
                    ("cache-control", "no-store"),
                ],
                include_str!("../../../crates/export-doc-contracts/src/openapi.json"),
            )
        }),
    );
    for operation in ALL_OPERATIONS {
        let operation = *operation;
        let method = match operation.method {
            "GET" => MethodFilter::GET,
            "POST" => MethodFilter::POST,
            "PUT" => MethodFilter::PUT,
            "PATCH" => MethodFilter::PATCH,
            "DELETE" => MethodFilter::DELETE,
            _ => panic!("unsupported generated method"),
        };
        router = router.route(
            operation.path,
            on(
                method,
                move |State(state): State<ServerState>, request: Request| {
                    request::handle(state, operation, request)
                },
            )
            .layer(DefaultBodyLimit::max(
                if operation == UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB {
                    129 * 1024 * 1024
                } else if [
                    IMPORT_HS_CODE_KNOWLEDGE,
                    UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
                    UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
                ]
                .contains(&operation)
                {
                    101 * 1024 * 1024
                } else {
                    33 * 1024 * 1024
                },
            )),
        );
    }
    if let Some(root) = web_root {
        router = router.fallback_service(
            ServeDir::new(root).not_found_service(ServeFile::new(root.join("index.html"))),
        );
    }
    router.with_state(state)
}
