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
    assert_eq!(plan["reason"], "到期复核");
    assert_eq!(plan["canApprove"], false);
    assert!(plan["approvalBlocker"].as_str().unwrap().contains("申请人"));
    assert_eq!(plan["items"][0]["id"], id);
    assert_eq!(plan["items"][0]["fileName"], "signed.pdf");
    let listed = call(QUERY_FILE_RETENTION, json!({})).unwrap();
    assert_eq!(listed["plan"], plan);
    assert_eq!(listed["items"][0]["canArchive"], true);
    assert_eq!(
        call(APPROVE_RETENTION_DELETION, json!({"token":plan["token"]}))
            .unwrap_err()
            .status,
        Some(403)
    );
    let version = service.store.files.configuration().unwrap().revision;
    let source_path = root
        .0
        .join("Files/objects")
        .join(&digest[..2])
        .join(&digest);
    #[cfg(windows)]
    let blocker = {
        use std::os::windows::fs::OpenOptionsExt;
        fs::OpenOptions::new()
            .read(true)
            .share_mode(1 | 2)
            .open(&source_path)
            .unwrap()
    };
    let archived = super::super::archive::run(
        &service.store,
        &actor,
        &json!({"ids":[id],"location":"archive","expectedVersion":version,"reason":"低频资料"}),
    );
    #[cfg(windows)]
    {
        let cause = archived.unwrap_err();
        assert_eq!(
            cause.public_body("archive-test")["outcome"],
            "PartiallyCommitted"
        );
        assert!(cause.message.contains("已切换 1 个对象、15 字节"));
        assert!(source_path.exists());
        drop(blocker);
        let cleanup = super::super::cleanup::preview(&service, &actor).unwrap();
        assert_eq!(cleanup["archiveCleanupCount"], 1);
        assert_eq!(cleanup["count"], 1);
        let token = cleanup["token"].as_str().unwrap();
        let active_upload = service.store.files.prepare(b"signed evidence").unwrap();
        assert_eq!(
            super::super::cleanup::execute(&service.store, &actor, token)
                .unwrap_err()
                .status,
            Some(429)
        );
        assert!(source_path.exists());
        drop(active_upload);
        assert_eq!(
            super::super::cleanup::execute(&service.store, &actor, token).unwrap(),
            15
        );
        assert_eq!(
            super::super::cleanup::preview(&service, &actor).unwrap()["archiveCleanupCount"],
            0
        );
    }
    #[cfg(not(windows))]
    assert_eq!(archived.unwrap(), (1, 15));
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
    service.store.files.verify_object(&info.object).unwrap();
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
    let review =
        super::super::retention::handle(&service, &reviewer, QUERY_FILE_RETENTION, &json!({}))
            .unwrap();
    assert_eq!(review["plan"]["canApprove"], true);
    assert_eq!(review["plan"]["approvalBlocker"], "");
    assert_eq!(review["plan"]["token"], plan["token"]);
    assert_eq!(review["plan"]["items"], plan["items"]);
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
