use super::*;
use crate::engine::test_support::replica_fixture::Fixture;
#[test]
fn webdav_replica_streams_verifies_and_publishes_with_move() {
    let workspace = Workspace::new();
    let fixture = Fixture::start(workspace.0.clone(), false, false, None);
    let service = open_service(&workspace);
    service.store.transaction(|tx| {
        tx.set_settings("settings", 1, &json!({"webDav":{"enabled":true,"url":format!("http://{}/backups", fixture.address),"userName":"fixture"}}))?; Ok(())
    }).unwrap();
    let source = workspace.0.join("source.bin");
    fs::write(&source, vec![7; 1024 * 1024 + 3]).unwrap();
    let (size, digest) = crate::engine::backup_io::digest_file(&source).unwrap();
    let target =
        cloud::RecoveryTarget::read(&service.store, &service.protector, &nonce().unwrap()).unwrap();
    target
        .upload(&source, "archive.edmrecovery", size, &digest, true)
        .unwrap();
    target.delete("archive.edmrecovery").unwrap();
    let calls = fixture.finish();
    assert_eq!(
        calls
            .iter()
            .map(|call| call.split_once(' ').unwrap().0)
            .collect::<Vec<_>>(),
        ["MKCOL", "PUT", "GET", "MOVE", "DELETE"]
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
                64 * 1024 * 1024 + 17
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
            let uploaded = target.upload(&source, "archive.edmrecovery", size, &digest, true);
            if uploaded.is_ok() {
                target.delete("archive.edmrecovery").unwrap();
            }
            uploaded
        });
        let calls = fixture.finish();
        assert_eq!(result.is_ok(), mode == "success", "{mode}: {result:?}");
        assert_eq!(
            calls.iter().filter(|call| call.starts_with("PUT ")).count(),
            if mode == "success" { 2 } else { 1 }
        );
        assert!(calls.last().unwrap().starts_with("DELETE "));
    }
}
