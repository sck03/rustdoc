use std::time::{Duration, Instant};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::{TcpListener, TcpStream},
};

#[tokio::test]
async fn incomplete_headers_expire_without_blocking_other_clients() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = listener.local_addr().unwrap();
    let shutdown = export_doc_server::Shutdown::default();
    let (stop, stopped) = tokio::sync::oneshot::channel();
    let server = tokio::spawn(export_doc_server::serve(
        listener,
        axum::Router::new().route("/ready", axum::routing::get(|| async { "ready" })),
        shutdown,
        async {
            let _ = stopped.await;
        },
    ));
    let mut slow = TcpStream::connect(address).await.unwrap();
    slow.write_all(b"GET /ready HTTP/1.1\r\nHost: localhost\r\n")
        .await
        .unwrap();
    let started = Instant::now();
    let mut ordinary = TcpStream::connect(address).await.unwrap();
    ordinary
        .write_all(b"GET /ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")
        .await
        .unwrap();
    let mut response = vec![];
    tokio::time::timeout(Duration::from_secs(2), ordinary.read_to_end(&mut response))
        .await
        .unwrap()
        .unwrap();
    assert!(String::from_utf8(response).unwrap().contains("200 OK"));
    let mut response = vec![];
    let expired =
        tokio::time::timeout(Duration::from_secs(20), slow.read_to_end(&mut response)).await;
    let _ = stop.send(());
    server.await.unwrap().unwrap();
    let closed = expired.expect("an incomplete header must not retain the connection indefinitely");
    assert!(
        closed.is_ok()
            || closed.is_err_and(|cause| cause.kind() == std::io::ErrorKind::ConnectionReset)
    );
    assert!(started.elapsed() >= Duration::from_secs(14));
    assert!(started.elapsed() < Duration::from_secs(20));
    assert!(response.is_empty() || String::from_utf8_lossy(&response).contains("408"));
}
