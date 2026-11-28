//! Small independent HTTP composition root. Endpoint authentication is projected
//! from native metadata; no business service or database session is constructed.
use crate::{configuration::Configuration, request::error, response};
use axum::{
    Router,
    body::{Body, to_bytes},
    extract::Request,
    http::{Response, StatusCode},
    routing::MethodFilter,
};
use export_doc_contracts::{
    contracts,
    generated_api::{ALL_OPERATIONS, Operation},
};
use export_doc_engine::{engine::RecoveryMaintenance, paths::nonce};
use serde_json::{Value, json};
use std::{
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};
use subtle::ConstantTimeEq;
const COOKIE: &str = "ExportDocManager.Maintenance";
mod execution;
struct Session {
    token: String,
    csrf: String,
    expires: Instant,
    deadline: Instant,
    secure: bool,
}
struct Credentials {
    bootstrap: Option<String>,
    session: Option<Session>,
    failures: u32,
    next: Instant,
}
struct State {
    execution: execution::Executor,
    engine: Arc<RecoveryMaintenance>,
    credentials: Mutex<Credentials>,
}
pub fn run(configuration: Configuration) -> Result<(), Box<dyn std::error::Error>> {
    if configuration.initialize_schema
        || configuration.initialize_only
        || configuration.restore_pending
        || configuration.migration_package.is_some()
    {
        return Err("网页维护不能与建表或自动恢复参数同时使用。".into());
    }
    let engine = RecoveryMaintenance::open(
        configuration.paths,
        configuration.maintenance_connection,
        configuration.connection,
        configuration.owner,
    )?;
    let state = Arc::new(State {
        execution: execution::Executor::new(),
        engine: engine.clone(),
        credentials: Mutex::new(Credentials {
            bootstrap: Some(configuration.maintenance_token),
            session: None,
            failures: 0,
            next: Instant::now(),
        }),
    });
    let mut router = Router::new();
    for operation in ALL_OPERATIONS.iter().copied().filter(|operation| {
        contracts::contract()["operations"][operation.id]["policy"]["authenticationContext"]
            == "Maintenance"
    }) {
        let state = state.clone();
        let filter = if operation.method == "GET" {
            MethodFilter::GET
        } else {
            MethodFilter::POST
        };
        router = router.route(
            operation.path,
            axum::routing::on(filter, move |request: Request| {
                handle(state.clone(), operation, request)
            }),
        );
    }
    if let Some(root) = configuration.web_root {
        router = router.fallback_service(
            tower_http::services::ServeDir::new(&root).not_found_service(
                tower_http::services::ServeFile::new(root.join("index.html")),
            ),
        );
    }
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .enable_all()
        .build()?;
    runtime.block_on(async move {
        let listener = tokio::net::TcpListener::bind(configuration.bind).await?;
        eprintln!(
            "独立维护服务已启动：http://{}/#/maintenance（凭证从部署端读取，不写入日志）",
            listener.local_addr()?
        );
        axum::serve(
            listener,
            router.layer(axum::middleware::from_fn(crate::web::policy)),
        )
        .with_graceful_shutdown(crate::shutdown_signal())
        .await
    })?;
    engine.close()?;
    Ok(())
}
async fn handle(state: Arc<State>, operation: Operation, request: Request) -> Response<Body> {
    let request_id = nonce().unwrap_or_else(|_| "maintenance".into());
    let result = execute(&state, operation, request).await;
    match result {
        Ok((value, cookie)) => {
            let mut response = Response::builder()
                .status(StatusCode::OK)
                .header("content-type", "application/json")
                .header("cache-control", "no-store")
                .header("x-request-id", request_id);
            if let Some(cookie) = cookie {
                response = response.header("set-cookie", cookie);
            }
            response.body(Body::from(value.to_string())).unwrap()
        }
        Err(cause) => response::error_with_id(cause, &request_id),
    }
}
async fn execute(
    state: &State,
    operation: Operation,
    request: Request,
) -> Result<(Value, Option<String>), export_doc_engine::api::ApiError> {
    let headers = request.headers();
    let origin = headers
        .get("origin")
        .and_then(|value| value.to_str().ok())
        .ok_or_else(|| error(403, "维护请求缺少来源信息，请从维护页面操作。"));
    if headers
        .get("sec-fetch-site")
        .is_some_and(|value| value == "cross-site")
    {
        return Err(error(403, "维护请求必须来自当前服务页面。"));
    }
    let secure = if operation.method != "GET" || headers.contains_key("origin") {
        let origin = url::Url::parse(origin?).map_err(|_| error(403, "维护来源无效。"))?;
        let host = headers
            .get("host")
            .and_then(|value| value.to_str().ok())
            .unwrap_or("");
        if origin.host_str().is_none()
            || origin.origin().ascii_serialization() != format!("{}://{}", origin.scheme(), host)
            || !matches!(origin.scheme(), "http" | "https")
        {
            return Err(error(403, "维护请求来源与当前服务不匹配。"));
        }
        origin.scheme() == "https"
    } else {
        false
    };
    let mut refreshed_cookie = None;
    let mut resumed_session = None;
    if operation.requires_authentication {
        let mut credentials = state
            .credentials
            .lock()
            .map_err(|_| error(503, "维护认证状态不可用。"))?;
        let session = credentials
            .session
            .as_mut()
            .filter(|session| session.expires > Instant::now() && session.deadline > Instant::now())
            .ok_or_else(|| error(401, "维护会话已过期，请重启维护服务并重新登录。"))?;
        let cookie = headers
            .get("cookie")
            .and_then(|value| value.to_str().ok())
            .and_then(|value| {
                value
                    .split(';')
                    .filter_map(|part| part.trim().split_once('='))
                    .find(|(name, _)| *name == COOKIE)
                    .map(|(_, token)| token)
            })
            .unwrap_or("");
        if !bool::from(cookie.as_bytes().ct_eq(session.token.as_bytes())) {
            return Err(error(401, "维护会话无效。"));
        }
        if operation.method != "GET"
            && !bool::from(
                headers
                    .get("x-maintenance-csrf")
                    .map(|value| value.as_bytes())
                    .unwrap_or_default()
                    .ct_eq(session.csrf.as_bytes()),
            )
        {
            return Err(error(403, "维护页面校验已失效，请重新登录。"));
        }
        session.expires = (Instant::now() + Duration::from_secs(3600)).min(session.deadline);
        resumed_session = Some(
            json!({"csrfToken":session.csrf,"expiresInSeconds":session.expires.saturating_duration_since(Instant::now()).as_secs()}),
        );
        refreshed_cookie = Some(format!(
            "{COOKIE}={}; Path=/maintenance-api; HttpOnly; SameSite=Strict; Max-Age={}{}",
            session.token,
            session
                .expires
                .saturating_duration_since(Instant::now())
                .as_secs(),
            if session.secure { "; Secure" } else { "" }
        ));
    }
    let body = if operation.method == "GET" {
        json!({})
    } else {
        let bytes = tokio::time::timeout(
            Duration::from_secs(15),
            to_bytes(request.into_body(), 64 * 1024),
        )
        .await
        .map_err(|_| error(504, "维护请求读取超时。"))?
        .map_err(|_| error(400, "维护请求超过容量限制或未完整读取。"))?;
        if bytes.is_empty() {
            json!({})
        } else {
            serde_json::from_slice(&bytes).map_err(|_| error(400, "维护请求格式无效。"))?
        }
    };
    let schema = contracts::request(operation.id);
    if !schema.is_null() {
        export_doc_contracts::validation::structure(schema, &body)
            .map_err(|message| error(400, &message))?;
    }
    match operation.id {
        "OpenMaintenanceSession" => {
            let mut credentials = state
                .credentials
                .lock()
                .map_err(|_| error(503, "维护认证状态不可用。"))?;
            if Instant::now() < credentials.next {
                return Err(error(429, "维护凭证尝试过于频繁，请稍后再试。"));
            }
            let token = body["token"].as_str().unwrap_or("");
            if !credentials
                .bootstrap
                .as_ref()
                .is_some_and(|expected| bool::from(token.as_bytes().ct_eq(expected.as_bytes())))
            {
                credentials.failures = credentials.failures.saturating_add(1);
                credentials.next = Instant::now()
                    + Duration::from_secs((1u64 << credentials.failures.min(6)).min(60));
                return Err(error(401, "维护一次性凭证无效或已使用。"));
            }
            let session = Session {
                token: nonce().map_err(|_| error(503, "无法创建维护会话。"))?,
                csrf: nonce().map_err(|_| error(503, "无法生成页面校验。"))?,
                expires: Instant::now() + Duration::from_secs(3600),
                deadline: Instant::now() + Duration::from_secs(8 * 3600),
                secure,
            };
            let cookie = format!(
                "{COOKIE}={}; Path=/maintenance-api; HttpOnly; SameSite=Strict; Max-Age=3600{}",
                session.token,
                if secure { "; Secure" } else { "" }
            );
            let response = json!({"csrfToken":session.csrf,"expiresInSeconds":3600});
            credentials.bootstrap = None;
            credentials.session = Some(session);
            Ok((response, Some(cookie)))
        }
        "ResumeMaintenanceSession" => Ok((
            resumed_session.ok_or_else(|| error(401, "维护会话无效。"))?,
            refreshed_cookie,
        )),
        "GetMaintenanceStatus" => Ok((state.engine.status()?, refreshed_cookie)),
        "CancelMaintenanceOperation" => Ok((state.engine.cancel()?, refreshed_cookie)),
        _ => {
            let engine = state.engine.clone();
            let result = state
                .execution
                .run(
                    Duration::from_secs(crate::request::request_seconds(operation)),
                    move || {
                        if operation.id == "AbandonMaintenance" {
                            engine.abandon()
                        } else {
                            engine.start(operation.id, body)
                        }
                    },
                )
                .await?;
            Ok((result, refreshed_cookie))
        }
    }
}
