use super::*;
use crate::engine::test_support::replica_fixture::Fixture;
#[test]
fn webdav_repository_objects_are_conditional_and_read_back_before_commit() {
    let workspace = Workspace::new();
    let fixture = Fixture::start(workspace.0.clone(), false, false, None);
    let target = cloud::RecoveryTarget::from_credentials(
        &format!("http://{}/backups", fixture.address),
        "fixture",
        "fixture-secret",
        &nonce().unwrap(),
    )
    .unwrap();
    assert!(
        target
            .create_small("manifest-example", b"encrypted object")
            .unwrap()
    );
    assert_eq!(
        target
            .read_small("manifest-example", 1024)
            .unwrap()
            .unwrap(),
        b"encrypted object"
    );
    assert!(
        !target
            .create_small("manifest-example", b"must not overwrite")
            .unwrap()
    );
    target.delete("manifest-example").unwrap();
    let calls = fixture.finish();
    assert_eq!(
        calls
            .iter()
            .map(|c| c.split_once(' ').unwrap().0)
            .collect::<Vec<_>>(),
        [
            "MKCOL", "MKCOL", "PUT", "GET", "MKCOL", "MKCOL", "PUT", "DELETE"
        ]
    );
}
#[cfg(feature = "s3-backup")]
#[test]
fn s3_multipart_roundtrip_and_error_or_cancellation_abort_the_upload() {
    use crate::operation::OperationScope;
    for mode in ["success", "complete-error", "cancel"] {
        let workspace = Workspace::new();
        let scope = OperationScope::new(Duration::from_secs(40));
        let fixture = Fixture::start(
            workspace.0.clone(),
            true,
            mode == "complete-error",
            (mode == "cancel").then(|| scope.cancellation_flag()),
        );
        let source = workspace.0.join("source.bin");
        fs::File::create(&source)
            .unwrap()
            .set_len(if mode == "success" {
                9 * 1024 * 1024 + 17
            } else {
                9 * 1024 * 1024
            })
            .unwrap();
        let (size, digest) = crate::engine::backup_io::digest_file(&source).unwrap();
        let config = export_doc_domain::backup_plan::S3Settings {
            addressing: export_doc_domain::backup_plan::S3Addressing::Path,
            endpoint: format!("http://{}", fixture.address),
            bucket: "backup-bucket".into(),
            access_key_id: "fixture-access".into(),
            ..Default::default()
        };
        let result = scope.run(|| {
            let target = s3::Target::new(config, &nonce().unwrap(), "fixture-secret", "").unwrap();
            let uploaded = (|| {
                let mut stream = crate::engine::s3_transport::StreamUpload::new(
                    target.clone(),
                    "archive.edmpoint",
                    size,
                    &digest,
                )?;
                crate::engine::backup_io::copy_chunks(
                    &mut fs::File::open(&source)?,
                    &mut stream,
                    size,
                    |_| {},
                )?;
                stream.finish()?;
                let actual = target.download("archive.edmpoint", &mut std::io::sink(), size)?;
                assert_eq!(actual, (size, digest.clone()));
                Ok::<_, crate::api::ApiError>(())
            })();
            if uploaded.is_ok() {
                target.delete("archive.edmpoint").unwrap();
            }
            uploaded
        });
        let calls = fixture.finish();
        assert_eq!(result.is_ok(), mode == "success", "{mode}: {result:?}");
        assert_eq!(
            calls.iter().filter(|call| call.starts_with("PUT ")).count(),
            if mode == "cancel" { 1 } else { 2 }
        );
        assert!(calls.last().unwrap().starts_with("DELETE "));
    }
}
