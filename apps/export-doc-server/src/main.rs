use export_doc_engine::{engine::NativeService, paths::atomic_write};
use export_doc_server::{configuration::Configuration, router};
use export_doc_storage::Connection;
use std::{sync::Arc, time::Duration};

fn main() {
    if let Err(error) = run() {
        eprintln!("Rust API 启动或运行失败：{error}");
        std::process::exit(1);
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.first().is_some_and(|arg| arg == "--health-check") {
        if args.len() != 2 {
            return Err("健康检查须指定一个监听地址。".into());
        }
        return health_check(&args[1]);
    }
    if args.iter().any(|a| a == "--ai-review-worker") {
        return export_doc_engine::ai_worker().map_err(Into::into);
    }
    if let Some(result) = export_doc_engine::pdf::worker_args(&args) {
        return result.map_err(Into::into);
    }
    let configuration = Configuration::load()?;
    if configuration.initialize_schema {
        Connection::initialize_postgres(
            &configuration.maintenance_connection,
            &configuration.owner,
        )?;
    }
    if configuration.initialize_only {
        return Ok(());
    }
    let retention = export_doc_engine::engine::tasks::retention::Retention::from_lookup(|key| {
        std::env::var(key).ok()
    })?;
    let service = NativeService::open_postgres_with_retention(
        configuration.paths.clone(),
        &configuration.connection,
        configuration.bootstrap_token,
        configuration.business_clock,
        retention,
    )?;
    let routes = router(service.clone(), configuration.web_root.as_deref());
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .max_blocking_threads(20)
        .enable_all()
        .build()?;
    let health = service.clone();
    let result = runtime.block_on(async move {
        let listener = tokio::net::TcpListener::bind(configuration.bind).await?;
        let address = listener.local_addr()?;
        if let Some(path) = configuration.endpoint_file {
            let endpoint = serde_json::json!({"processId":std::process::id(),"address":address.to_string(),"apiBaseUrl":format!("http://{address}"),"runtime":"Rust","databaseProvider":"PostgreSQL"});
            atomic_write(&path, &serde_json::to_vec(&endpoint)?).map_err(std::io::Error::other)?;
        }
        eprintln!("ExportDocManager Rust API listening on {address}; database=PostgreSQL 18");
        let (stop, mut stopping) = tokio::sync::watch::channel(false);
        let monitor = tokio::spawn(async move {
            let mut interval = tokio::time::interval(Duration::from_secs(2));
            loop {
                interval.tick().await;
                let service = Arc::clone(&health);
                let result = tokio::task::spawn_blocking(move || service.health()).await;
                if !matches!(result, Ok(Ok(()))) {
                    let _ = stop.send(true);
                    return;
                }
            }
        });
        let server = axum::serve(listener, routes).with_graceful_shutdown(async move {
            tokio::select! {
                _ = shutdown_signal() => {},
                _ = stopping.changed() => eprintln!("数据库实例锁或连接失效，Rust API 正在停止。"),
            }
        }).await;
        monitor.abort();
        let _ = monitor.await;
        server
    });
    service.close()?;
    result?;
    Ok(())
}

async fn shutdown_signal() {
    #[cfg(unix)]
    {
        match tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate()) {
            Ok(mut terminate) => {
                tokio::select! {_ = tokio::signal::ctrl_c()=>{},_ = terminate.recv()=>{}}
            }
            Err(_) => {
                let _ = tokio::signal::ctrl_c().await;
            }
        }
    }
    #[cfg(not(unix))]
    {
        let _ = tokio::signal::ctrl_c().await;
    }
}
fn health_check(address: &str) -> Result<(), Box<dyn std::error::Error>> {
    use std::io::{Read, Write};
    let address = address.parse::<std::net::SocketAddr>()?;
    if !address.ip().is_loopback() {
        return Err("健康检查只能访问本机监听地址。".into());
    }
    let mut stream = std::net::TcpStream::connect_timeout(&address, Duration::from_secs(2))?;
    stream.set_read_timeout(Some(Duration::from_secs(2)))?;
    stream.set_write_timeout(Some(Duration::from_secs(2)))?;
    let path = export_doc_contracts::generated_api::GET_READINESS.path;
    write!(
        stream,
        "GET {path} HTTP/1.1\r\nHost: {address}\r\nConnection: close\r\n\r\n"
    )?;
    let mut response = [0_u8; 64];
    let count = stream.read(&mut response)?;
    if std::str::from_utf8(&response[..count])?.starts_with("HTTP/1.1 200 ") {
        Ok(())
    } else {
        Err("Rust API 尚未就绪。".into())
    }
}
