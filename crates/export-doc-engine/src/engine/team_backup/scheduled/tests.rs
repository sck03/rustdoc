use super::*;
use crate::engine::team_backup::tests::{TestService, Workspace, admin, open_service};
use std::time::Instant;
fn request(service: &NativeService) -> Value {
    let state = status(service, &admin()).unwrap();
    json!({"expectedRevision":state["revision"],"expectedSettingsRevision":state["settingsRevision"],"plan":state["plan"],"webDav":state["webDav"],"webDavPassword":null,"s3SecretAccessKey":null,"s3SessionToken":null,"encryptionPassword":""})
}
fn configure(service: &NativeService) {
    let mut body = request(service);
    body["encryptionPassword"] = json!("scheduled-backup-test-password");
    save(service, &admin(), &body).unwrap();
}
fn wait(service: &NativeService, job: Value) -> Value {
    let deadline = Instant::now() + Duration::from_secs(60);
    loop {
        let value = service
            .jobs
            .get(&admin(), job["jobId"].as_str().unwrap())
            .unwrap();
        if matches!(
            value["status"].as_str(),
            Some("Succeeded" | "Failed" | "Canceled")
        ) {
            return value;
        }
        assert!(Instant::now() < deadline, "backup timed out: {value}");
        std::thread::sleep(Duration::from_millis(10));
    }
}
#[test]
fn recovery_point_exports_independently_and_restores_directly_to_registered_library() {
    let workspace = Workspace::new();
    let external = Workspace::new();
    let mut paths = workspace.paths();
    paths
        .register_file_storage_root("nas", external.0.clone())
        .unwrap();
    let service = TestService::open(paths.clone());
    configure(&service);
    service
        .store
        .connection()
        .unwrap()
        .set_settings("recovery-test", 1, &json!({"value":"original"}))
        .unwrap();
    let prepared = service.store.files.prepare(b"original attachment").unwrap();
    let record=service.store.transaction(|tx|{
  let body=json!({"companyScope":"DEFAULT","versionNumber":1,"ownerUserId":1,"departmentId":"GENERAL"});
  let id=tx.insert(&export_doc_storage::RecordWrite{kind:"repository-test",identity:None,body:&body})?;
  tx.publish_file(&prepared,"original",id,"original.bin","application/octet-stream")?;Ok(id)
 }).unwrap();
    drop(prepared);
    let backup = wait(&service, run_now(&service, &admin()).unwrap());
    assert_eq!(backup["status"], "Succeeded", "{backup}");
    let listed = archives(&service, &admin(), &[]).unwrap();
    let id = listed["items"][0]["id"].as_str().unwrap();
    let destination = paths.cache_root.join("portable.edmpoint");
    let exported = wait(
        &service,
        save_to_path(
            &service,
            &admin(),
            id,
            &json!({"destinationPath":destination}),
        )
        .unwrap(),
    );
    assert_eq!(exported["status"], "Succeeded", "{exported}");
    let (repo, point) = super::super::repository::Repository::from_portable(
        fs::File::open(&destination).unwrap(),
        "scheduled-backup-test-password",
    )
    .unwrap();
    assert_eq!(point, id);
    drop(repo);
    service
        .store
        .connection()
        .unwrap()
        .set_settings("recovery-test", 2, &json!({"value":"modified"}))
        .unwrap();
    let job=wait(&service,stage_restore(&service,&admin(),id,&json!({"password":"scheduled-backup-test-password","adminPassword":"","confirmationText":"RECOVER","fileMode":"Directory","targetLocation":"nas"})).unwrap());
    assert_eq!(job["status"], "Succeeded", "{job}");
    service.close().unwrap();
    drop(service);
    let restored = TestService::open(paths);
    assert_eq!(
        restored.store.settings("recovery-test").unwrap().unwrap()["value"],
        "original"
    );
    let info = restored
        .store
        .connection()
        .unwrap()
        .file_info(record, "original")
        .unwrap()
        .unwrap();
    assert_eq!(
        restored.store.files.load(&info, 100).unwrap().content,
        b"original attachment"
    );
    assert!(
        matches!(restored.store.files.configuration().unwrap().backend,crate::engine::file_store::Backend::Local{ref directory} if directory.starts_with("@nas/recovered-"))
    );
    restored.close().unwrap();
}
#[test]
fn scheduling_starts_disabled_and_secrets_remain_protected() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    assert_eq!(
        status(&service, &admin()).unwrap()["plan"]["enabled"],
        false
    );
    let mut body = request(&service);
    body["plan"]["enabled"] = json!(true);
    assert_eq!(
        save(&service, &admin(), &body).unwrap_err().status,
        Some(400)
    );
    body["plan"]["enabled"] = json!(false);
    body["encryptionPassword"] = json!("scheduled-backup-test-password");
    body["webDav"] = json!({"enabled":true,"url":"http://127.0.0.1/backups","userName":"backup"});
    body["webDavPassword"] = json!("private-dav-password");
    let saved = save(&service, &admin(), &body).unwrap();
    assert_eq!(saved["webDavPasswordSet"], true);
    assert_eq!(saved["encryptionPasswordSet"], true);
    assert!(!saved.to_string().contains("private-dav-password"));
    assert!(
        !service
            .store
            .settings(PLAN)
            .unwrap()
            .unwrap()
            .to_string()
            .contains("scheduled-backup-test-password")
    );
    assert!(
        !service
            .store
            .settings("protected-credentials")
            .unwrap()
            .unwrap()
            .to_string()
            .contains("private-dav-password")
    );
    assert_eq!(
        save(&service, &admin(), &body).unwrap_err().status,
        Some(409)
    );
    let mut actor = admin();
    actor.admin = false;
    assert_eq!(status(&service, &actor).unwrap_err().status, Some(403));
    let mut body = request(&service);
    body["expectedSettingsRevision"] = json!(0);
    assert_eq!(
        save(&service, &admin(), &body).unwrap_err().status,
        Some(409)
    );
    let mut body = request(&service);
    body["webDavPassword"] = json!("");
    assert_eq!(
        save(&service, &admin(), &body).unwrap()["webDavPasswordSet"],
        false
    );
}
#[test]
fn scheduled_backups_deduplicate_slots_and_defer_busy_packages() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    configure(&service);
    let mut saved = load(&service.store).unwrap();
    saved.plan.enabled = true;
    service
        .store
        .transaction(|tx| {
            tx.set_settings(PLAN, saved.revision, &serde_json::to_value(&saved)?)?;
            Ok(())
        })
        .unwrap();
    let now = DateTime::parse_from_rfc3339("2026-10-14T03:00:00Z")
        .unwrap()
        .with_timezone(&Utc);
    let lock = package::lock(&service.paths).unwrap();
    assert!(
        service
            .backup_scheduler
            .run(&service, None, now)
            .unwrap()
            .is_none()
    );
    assert_eq!(state(&service.store).unwrap()["lastResult"], "Deferred");
    drop(lock);
    assert!(
        service
            .backup_scheduler
            .run(&service, None, now + chrono::Duration::minutes(1))
            .unwrap()
            .is_none()
    );
    let job = service
        .backup_scheduler
        .run(&service, None, now + chrono::Duration::minutes(6))
        .unwrap()
        .unwrap();
    assert_eq!(wait(&service, job)["status"], "Succeeded");
    assert!(
        service
            .backup_scheduler
            .run(&service, None, now + chrono::Duration::hours(1))
            .unwrap()
            .is_none()
    );
    assert_eq!(archives(&service, &admin(), &[]).unwrap()["totalCount"], 1);
}
