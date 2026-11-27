use super::*;
use crate::engine::{
    auth,
    test_support::{TestService, administrator},
};
use crate::generated_api::*;
#[test]
fn archive_routes_are_verified_and_retention_requires_an_independent_reviewer() {
    let root = ScratchDirectory::new();
    let external = ScratchDirectory::new();
    let mut paths = root.paths();
    paths
        .register_file_storage_root("archive", external.0.clone())
        .unwrap();
    let service = TestService::open(paths);
    let actor = administrator();
    let prepared = service.store.files.prepare(b"signed evidence").unwrap();
    let digest = prepared.object.digest.clone();
    let id = service
        .store
        .transaction(|tx| {
            let id = tx.insert(&RecordWrite {
                kind: "oa-submission",
                identity: None,
                body: &json!({"companyScope":"DEFAULT","versionNumber":1,"ownerUserId":1,"departmentId":"GENERAL"}),
            })?;
            tx.publish_file(&prepared, "signed", id, "signed.pdf", "application/pdf")?;
            tx.delete_files(id, None)?;
            Ok(format!("{id}:signed:{digest}"))
        })
        .unwrap();
    drop(prepared);
    let call = |op, body| super::super::retention::handle(&service, &actor, op, &body);
    call(CLASSIFY_RETAINED_FILE,json!({"id":id,"archiveClass":"Signed","basisDate":"2020-01-01","expectedVersion":1,"reason":"已结案"})).unwrap();
    call(
        SAVE_RETENTION_POLICY,
        json!({"archiveClass":"Signed","days":1,"expectedVersion":0,"reason":"测试期限"}),
    )
    .unwrap();
    let plan = call(
        PREVIEW_RETENTION_DELETION,
        json!({"ids":[id],"reason":"到期复核"}),
    )
    .unwrap();
    assert_eq!(
        call(APPROVE_RETENTION_DELETION, json!({"token":plan["token"]}))
            .unwrap_err()
            .status,
        Some(403)
    );
    let version = service.store.files.configuration().unwrap().revision;
    assert_eq!(
        super::super::archive::run(
            &service.store,
            &actor,
            &json!({"ids":[id],"location":"archive","expectedVersion":version,"reason":"低频资料"})
        )
        .unwrap(),
        (1, 15)
    );
    let row = service
        .store
        .connection()
        .unwrap()
        .retained_file(&id)
        .unwrap()
        .unwrap();
    assert!(!row["archiveIdentity"].as_str().unwrap().is_empty());
    let info =
        super::super::retention::download_info(&service.store, &actor, &[("id", id.clone())])
            .unwrap();
    assert_eq!(
        service.store.files.load(&info, 100).unwrap().content,
        b"signed evidence"
    );
    assert!(
        !root
            .0
            .join("Files/objects")
            .join(&digest[..2])
            .join(&digest)
            .exists()
    );
    // Streaming backup follows the same catalog route without staging an entire file.
    let mut backup = Vec::new();
    service
        .store
        .files
        .stream_object(&info.object, &mut backup)
        .unwrap();
    assert_eq!(backup, b"signed evidence");
    let external_file = service
        .store
        .files
        .object_path(
            &service
                .store
                .files
                .object_configuration(&info.object)
                .unwrap(),
            &info.object,
        )
        .unwrap();
    let snapshot_path = root.0.join("archive-snapshot.sqlite3");
    service
        .store
        .connection()
        .unwrap()
        .backup(&snapshot_path, &mut |_| Ok(()))
        .unwrap();
    let snapshot = export_doc_storage::Connection::sqlite_read_only(&snapshot_path).unwrap();
    super::super::incremental::capture(&service.store, &snapshot_path, &snapshot_path, |cursor| {
        Ok(snapshot.file_objects(cursor, 100, false)?)
    })
    .unwrap();
    drop(snapshot);
    fs::remove_file(&external_file).unwrap();
    assert!(service.store.files.load(&info, 100).is_err());
    super::super::incremental::restore(&service.store.files, &snapshot_path).unwrap();
    assert_eq!(
        service.store.files.load(&info, 100).unwrap().content,
        b"signed evidence"
    );
    let mut reviewer = actor.clone();
    reviewer.id = service
        .store
        .transaction(|tx| {
            let original = tx.get("users", 1)?.unwrap();
            let mut body = original;
            body["username"] = json!("reviewer");
            body["fullName"] = json!("复核员");
            let saved = crate::engine::store::save(
                tx,
                "users",
                0,
                body,
                Some("reviewer".into()),
                &actor,
                "create",
            )?;
            Ok(saved["id"].as_i64().unwrap())
        })
        .unwrap();
    assert!(
        auth::refresh_actor_in(&service.store.connection().unwrap(), &reviewer)
            .unwrap()
            .admin
    );
    // Archive location change invalidates the old deletion preflight.
    assert_eq!(
        super::super::retention::handle(
            &service,
            &reviewer,
            APPROVE_RETENTION_DELETION,
            &json!({"token":plan["token"]})
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let plan = call(
        PREVIEW_RETENTION_DELETION,
        json!({"ids":[id],"reason":"重新复核"}),
    )
    .unwrap();
    call(
        HOLD_RETAINED_FILE,
        json!({"digest":digest,"active":true,"expectedVersion":0,"reason":"调查保留"}),
    )
    .unwrap();
    assert_eq!(
        super::super::retention::handle(
            &service,
            &reviewer,
            APPROVE_RETENTION_DELETION,
            &json!({"token":plan["token"]})
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    call(
        HOLD_RETAINED_FILE,
        json!({"digest":digest,"active":false,"expectedVersion":1,"reason":"调查结束"}),
    )
    .unwrap();
    let plan = call(
        PREVIEW_RETENTION_DELETION,
        json!({"ids":[id],"reason":"重新复核"}),
    )
    .unwrap();
    super::super::retention::handle(
        &service,
        &reviewer,
        APPROVE_RETENTION_DELETION,
        &json!({"token":plan["token"]}),
    )
    .unwrap();
    service.store.files.reconcile(&service.store).unwrap();
    assert!(!external_file.exists());
}
