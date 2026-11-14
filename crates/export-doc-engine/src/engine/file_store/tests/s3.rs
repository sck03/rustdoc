use super::*;
use crate::engine::test_support::{administrator, replica_fixture::Fixture};
use export_doc_domain::backup_plan::{S3Addressing, S3Settings};

#[test]
fn remote_archive_upload_does_not_hold_the_task_completion_gate() {
    use crate::engine::tasks::{ArchiveOutput, FileOutput, Jobs, TaskOutput};
    use std::{
        sync::{Arc, atomic::Ordering},
        time::{Duration, Instant},
    };
    let root = ScratchDirectory::new();
    let store = Arc::new(Store::open(&root.paths()).unwrap());
    crate::engine::auth::seed(&store).unwrap();
    let fixture = Fixture::start(root.0.clone(), true, false, None);
    let mut config = store.files.configuration().unwrap();
    config.backend = Backend::S3 {
        settings: S3Settings {
            endpoint: format!("http://{}", fixture.address),
            bucket: "fixture-bucket".into(),
            access_key_id: "fixture-access".into(),
            addressing: S3Addressing::Path,
            ..Default::default()
        },
        secret: store
            .files
            .protector
            .protect("file-storage-secret", "fixture-secret")
            .unwrap(),
        token: String::new(),
    };
    store.files.initialize_target(&config).unwrap();
    store.files.activate(config.clone()).unwrap();
    let jobs = Jobs::with_retention(store.clone(), Default::default()).unwrap();
    let actor = administrator();
    let id = store.transaction(|tx| Ok(tx.insert(&RecordWrite::unkeyed("file-test", &json!({"companyScope":"DEFAULT","versionNumber":1,"ownerUserId":1,"departmentId":"GENERAL"})))?)).unwrap();
    fixture.put_started.store(false, Ordering::Release);
    fixture.hold_put.store(true, Ordering::Release);
    let first = jobs
        .start(
            &actor,
            crate::generated_api::GET_JOB,
            "Archive",
            "归档",
            move |_| {
                let mut output = TaskOutput::file("unused".into(), "application/pdf", vec![]);
                output.file = None;
                output.archive = Some(ArchiveOutput {
                    record_id: id,
                    kind: "oa-approval-pdf",
                    file: FileOutput {
                        file_name: "archive.pdf".into(),
                        media_type: "application/pdf".into(),
                        content: b"%PDF-archive".to_vec(),
                    },
                });
                Ok(output)
            },
        )
        .unwrap();
    let deadline = Instant::now() + Duration::from_secs(5);
    while !fixture.put_started.load(Ordering::Acquire) {
        assert!(Instant::now() < deadline);
        std::thread::sleep(Duration::from_millis(10));
    }
    let second = jobs
        .start(
            &actor,
            crate::generated_api::GET_JOB,
            "Other",
            "其他任务",
            |_| {
                Ok(TaskOutput::file(
                    "other.txt".into(),
                    "text/plain",
                    b"ready".to_vec(),
                ))
            },
        )
        .unwrap();
    let deadline = Instant::now() + Duration::from_secs(5);
    let completed = loop {
        if jobs.get(&actor, second["jobId"].as_str().unwrap()).unwrap()["status"] == "Succeeded" {
            break true;
        }
        if Instant::now() >= deadline {
            break false;
        }
        std::thread::sleep(Duration::from_millis(10));
    };
    fixture.hold_put.store(false, Ordering::Release);
    let deadline = Instant::now() + Duration::from_secs(30);
    while jobs.get(&actor, first["jobId"].as_str().unwrap()).unwrap()["status"] == "Running" {
        assert!(Instant::now() < deadline, "released upload must complete");
        std::thread::sleep(Duration::from_millis(10));
    }
    jobs.close().unwrap();
    assert!(
        completed,
        "unrelated completion must finish while S3 upload is blocked"
    );
    jobs.health().unwrap();
    assert_eq!(
        jobs.get(&actor, first["jobId"].as_str().unwrap()).unwrap()["status"],
        "Succeeded"
    );
    store
        .files
        .target(&config)
        .unwrap()
        .delete("store-id")
        .unwrap();
    fixture.finish();
}

#[test]
fn small_objects_cache_integrity_and_credentials_share_one_namespace() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    crate::engine::auth::seed(&store).unwrap();
    let fixture = Fixture::start(root.0.clone(), true, false, None);
    let mut config = store.files.configuration().unwrap();
    config.backend = Backend::S3 {
        settings: S3Settings {
            endpoint: format!("http://{}", fixture.address),
            bucket: "fixture-bucket".into(),
            access_key_id: "fixture-access".into(),
            addressing: S3Addressing::Path,
            ..Default::default()
        },
        secret: store
            .files
            .protector
            .protect("file-storage-secret", "fixture-secret")
            .unwrap(),
        token: String::new(),
    };
    store.files.initialize_target(&config).unwrap();
    store.files.activate(config.clone()).unwrap();
    let prepared = store.files.prepare(b"verified object").unwrap();
    record(&store, &prepared).unwrap();
    assert!(
        !prepared.intent_path().exists(),
        "committed upload intent must be removed"
    );
    let mut first = store.files.open_object(&prepared.object).unwrap();
    let mut second = store.files.open_object(&prepared.object).unwrap();
    first.seek(SeekFrom::Start(9)).unwrap();
    let mut content = String::new();
    second.read_to_string(&mut content).unwrap();
    assert_eq!(
        content, "verified object",
        "Range readers must not share a cursor"
    );
    let body = json!({"expectedVersion":1,"accessKeyId":"fixture-access","secret":"rotated-secret","token":""});
    super::super::credentials::rotate(&store, &administrator(), &body).unwrap();
    let rotated = store.files.configuration().unwrap();
    assert_eq!(rotated.identity, config.identity);
    assert_eq!(rotated.revision, config.revision);
    assert_eq!(rotated.credential_revision, 2);
    assert_eq!(
        super::super::credentials::rotate(&store, &administrator(), &body)
            .unwrap_err()
            .status,
        Some(409)
    );
    let target = store.files.target(&rotated).unwrap();
    let object_url = url::Url::parse(&format!(
        "{}/{}",
        target.identity().strip_prefix("S3:").unwrap(),
        prepared.object.digest
    ))
    .unwrap();
    fs::write(
        root.0
            .join(crate::engine::media::digest(object_url.path().as_bytes())),
        b"corrupt",
    )
    .unwrap();
    assert!(
        store.files.open_object(&prepared.object).is_ok(),
        "verified cache remains usable"
    );
    assert!(
        store.files.verify_object(&prepared.object).is_err(),
        "integrity checks must bypass the cache"
    );
    target.delete("store-id").unwrap();
    let calls = fixture.finish();
    assert_eq!(
        calls
            .iter()
            .filter(|call| call.starts_with("POST "))
            .count(),
        0,
        "small objects use single PUT"
    );
    assert_eq!(
        calls
            .iter()
            .filter(|call| call.starts_with("GET ") && call.ends_with(&prepared.object.digest))
            .count(),
        3,
        "upload verification, one cache fill and explicit integrity check"
    );
}

#[test]
fn expired_remote_credentials_do_not_prevent_authenticated_recovery_startup() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let mut config = store.files.configuration().unwrap();
    config.backend = Backend::S3 {
        settings: S3Settings {
            endpoint: "http://127.0.0.1:1".into(),
            bucket: "fixture-bucket".into(),
            access_key_id: "expired".into(),
            addressing: S3Addressing::Path,
            ..Default::default()
        },
        secret: store
            .files
            .protector
            .protect("file-storage-secret", "expired")
            .unwrap(),
        token: String::new(),
    };
    store.files.activate(config).unwrap();
    drop(store);
    let reopened = Store::open(&root.paths()).unwrap();
    assert!(reopened.files.health().is_err());
}
