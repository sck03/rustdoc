use crate::paths::hex;
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    fs,
    io::{Read, Write},
    net::TcpListener,
    path::PathBuf,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    thread::JoinHandle,
    time::{Duration, Instant},
};
pub(crate) struct Fixture {
    pub(crate) address: std::net::SocketAddr,
    #[cfg(feature = "s3-storage")]
    pub(crate) hold_put: Arc<AtomicBool>,
    #[cfg(feature = "s3-storage")]
    pub(crate) put_started: Arc<AtomicBool>,
    stop: Arc<AtomicBool>,
    handle: Option<JoinHandle<Vec<String>>>,
}
impl Fixture {
    pub(crate) fn start(
        root: PathBuf,
        s3: bool,
        fail_complete: bool,
        cancel: Option<Arc<AtomicBool>>,
    ) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let address = listener.local_addr().unwrap();
        listener.set_nonblocking(true).unwrap();
        let stop = Arc::new(AtomicBool::new(false));
        let stopped = stop.clone();
        let hold_put = Arc::new(AtomicBool::new(false));
        let put_started = Arc::new(AtomicBool::new(false));
        let hold = hold_put.clone();
        let started = put_started.clone();
        let handle = std::thread::spawn(move || {
            let mut calls = Vec::new();
            let deadline = Instant::now() + Duration::from_secs(45);
            loop {
                if stopped.load(Ordering::Acquire) {
                    break;
                }
                let (mut stream, _) = match listener.accept() {
                    Ok(stream) => stream,
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        assert!(
                            Instant::now() < deadline,
                            "replica protocol fixture timed out"
                        );
                        std::thread::sleep(Duration::from_millis(5));
                        continue;
                    }
                    Err(cause) => panic!("fixture accept: {cause}"),
                };
                stream.set_nonblocking(false).unwrap();
                stream
                    .set_read_timeout(Some(Duration::from_secs(30)))
                    .unwrap();
                stream
                    .set_write_timeout(Some(Duration::from_secs(30)))
                    .unwrap();
                let mut header = Vec::new();
                while !header.ends_with(b"\r\n\r\n") {
                    let mut byte = [0];
                    stream.read_exact(&mut byte).unwrap();
                    header.push(byte[0]);
                    assert!(header.len() < 32 * 1024);
                }
                let header = String::from_utf8(header).unwrap();
                let first: Vec<_> = header.lines().next().unwrap().split_whitespace().collect();
                let method = first[0];
                let uri = url::Url::parse(&format!("http://{address}{}", first[1])).unwrap();
                let headers: BTreeMap<_, _> = header
                    .lines()
                    .skip(1)
                    .filter_map(|line| line.split_once(':'))
                    .map(|(name, value)| (name.to_ascii_lowercase(), value.trim().to_string()))
                    .collect();
                let parameters: BTreeMap<_, _> = uri
                    .query_pairs()
                    .map(|(key, value)| (key.into_owned(), value.into_owned()))
                    .collect();
                if s3 {
                    assert!(
                        headers["authorization"]
                            .starts_with("AWS4-HMAC-SHA256 Credential=fixture-access/")
                    );
                    assert!(headers["authorization"].contains("/s3/aws4_request"));
                    assert!(headers.contains_key("x-amz-date"));
                } else {
                    assert!(headers["authorization"].starts_with("Basic "));
                }
                let length = headers
                    .get("content-length")
                    .map_or(0, |value| value.parse::<u64>().unwrap());
                let mut reader = Read::by_ref(&mut stream).take(length);
                let mut hash = Sha256::new();
                let mut buffer = [0; 64 * 1024];
                let mut file = if method == "PUT" {
                    Some(
                        fs::OpenOptions::new()
                            .create(true)
                            .write(true)
                            .truncate(parameters.get("partNumber").is_none_or(|part| part == "1"))
                            .append(parameters.get("partNumber").is_some_and(|part| part != "1"))
                            .open(root.join(if s3 {
                                hex(&Sha256::digest(uri.path().as_bytes()))
                            } else {
                                "remote-object.bin".into()
                            }))
                            .unwrap(),
                    )
                } else {
                    None
                };
                loop {
                    let count = reader.read(&mut buffer).unwrap();
                    if count == 0 {
                        break;
                    }
                    hash.update(&buffer[..count]);
                    if let Some(file) = &mut file {
                        file.write_all(&buffer[..count]).unwrap();
                    }
                }
                assert_eq!(reader.limit(), 0, "upload body was truncated");
                drop(file);
                if s3 {
                    assert_eq!(headers["x-amz-content-sha256"], hex(&hash.finalize()));
                }
                calls.push(format!("{method} {}", uri.path()));
                let body = if s3 && method == "POST" && parameters.contains_key("uploads") {
                    b"<InitiateMultipartUploadResult><UploadId>upload&amp;01</UploadId></InitiateMultipartUploadResult>".as_slice()
                } else if s3 && method == "POST" {
                    assert_eq!(parameters.get("uploadId").unwrap(), "upload&01");
                    if fail_complete {
                        b"<Error><Code>InvalidPart</Code></Error>".as_slice()
                    } else {
                        b"<CompleteMultipartUploadResult><ETag>complete</ETag></CompleteMultipartUploadResult>".as_slice()
                    }
                } else {
                    b""
                };
                if method == "GET" {
                    let mut file = fs::File::open(root.join(if s3 {
                        hex(&Sha256::digest(uri.path().as_bytes()))
                    } else {
                        "remote-object.bin".into()
                    }))
                    .unwrap();
                    write!(
                        stream,
                        "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
                        file.metadata().unwrap().len()
                    )
                    .unwrap();
                    std::io::copy(&mut file, &mut stream).unwrap();
                } else {
                    if method == "MOVE" {
                        assert_eq!(headers["overwrite"], "F");
                        assert!(headers["destination"].ends_with("archive.edmrecovery"));
                    }
                    if method == "PUT" {
                        started.store(true, Ordering::Release);
                        while hold.load(Ordering::Acquire) && !stopped.load(Ordering::Acquire) {
                            std::thread::sleep(Duration::from_millis(5));
                        }
                        if let Some(cancel) = &cancel {
                            cancel.store(true, Ordering::Release);
                        }
                    }
                    let code = if matches!(method, "MKCOL" | "MOVE") {
                        201
                    } else if method == "DELETE" {
                        204
                    } else {
                        200
                    };
                    let sent = write!(stream, "HTTP/1.1 {code} OK\r\nContent-Length: {}\r\nETag: \"part\"\r\nConnection: close\r\n\r\n", body.len()).and_then(|_| stream.write_all(body));
                    if cancel.is_none() || method != "PUT" {
                        sent.unwrap();
                    }
                }
                if method == "DELETE" {
                    break;
                }
            }
            calls
        });
        Self {
            #[cfg(feature = "s3-storage")]
            hold_put,
            #[cfg(feature = "s3-storage")]
            put_started,
            address,
            stop,
            handle: Some(handle),
        }
    }
    pub(crate) fn finish(mut self) -> Vec<String> {
        self.handle.take().unwrap().join().unwrap()
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Release);
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}
