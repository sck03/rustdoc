use axum::{Router, extract::ConnectInfo, routing::get};
use export_doc_server::{Shutdown, serve};
use std::{future::pending, net::SocketAddr, time::Duration};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::{TcpListener, TcpStream},
    time::timeout,
};

async fn response(stream: &mut TcpStream) {
    let peer = stream.local_addr().unwrap();
    timeout(Duration::from_secs(5), async {
        let mut bytes = Vec::new();
        while !bytes.ends_with(b"\r\n\r\nok") {
            bytes.push(stream.read_u8().await.unwrap());
            assert!(bytes.len() < 4096);
        }
        assert!(bytes.starts_with(b"HTTP/1.1 200 OK\r\n"));
        assert!(
            String::from_utf8(bytes)
                .unwrap()
                .contains(&format!("\r\nx-client-address: {peer}\r\n"))
        );
    })
    .await
    .expect("accepted connection did not respond");
}

#[tokio::test]
async fn connection_limit_releases_capacity_and_shutdown_drains_idle_connections() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = listener.local_addr().unwrap();
    let shutdown = Shutdown::default();
    let server = tokio::spawn(serve(
        listener,
        Router::new().route(
            "/probe",
            get(|ConnectInfo(peer): ConnectInfo<SocketAddr>| async move {
                ([("x-client-address", peer.to_string())], "ok")
            }),
        ),
        shutdown.clone(),
        pending(),
    ));
    let request = b"GET /probe HTTP/1.1\r\nHost: localhost\r\n\r\n";
    let mut connections = Vec::new();
    for _ in 0..256 {
        let mut stream = TcpStream::connect(address).await.unwrap();
        stream.write_all(request).await.unwrap();
        response(&mut stream).await;
        connections.push(stream);
    }
    let mut waiting = TcpStream::connect(address).await.unwrap();
    waiting.write_all(request).await.unwrap();
    assert!(
        timeout(Duration::from_millis(200), waiting.read_u8())
            .await
            .is_err(),
        "the connection beyond the capacity must wait for a free slot"
    );
    drop(connections.pop());
    response(&mut waiting).await;
    waiting.write_all(request).await.unwrap();
    response(&mut waiting).await;
    shutdown.stop();
    timeout(Duration::from_secs(5), server)
        .await
        .expect("idle connections prevented shutdown")
        .unwrap()
        .unwrap();
}
