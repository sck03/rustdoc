use super::*;
use std::{
    io::{Read, Write},
    net::{TcpListener, TcpStream},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::Instant,
};

fn tls() -> MakeTlsConnector {
    MakeTlsConnector::new(native_tls::TlsConnector::new().unwrap())
}
fn deadline<T>(work: impl FnOnce() -> T) -> T {
    operation::run(
        Arc::new(AtomicBool::new(false)),
        Instant::now() + Duration::from_millis(150),
        work,
    )
}

#[test]
fn stalled_postgres_handshake_respects_caller_deadline() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let (release, stop) = std::sync::mpsc::channel();
    let server = std::thread::spawn(move || {
        let (_socket, _) = listener.accept().unwrap();
        let _ = stop.recv_timeout(Duration::from_secs(3));
    });
    let mut config = Config::new();
    config
        .host("127.0.0.1")
        .port(port)
        .user("test")
        .ssl_mode(tokio_postgres::config::SslMode::Disable);
    let started = Instant::now();
    let result = deadline(|| Client::connect(config, tls()));
    release.send(()).unwrap();
    server.join().unwrap();
    assert!(matches!(result, Err(error) if error.kind == ErrorKind::Timeout));
    assert!(started.elapsed() < Duration::from_secs(2));
}

#[test]
#[ignore = "requires isolated PostgreSQL 18"]
fn postgres_cancellation_drains_and_blackhole_closes_the_connection() {
    let connection = std::env::var("EXPORTDOC_TEST_POSTGRES_APP").unwrap();
    let config: Config = connection.parse().unwrap();
    let client = Client::connect(config.clone(), tls()).unwrap();
    let started = Instant::now();
    assert_eq!(
        deadline(|| client.query("SELECT pg_sleep(10)", &[]))
            .unwrap_err()
            .kind,
        ErrorKind::Timeout
    );
    assert!(started.elapsed() < Duration::from_secs(2));
    assert_eq!(
        client.query_one("SELECT 42", &[]).unwrap().get::<_, i32>(0),
        42,
        "confirmed cancellation leaves the connection reusable"
    );
    drop(client);

    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let upstream_port = config.get_ports()[0];
    let discard = Arc::new(AtomicBool::new(false));
    let stopped = Arc::new(AtomicBool::new(false));
    let gate = discard.clone();
    let stop = stopped.clone();
    let proxy = std::thread::spawn(move || {
        let (incoming, _) = listener.accept().unwrap();
        let upstream = TcpStream::connect(("127.0.0.1", upstream_port)).unwrap();
        std::thread::scope(|threads| {
            for (mut input, mut output) in [
                (incoming.try_clone().unwrap(), upstream.try_clone().unwrap()),
                (upstream, incoming),
            ] {
                let gate = &gate;
                let stop = &stop;
                threads.spawn(move || {
                    input
                        .set_read_timeout(Some(Duration::from_millis(20)))
                        .unwrap();
                    output
                        .set_write_timeout(Some(Duration::from_millis(100)))
                        .unwrap();
                    let mut bytes = [0u8; 8192];
                    while !stop.load(Ordering::Acquire) {
                        match input.read(&mut bytes) {
                            Ok(0) => break,
                            Ok(size) => {
                                if !gate.load(Ordering::Acquire)
                                    && output.write_all(&bytes[..size]).is_err()
                                {
                                    break;
                                }
                            }
                            Err(error)
                                if matches!(
                                    error.kind(),
                                    std::io::ErrorKind::WouldBlock | std::io::ErrorKind::TimedOut
                                ) =>
                            {
                                ()
                            }
                            Err(_) => break,
                        }
                    }
                });
            }
        });
    });
    let mut via_proxy = Config::new();
    via_proxy
        .host("127.0.0.1")
        .port(port)
        .user(config.get_user().unwrap())
        .password(config.get_password().unwrap())
        .dbname(config.get_dbname().unwrap())
        .ssl_mode(tokio_postgres::config::SslMode::Disable);
    let client = Client::connect(via_proxy, tls()).unwrap();
    client.query_one("SELECT 1", &[]).unwrap();
    discard.store(true, Ordering::Release);
    let started = Instant::now();
    let result = deadline(|| client.query("SELECT 1", &[]));
    let closed = client.is_closed();
    stopped.store(true, Ordering::Release);
    drop(client);
    proxy.join().unwrap();
    assert_eq!(result.unwrap_err().kind, ErrorKind::Timeout);
    assert!(closed, "an unconfirmed outcome must close the wire");
    assert!(started.elapsed() < Duration::from_secs(3));
}
