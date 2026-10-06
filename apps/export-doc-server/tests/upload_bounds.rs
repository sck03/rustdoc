use axum::{body::Body, http::Request};
use export_doc_engine::{
    engine::NativeService,
    generated_api::{LOGIN, PREVIEW_CRM_CUSTOMER_IMPORT, UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB},
    paths::{RuntimePaths, nonce},
};
use std::{
    io::{BufRead, BufReader, Write},
    net::{Shutdown, SocketAddr, TcpStream},
    path::PathBuf,
    time::Duration,
};
use tower::ServiceExt;

// Leave the chunked body open: the server must reject the offending field
// without waiting for a closing multipart boundary or the next body chunk.
fn unfinished_upload(
    address: SocketAddr,
    token: &str,
    path: &str,
    content: &str,
    prefix: &[u8],
    eof: bool,
) -> String {
    let mut stream = TcpStream::connect(address).unwrap();
    let timeout = Some(Duration::from_secs(3));
    stream.set_read_timeout(timeout).unwrap();
    stream.set_write_timeout(timeout).unwrap();
    write!(stream, "POST {path} HTTP/1.1\r\nHost: {address}\r\nAuthorization: Bearer {token}\r\nContent-Type: {content}\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n{:x}\r\n", prefix.len()).unwrap();
    stream.write_all(prefix).unwrap();
    stream.write_all(b"\r\n").unwrap();
    if eof {
        stream.shutdown(Shutdown::Write).unwrap();
    }
    let mut response = String::new();
    match BufReader::new(stream).read_line(&mut response) {
        Ok(_) => response,
        Err(error) => format!("read failed: {error}"),
    }
}

#[tokio::test]
async fn uploads_reject_oversized_fields_before_consuming_the_rest_of_the_request() {
    let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_owned();
    let root = workspace
        .join(".codex-runtime/http-upload-tests")
        .join(nonce().unwrap());
    let service = NativeService::open(RuntimePaths::server(&workspace, &root).unwrap()).unwrap();
    let app = export_doc_server::router(service.clone(), None);
    let credentials = Some(serde_json::json!({"username":"admin","password":""}));
    let login = service.dispatch(LOGIN, &[], &[], credentials, "").unwrap();
    let login: serde_json::Value = serde_json::from_slice(&login).unwrap();
    let token = login["accessToken"].as_str().unwrap().to_owned();
    let mut bytes =
        b"--test\r\nContent-Disposition: form-data; name=file; filename=huge.csv\r\n\r\n".to_vec();
    bytes.resize(34 * 1024 * 1024, b'x');
    bytes.extend_from_slice(b"\r\n--test--\r\n");
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri(PREVIEW_CRM_CUSTOMER_IMPORT.path)
                .header("authorization", format!("Bearer {token}"))
                .header("content-type", "multipart/form-data; boundary=test")
                .body(Body::from(bytes))
                .unwrap(),
        )
        .await
        .unwrap();
    let network_app = app.clone();
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = listener.local_addr().unwrap();
    let (stop, stopped) = tokio::sync::oneshot::channel();
    let server = tokio::spawn(async move {
        axum::serve(listener, network_app)
            .with_graceful_shutdown(async {
                let _ = stopped.await;
            })
            .await
            .unwrap()
    });
    let responses = tokio::task::spawn_blocking(move || {
        let mut field = b"--test\r\nContent-Disposition: form-data; name=note\r\n\r\n".to_vec();
        field.resize(field.len() + 65536 + 128, b'x');
        let metadata = unfinished_upload(address, &token, PREVIEW_CRM_CUSTOMER_IMPORT.path, "multipart/form-data; boundary=test", &field, false);
        let mut file = b"--test\r\nContent-Disposition: form-data; name=file; filename=huge.csv\r\n\r\n".to_vec();
        file.resize(file.len() + 16 * 1024 * 1024 + 128, b'x');
        let oversized = unfinished_upload(address, &token, PREVIEW_CRM_CUSTOMER_IMPORT.path, "multipart/form-data; boundary=test", &file, false);
        let mut files = Vec::new();
        for _ in 0..101 {
            files.extend_from_slice(b"--test\r\nContent-Disposition: form-data; name=files; filename=a.pdf\r\n\r\n%PDF-\r\n");
        }
        let count = unfinished_upload(address, &token, UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB.path, "multipart/form-data; boundary=test", &files, false);
        let broken = unfinished_upload(address, &token, LOGIN.path, "application/json", b"{", true);
        [metadata, oversized, count, broken]
    }).await.unwrap();
    stop.send(()).unwrap();
    server.await.unwrap();
    drop(app);
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
    assert_eq!(response.status().as_u16(), 413);
    for (response, expected) in responses.iter().zip([400, 413, 400, 400]) {
        assert!(
            response.starts_with(&format!("HTTP/1.1 {expected}")),
            "expected {expected}: {responses:#?}"
        );
    }
}
