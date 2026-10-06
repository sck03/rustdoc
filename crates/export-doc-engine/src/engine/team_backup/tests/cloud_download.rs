use super as cloud;
use super::super::{
    backup_root, cloud_staging_root,
    tests::{Workspace, admin, open_service},
};
use super::*;
use std::{
    io::{Read, Write},
    net::TcpListener,
    sync::mpsc,
    time::{Duration, Instant},
};

#[test]
fn cloud_download_preserves_backups_and_reclaims_staging() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    let snapshot = workspace.0.join("snapshot.sqlite3");
    service
        .store
        .connection()
        .unwrap()
        .backup(&snapshot)
        .unwrap();
    let valid = fs::read(snapshot).unwrap();
    let staging = cloud_staging_root(&service.paths).unwrap();
    let unrelated = staging.join("unrelated.tmp");
    fs::write(&unrelated, b"keep").unwrap();
    for (body, status, mode) in [
        (valid.clone(), 200, "valid"),
        (b"not sqlite".to_vec(), 200, "invalid-header"),
        (
            [&valid[..16], b"damaged database"].concat(),
            200,
            "invalid-database",
        ),
        (vec![], 200, "empty"),
        (b"server error".to_vec(), 500, "http-error"),
        (valid.clone(), 200, "truncated"),
        (valid.clone(), 200, "cancel"),
        (valid.clone(), 200, "blocked"),
    ] {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let url = format!("http://{}", listener.local_addr().unwrap());
        let length = body.len() + usize::from(mode == "truncated");
        let (ready, received) = mpsc::channel();
        let (release, proceed) = mpsc::channel();
        let server = std::thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            stream
                .set_read_timeout(Some(Duration::from_secs(5)))
                .unwrap();
            let mut request = Vec::new();
            while !request.ends_with(b"\r\n\r\n") {
                let mut byte = [0];
                stream.read_exact(&mut byte).unwrap();
                request.push(byte[0]);
            }
            ready.send(()).unwrap();
            proceed.recv_timeout(Duration::from_secs(5)).unwrap();
            write!(
                stream,
                "HTTP/1.1 {status} Test\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n"
            )
            .unwrap();
            let sent = stream.write_all(&body);
            if mode != "cancel" {
                sent.unwrap();
            }
        });
        let remote_name = format!("{mode}.sqlite3");
        let target = backup_root(&service.paths).unwrap().join(&remote_name);
        if mode == "blocked" {
            fs::create_dir(&target).unwrap();
        } else {
            fs::write(&target, b"existing backup").unwrap();
        }
        service
            .store
            .transaction(|tx| {
                tx.set_settings(
                    "settings",
                    1,
                    &json!({"webDav":{"enabled":true,"url":url,"userName":"test"}}),
                )?;
                Ok(())
            })
            .unwrap();
        let job =
            cloud::download(&service, &admin(), &json!({"remoteFileName":remote_name})).unwrap();
        received.recv_timeout(Duration::from_secs(5)).unwrap();
        if mode == "cancel" {
            service
                .jobs
                .operation(CANCEL_JOB, &admin(), job["jobId"].as_str().unwrap())
                .unwrap();
        }
        release.send(()).unwrap();
        let deadline = Instant::now() + Duration::from_secs(10);
        let result = loop {
            let result = service
                .jobs
                .get(&admin(), job["jobId"].as_str().unwrap())
                .unwrap();
            if !["Running", "Pending", "Queued"].contains(&result["status"].as_str().unwrap()) {
                break result;
            }
            assert!(Instant::now() < deadline, "download task timed out");
            std::thread::sleep(Duration::from_millis(10));
        };
        server.join().unwrap();
        if mode == "valid" {
            assert_eq!(result["status"], "Succeeded", "{result:?}");
            assert_eq!(fs::read(&target).unwrap(), valid);
        } else {
            assert_eq!(
                result["status"],
                if mode == "cancel" {
                    "Canceled"
                } else {
                    "Failed"
                },
                "{result:?}"
            );
            if mode == "blocked" {
                assert!(target.is_dir());
            } else {
                assert_eq!(fs::read(&target).unwrap(), b"existing backup");
            }
        }
        assert_eq!(fs::read(&unrelated).unwrap(), b"keep");
        assert_eq!(fs::read_dir(&staging).unwrap().count(), 1);
    }
}
