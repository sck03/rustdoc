//! Private loopback transport for the Tauri host. Business operations remain in
//! NativeService and share the exact HTTP adapter used by the team edition.
use axum::{
    Router,
    extract::Request,
    http::{HeaderValue, header},
    middleware::{self, Next},
    response::Response,
};
use export_doc_engine::{engine::NativeService, paths::nonce};
use std::{
    net::TcpListener,
    sync::{Arc, mpsc},
    thread,
    time::Duration,
};
use tower_http::cors::{AllowHeaders, AllowMethods, CorsLayer};

pub struct DesktopHost {
    pub api_base_url: String,
    pub access_token: String,
    shutdown: Option<tokio::sync::oneshot::Sender<()>>,
    finished: mpsc::Receiver<Result<(), String>>,
    worker: Option<thread::JoinHandle<()>>,
}

impl DesktopHost {
    pub fn start(service: Arc<NativeService>, development: bool) -> Result<Self, String> {
        if service.provider().map_err(|e| e.to_string())? != "SQLite" {
            return Err("桌面宿主只允许 SQLite；团队连接由独立 Rust 服务管理。".into());
        }
        let listener =
            TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, 0)).map_err(|e| e.to_string())?;
        listener.set_nonblocking(true).map_err(|e| e.to_string())?;
        let authority = listener
            .local_addr()
            .map_err(|e| e.to_string())?
            .to_string();
        let access_token = format!("{}{}", nonce()?, nonce()?);
        let routes = desktop_router(service.clone(), &access_token, &authority, development);
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .max_blocking_threads(20)
            .enable_all()
            .build()
            .map_err(|e| e.to_string())?;
        let (shutdown, stopping) = tokio::sync::oneshot::channel();
        let (completed, finished) = mpsc::sync_channel(1);
        let worker = thread::Builder::new().name("desktop-rust-api".into()).spawn(move || {
            let result = runtime.block_on(async {
                let listener = tokio::net::TcpListener::from_std(listener)
                    .map_err(|e| e.to_string())?;
                let health = service.clone();
                axum::serve(listener, routes).with_graceful_shutdown(async move {
                    tokio::pin!(stopping);
                    let mut interval = tokio::time::interval(Duration::from_secs(2));
                    loop {
                        tokio::select! {
                            _ = &mut stopping => break,
                            _ = interval.tick() => {
                                let health = health.clone();
                                if !matches!(tokio::task::spawn_blocking(move || health.health()).await, Ok(Ok(()))) {
                                    eprintln!("桌面数据库不可用，Rust API 已停止接收请求。");
                                    break;
                                }
                            }
                        }
                    }
                }).await.map_err(|e| e.to_string())
            });
            let closed = service.close().map_err(|e| e.to_string());
            runtime.shutdown_timeout(Duration::from_secs(5));
            let _ = completed.send(result.and(closed));
        }).map_err(|e| e.to_string())?;
        Ok(Self {
            api_base_url: format!("http://{authority}"),
            access_token,
            shutdown: Some(shutdown),
            finished,
            worker: Some(worker),
        })
    }

    /// Call from a worker, never from the UI event loop. In-flight requests have
    /// their own cancellation/deadlines; failed shutdown is reported to the host.
    pub fn stop(mut self) -> Result<(), String> {
        self.signal_stop();
        let result = self
            .finished
            .recv_timeout(Duration::from_secs(45))
            .map_err(|_| "Rust 后端关闭超过时限。".to_owned())?;
        if let Some(worker) = self.worker.take() {
            worker
                .join()
                .map_err(|_| "Rust 后端线程异常退出。".to_owned())?;
        }
        result
    }

    fn signal_stop(&mut self) {
        if let Some(shutdown) = self.shutdown.take() {
            let _ = shutdown.send(());
        }
    }
}

impl Drop for DesktopHost {
    fn drop(&mut self) {
        self.signal_stop();
    }
}

fn desktop_router(
    service: Arc<NativeService>,
    token: &str,
    authority: &str,
    development: bool,
) -> Router {
    let mut origins = vec![
        "tauri://localhost",
        "http://tauri.localhost",
        "https://tauri.localhost",
    ];
    if development {
        origins.push("http://127.0.0.1:5173");
    }
    let allowed: Vec<HeaderValue> = origins
        .iter()
        .map(|v| HeaderValue::from_static(v))
        .collect();
    let cors = CorsLayer::new()
        .allow_origin(allowed.clone())
        .allow_methods(AllowMethods::mirror_request())
        .allow_headers(AllowHeaders::mirror_request())
        .allow_credentials(true)
        .expose_headers([header::CONTENT_DISPOSITION]);
    let authority = authority.to_owned();
    crate::compose_router(service, None, Some(Arc::from(token)))
        .layer(cors)
        .layer(middleware::from_fn(move |request: Request, next: Next| {
            let authority = authority.clone();
            let allowed = allowed.clone();
            async move {
                let valid_host = request
                    .headers()
                    .get(header::HOST)
                    .and_then(|v| v.to_str().ok())
                    == Some(authority.as_str());
                let valid_origin = request
                    .headers()
                    .get(header::ORIGIN)
                    .is_none_or(|origin| allowed.contains(origin));
                if !valid_host || !valid_origin {
                    return denied();
                }
                next.run(request).await
            }
        }))
}

fn denied() -> Response {
    crate::response::error(export_doc_engine::api::ApiError {
        status: Some(403),
        message: "不允许的桌面请求来源。".into(),
    })
}

#[cfg(test)]
mod tests;
