use super::*;
use crate::engine::{store::Store, test_support::ScratchDirectory};
use export_doc_storage::RecordWrite;
use serde_json::json;

fn record(store: &Store, prepared: &PreparedObject) -> Result<i64> {
    store.transaction(|tx| {
        let body = json!({"companyScope":"ACME","versionNumber":1,"ownerUserId":1,"departmentId":"GENERAL"});
        let id = tx.insert(&RecordWrite {kind:"file-test",identity:None,body:&body})?;
        tx.publish_file(prepared,"original",id,"original.bin","application/octet-stream")?;
        Ok(id)
    })
}

#[test]
fn immutable_objects_deduplicate_bytes_without_merging_business_references() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let a = store.files.prepare(b"original").unwrap();
    let b = store.files.prepare(b"original").unwrap();
    let first = record(&store, &a).unwrap();
    let second = record(&store, &b).unwrap();
    assert_eq!(
        store.connection().unwrap().file_statistics().unwrap(),
        json!({"fileCount":1,"physicalBytes":8,"logicalBytes":16,"unreferencedBytes":0})
    );
    assert_eq!(store.connection().unwrap().file_usage("ACME").unwrap(), 16);
    assert!(
        store
            .connection()
            .unwrap()
            .blob(first, "original")
            .unwrap()
            .is_none()
    );
    drop((a, b));
    store
        .transaction(|tx| {
            tx.delete_files(first, None)?;
            Ok(())
        })
        .unwrap();
    store
        .with_writes_paused(|| store.files.reconcile(&store))
        .unwrap();
    let info = store
        .connection()
        .unwrap()
        .file_info(second, "original")
        .unwrap()
        .unwrap();
    assert_eq!(store.files.load(&info, 100).unwrap().content, b"original");
    store
        .transaction(|tx| {
            tx.delete_files(second, None)?;
            Ok(())
        })
        .unwrap();
    store
        .with_writes_paused(|| store.files.reconcile(&store))
        .unwrap();
    assert_eq!(
        store.connection().unwrap().file_statistics().unwrap()["fileCount"],
        0
    );
    assert!(store.files.open_object(&info.object).is_err());
}

#[test]
fn rolled_back_upload_is_recovered_only_after_its_active_lease_ends() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let prepared = store.files.prepare(b"abandoned").unwrap();
    let object = prepared.object.clone();
    store
        .with_writes_paused(|| store.files.reconcile(&store))
        .unwrap();
    assert!(store.files.open_object(&object).is_ok());
    drop(prepared);
    store
        .with_writes_paused(|| store.files.reconcile(&store))
        .unwrap();
    assert!(store.files.open_object(&object).is_err());
    assert_eq!(
        store
            .with_writes_paused(|| store.files.reconcile(&store))
            .unwrap(),
        0
    );
}

#[test]
fn organization_quota_is_transactional_and_failed_publication_has_no_reference() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let mut config = store.files.configuration().unwrap();
    config.policy.quota_bytes = 5;
    store.files.activate(config).unwrap();
    let prepared = store.files.prepare(b"123456").unwrap();
    assert_eq!(record(&store, &prepared).unwrap_err().status, Some(409));
    assert_eq!(
        store.connection().unwrap().file_statistics().unwrap()["fileCount"],
        0
    );
}

#[test]
fn migration_resumes_verified_copies_and_rejects_an_old_prepared_upload() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let original = store.files.prepare(b"saved").unwrap();
    let record_id = record(&store, &original).unwrap();
    let stale = store.files.prepare(b"in flight").unwrap();
    let mut target = store.files.configuration().unwrap();
    target.identity = paths::nonce().unwrap();
    target.revision += 1;
    target.backend = Backend::Local {
        directory: "FileArchive".into(),
    };
    store.files.initialize_target(&target).unwrap();
    store.files.initialize_target(&target).unwrap();
    let (mut count, mut bytes) = (0, 0);
    admin::copy_pending(&store, &target, false, &mut count, &mut bytes).unwrap();
    assert_eq!((count, bytes), (1, 5));
    admin::copy_pending(&store, &target, false, &mut count, &mut bytes).unwrap();
    assert_eq!((count, bytes), (1, 5));
    store
        .with_writes_paused(|| store.files.activate(target))
        .unwrap();
    assert_eq!(record(&store, &stale).unwrap_err().status, Some(409));
    let info = store
        .connection()
        .unwrap()
        .file_info(record_id, "original")
        .unwrap()
        .unwrap();
    assert_eq!(store.files.load(&info, 100).unwrap().content, b"saved");
    assert!(root.0.join("Files/objects").is_dir());
}

#[test]
fn missing_identity_configuration_and_corrupt_objects_fail_closed() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let prepared = store.files.prepare(b"original").unwrap();
    record(&store, &prepared).unwrap();
    let path = store
        .files
        .object_path(&store.files.configuration().unwrap(), &prepared.object)
        .unwrap();
    fs::write(path, b"tampered").unwrap();
    assert_eq!(
        store
            .files
            .open_object(&prepared.object)
            .unwrap_err()
            .status,
        Some(503)
    );
    fs::remove_file(root.0.join("Files/.store-id")).unwrap();
    assert!(store.files.health().is_err());
    drop((prepared, store));
    fs::remove_file(root.0.join("Configuration/file-storage.json")).unwrap();
    assert!(Store::open(&root.paths()).is_err());
}

#[test]
fn recovery_pack_verifies_every_database_object_and_detects_missing_content() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let prepared = store.files.prepare(b"backup bytes").unwrap();
    record(&store, &prepared).unwrap();
    let pack = root.0.join("objects.pack");
    export_archive(&store, &pack).unwrap();
    let staging = root.0.join("Restored");
    restore_archive(&pack, &staging).unwrap();
    let scan = |cursor: &str| Ok(store.connection()?.file_objects(cursor, 200, false)?);
    verify_local_inventory(&staging, scan).unwrap();
    let object_path = staging
        .join("Files/objects")
        .join(&prepared.object.digest[..2])
        .join(&prepared.object.digest);
    fs::remove_file(object_path).unwrap();
    assert!(verify_local_inventory(&staging, scan).is_err());
}

#[test]
fn incremental_snapshots_reuse_objects_and_restore_after_primary_collection() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let prepared = store.files.prepare(b"snapshot original").unwrap();
    let object = prepared.object.clone();
    let id = record(&store, &prepared).unwrap();
    drop(prepared);
    let backups = root.0.join("Backups");
    fs::create_dir_all(&backups).unwrap();
    let first = backups.join("first.sqlite3");
    store.connection().unwrap().backup(&first).unwrap();
    let snapshot = export_doc_storage::Connection::sqlite(&first).unwrap();
    incremental::capture(&store, &first, &first, |cursor| {
        Ok(snapshot.file_objects(cursor, 200, false)?)
    })
    .unwrap();
    let copy = backups
        .join("Objects")
        .join(&object.digest[..2])
        .join(&object.digest);
    let modified = fs::metadata(&copy).unwrap().modified().unwrap();
    let second = backups.join("second.sqlite3");
    store.connection().unwrap().backup(&second).unwrap();
    incremental::capture(&store, &second, &second, |cursor| {
        Ok(snapshot.file_objects(cursor, 200, false)?)
    })
    .unwrap();
    assert_eq!(fs::metadata(&copy).unwrap().modified().unwrap(), modified);
    store
        .transaction(|tx| {
            tx.delete_files(id, None)?;
            Ok(())
        })
        .unwrap();
    store
        .with_writes_paused(|| store.files.reconcile(&store))
        .unwrap();
    assert!(store.files.open_object(&object).is_err());
    incremental::restore(&store.files, &first).unwrap();
    assert!(store.files.open_object(&object).is_ok());
    fs::write(&copy, b"tampered").unwrap();
    assert!(incremental::restore(&store.files, &second).is_err());
    drop(snapshot);
}

#[test]
fn registered_mounts_reject_unknown_names_missing_markers_and_system_directories() {
    let root = ScratchDirectory::new();
    let app = root.0.join("App");
    let data = root.0.join("Data");
    let mount = root.0.join("Disk");
    fs::create_dir_all(&app).unwrap();
    fs::create_dir_all(&mount).unwrap();
    let mut paths = RuntimePaths::server(&app, &data).unwrap();
    paths
        .register_file_storage_root("archive", mount.clone())
        .unwrap();
    let store = Store::open(&paths).unwrap();
    let mut target = store.files.configuration().unwrap();
    target.identity = crate::paths::nonce().unwrap();
    target.backend = Backend::Local {
        directory: "@unknown".into(),
    };
    assert!(store.files.initialize_target(&target).is_err());
    target.backend = Backend::Local {
        directory: "Database/Objects".into(),
    };
    assert!(store.files.initialize_target(&target).is_err());
    target.backend = Backend::Local {
        directory: "@archive".into(),
    };
    store.files.initialize_target(&target).unwrap();
    store.files.activate(target).unwrap();
    assert!(store.files.prepare(b"mounted").is_ok());
    fs::remove_file(mount.join(".store-id")).unwrap();
    assert!(store.files.prepare(b"must fail").is_err());
    assert!(!mount.join(".store-id").exists());
}

#[test]
fn backup_collection_preserves_all_recovery_points_and_fails_closed_on_bad_inventory() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let prepared = store.files.prepare(b"retained bytes").unwrap();
    let object = prepared.object.clone();
    let id = record(&store, &prepared).unwrap();
    let backups = root.0.join("Backups");
    fs::create_dir_all(&backups).unwrap();
    let cache = root.0.join("Cache");
    let save = |name: &str| {
        let snapshot = backups.join(name);
        store.connection().unwrap().backup(&snapshot).unwrap();
        let database = export_doc_storage::Connection::sqlite(&snapshot).unwrap();
        incremental::capture(&store, &snapshot, &snapshot, |cursor| {
            Ok(database.file_objects(cursor, 200, false)?)
        })
        .unwrap();
        snapshot
    };
    let first = save("first.sqlite3");
    let second = save("second.sqlite3");
    drop(prepared);
    store
        .transaction(|tx| {
            tx.delete_files(id, None)?;
            Ok(())
        })
        .unwrap();
    store
        .with_writes_paused(|| store.files.reconcile(&store))
        .unwrap();
    let empty = save("empty.sqlite3");
    let path = backups
        .join("Objects")
        .join(&object.digest[..2])
        .join(&object.digest);
    assert_eq!(incremental::collect(&backups, &cache).unwrap(), 0);
    assert!(path.exists());
    for snapshot in [first, second] {
        fs::remove_file(&snapshot).unwrap();
        fs::remove_file(snapshot.with_extension("objects.jsonl")).unwrap();
    }
    let manifest = empty.with_extension("objects.jsonl");
    let bytes = fs::read(&manifest).unwrap();
    fs::write(&manifest, b"invalid\n").unwrap();
    assert!(incremental::collect(&backups, &cache).is_err());
    assert!(path.exists());
    fs::write(manifest, bytes).unwrap();
    assert_eq!(incremental::collect(&backups, &cache).unwrap(), object.size);
    assert!(!path.exists());
    assert_eq!(incremental::collect(&backups, &cache).unwrap(), 0);
}
