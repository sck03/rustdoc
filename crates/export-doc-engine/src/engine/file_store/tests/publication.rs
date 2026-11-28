use super::*;
use crate::engine::test_support::{TestService, administrator};

fn retained(service: &TestService) -> (String, FileObject) {
    let upload = service.store.files.prepare(b"signed evidence").unwrap();
    let object = upload.object.clone();
    let id = service.store.transaction(|tx| {
        let id = tx.insert(&RecordWrite {
            kind: "oa-submission", identity: None,
            body: &json!({"companyScope":"DEFAULT","versionNumber":1,"ownerUserId":1,"departmentId":"GENERAL"}),
        })?;
        tx.publish_file(&upload, "signed", id, "signed.pdf", "application/pdf")?;
        tx.delete_files(id, None)?;
        let key = format!("{id}:signed:{}", object.digest);
        tx.classify_retained_file(&key, "Signed", "2020-01-01", 1)?;
        Ok(key)
    }).unwrap();
    (id, object)
}

#[test]
fn archived_digest_republication_uses_online_bytes_and_reclaims_the_old_copy() {
    let root = ScratchDirectory::new();
    let external = ScratchDirectory::new();
    let mut paths = root.paths();
    paths
        .register_file_storage_root("archive", external.0.clone())
        .unwrap();
    let service = TestService::open(paths);
    let actor = administrator();
    let (id, object) = retained(&service);
    let version = service.store.files.configuration().unwrap().revision;
    super::super::archive::run(
        &service.store,
        &actor,
        &json!({"ids":[id],"location":"archive","expectedVersion":version,"reason":"已结案"}),
    )
    .unwrap();
    let archived = service
        .store
        .connection()
        .unwrap()
        .file_object(&object.digest)
        .unwrap()
        .unwrap();
    let archive_config = service.store.files.object_configuration(&archived).unwrap();
    let archived_path = service
        .store
        .files
        .object_path(&archive_config, &archived)
        .unwrap();

    // A transaction rollback keeps the original route and its verified copy.
    let upload = service.store.files.prepare(b"signed evidence").unwrap();
    let failed: Result<()> = service.store.transaction(|tx| {
        let id = tx.insert(&RecordWrite {
            kind: "report-images",
            identity: None,
            body: &json!({"companyScope":"DEFAULT","versionNumber":1}),
        })?;
        tx.publish_file(&upload, "original", id, "copy.pdf", "application/pdf")?;
        let online = service.store.files.configuration()?;
        assert!(
            service
                .store
                .files
                .discover_intents(&service.store, &online)?
                .copies
                .is_empty(),
            "an uncommitted reactivation receipt remains leased"
        );
        Err(invalid("simulate validation after publication"))
    });
    assert!(failed.is_err());
    assert_eq!(
        service
            .store
            .connection()
            .unwrap()
            .file_object(&object.digest)
            .unwrap()
            .unwrap()
            .archive_identity,
        archived.archive_identity
    );
    assert!(archived_path.exists());
    drop(upload);

    let upload = service.store.files.prepare(b"signed evidence").unwrap();
    let published = record(&service.store, &upload).unwrap();
    drop(upload);
    let info = service
        .store
        .connection()
        .unwrap()
        .file_info(published, "original")
        .unwrap()
        .unwrap();
    assert!(info.object.archive_identity.is_empty());
    // The route itself must suffice even if the old archive bytes disappear.
    let hidden = archived_path.with_extension("offline");
    fs::rename(&archived_path, &hidden).unwrap();
    assert_eq!(
        service.store.files.load(&info, 100).unwrap().content,
        b"signed evidence"
    );
    fs::rename(hidden, &archived_path).unwrap();
    let plan = super::super::cleanup::preview(&service, &actor).unwrap();
    super::super::cleanup::execute(&service.store, &actor, plan["token"].as_str().unwrap())
        .unwrap();
    assert!(!archived_path.exists());
    assert_eq!(
        service.store.files.load(&info, 100).unwrap().content,
        b"signed evidence"
    );
    let rows = super::super::retention::handle(
        &service,
        &actor,
        crate::generated_api::QUERY_FILE_RETENTION,
        &json!({}),
    )
    .unwrap();
    assert_eq!(rows["items"][0]["canArchive"], false);
    assert!(
        rows["items"][0]["archiveBlocker"]
            .as_str()
            .unwrap()
            .contains("业务记录")
    );
}

#[test]
fn interrupted_archive_copy_is_discoverable_after_restart_without_route_switch() {
    let root = ScratchDirectory::new();
    let external = ScratchDirectory::new();
    let mut paths = root.paths();
    paths
        .register_file_storage_root("archive", external.0.clone())
        .unwrap();
    let service = TestService::open(paths.clone());
    let (_, object) = retained(&service);
    let mut config = service.store.files.configuration().unwrap();
    let identity = crate::paths::nonce().unwrap();
    config.archive_locations.insert(
        identity.clone(),
        Some(ArchiveLocation {
            directory: "@archive/interrupted".into(),
            medium_identity: crate::paths::volume_identity(&external.0).unwrap(),
        }),
    );
    service.store.files.activate(config).unwrap();
    let destination = FileObject {
        archive_identity: identity,
        ..object.clone()
    };
    let target = service
        .store
        .files
        .object_configuration(&destination)
        .unwrap();
    service.store.files.initialize_target(&target).unwrap();
    let receipt = service
        .store
        .files
        .begin_archive_cleanup(&target, &destination)
        .unwrap();
    service.store.files.copy_object(&target, &object).unwrap();
    let copied_path = service
        .store
        .files
        .object_path(&target, &destination)
        .unwrap();
    assert!(copied_path.exists());
    drop(receipt);
    drop(service);
    let service = TestService::open(paths);
    let actor = administrator();
    let plan = super::super::cleanup::preview(&service, &actor).unwrap();
    assert_eq!(plan["archiveCleanupCount"], 1);
    assert_eq!(
        super::super::cleanup::execute(&service.store, &actor, plan["token"].as_str().unwrap())
            .unwrap(),
        object.size
    );
    assert!(!copied_path.exists());
    service.store.files.verify_object(&object).unwrap();
}

#[test]
fn policy_save_returns_only_committed_revision_and_policy() {
    let root = ScratchDirectory::new();
    let service = root.service();
    let actor = administrator();
    let current = service.store.files.configuration().unwrap();
    let mut policy = current.policy;
    policy.file_bytes /= 2;
    let result = super::super::admin::handle(
        &service,
        &actor,
        crate::generated_api::UPDATE_FILE_STORAGE_POLICY,
        &json!({"expectedVersion":current.revision,"policy":policy}),
    )
    .unwrap();
    assert_eq!(
        result,
        json!({"versionNumber":current.revision+1,"policy":policy})
    );
    assert_eq!(
        service.store.files.configuration().unwrap().revision,
        current.revision + 1
    );
}
