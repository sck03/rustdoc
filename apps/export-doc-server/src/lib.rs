mod admission;
#[cfg(feature = "postgres")]
pub mod configuration;
pub mod desktop;
mod downloads;
mod request;
mod response;
mod telemetry;
const MAX_REQUESTS: usize = 16;
const MAX_QUEUED_REQUESTS: usize = 16;
const MAX_BULK_UPLOADS: usize = 2;

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
    telemetry: Arc<telemetry::Telemetry>,
    pub service: Arc<NativeService>,
    requests: Arc<admission::Admission>,
    bulk_uploads: Arc<Semaphore>,
    tickets: Arc<downloads::Tickets>,
    desktop_token: Option<Arc<str>>,
}

pub fn router(service: Arc<NativeService>, web_root: Option<&Path>) -> Router {
    compose_router(service, web_root, None)
}

fn compose_router(
    service: Arc<NativeService>,
    web_root: Option<&Path>,
    desktop_token: Option<Arc<str>>,
) -> Router {
    let telemetry = Arc::new(telemetry::Telemetry::new(service.paths.log_root.clone()));
    let state = ServerState {
        telemetry,
        service,
        requests: Arc::new(admission::Admission::new(
            MAX_REQUESTS,
            MAX_QUEUED_REQUESTS,
            std::time::Duration::from_secs(5),
        )),
        bulk_uploads: Arc::new(Semaphore::new(MAX_BULK_UPLOADS)),
        tickets: Arc::default(),
        desktop_token,
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
            .layer(DefaultBodyLimit::max(request::body::envelope_limit(
                operation,
            ))),
        );
    }
    if let Some(root) = web_root {
        router = router.fallback_service(
            ServeDir::new(root).not_found_service(ServeFile::new(root.join("index.html"))),
        );
    }
    router.with_state(state)
}
