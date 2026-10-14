use super::*;
use crate::engine::team_backup::tests::{Workspace, admin, open_service};

fn request(service: &NativeService) -> Value {
    let status = status(service, &admin()).unwrap();
    json!({"expectedRevision":status["revision"], "expectedSettingsRevision":status["settingsRevision"],
        "plan":status["plan"], "webDav":status["webDav"], "webDavPassword":null,
        "s3SecretAccessKey":null, "s3SessionToken":null, "encryptionPassword":""})
}
fn configure(service: &NativeService) {
    let mut body = request(service);
    body["encryptionPassword"] = json!("scheduled-backup-test-password");
    save(service, &admin(), &body).unwrap();
}
fn wait(service: &NativeService, job: Value) -> Value {
    let deadline = Instant::now() + Duration::from_secs(30);
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
fn inactive_legacy_flags_never_enable_scheduling_and_secrets_remain_protected() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    service
        .store
        .transaction(|tx| {
            tx.set_settings(
                "settings",
                1,
                &json!({"revision":1,"system":{"postgreSqlAutoBackupEnabled":true}}),
            )?;
            Ok(())
        })
        .unwrap();
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
    assert_eq!(repository::list(&service.paths).unwrap().len(), 1);
}
#[test]
fn retention_is_owned_and_replica_failure_keeps_the_local_archive_and_previous_copy() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    configure(&service);
    let unrelated = repository::root(&service.paths).join("administrator-file.txt");
    fs::write(&unrelated, b"preserve").unwrap();
    assert_eq!(
        wait(&service, run_now(&service, &admin()).unwrap())["status"],
        "Succeeded"
    );
    let first = repository::list(&service.paths).unwrap()[0]
        .file_name
        .clone();
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let address = listener.local_addr().unwrap();
    drop(listener);
    let mut body = request(&service);
    body["plan"]["replicaMode"] = json!("WebDav");
    body["plan"]["retentionCount"] = json!(1);
    body["webDav"] =
        json!({"enabled":true,"url":format!("http://{address}/backups"),"userName":"test"});
    save(&service, &admin(), &body).unwrap();
    assert_eq!(
        wait(&service, run_now(&service, &admin()).unwrap())["status"],
        "Succeeded"
    );
    let status = status(&service, &admin()).unwrap();
    assert_eq!(status["lastResult"], "Partial");
    assert_eq!(status["archives"].as_array().unwrap().len(), 2);
    assert!(repository::root(&service.paths).join(&first).is_file());
    let mut body = request(&service);
    body["plan"]["replicaMode"] = json!("None");
    save(&service, &admin(), &body).unwrap();
    assert_eq!(
        wait(&service, run_now(&service, &admin()).unwrap())["status"],
        "Succeeded"
    );
    assert_eq!(repository::list(&service.paths).unwrap().len(), 1);
    assert_eq!(fs::read(&unrelated).unwrap(), b"preserve");
}
#[test]
fn archive_restore_uses_saved_password_and_preserves_schema_compatible_data() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    configure(&service);
    service
        .store
        .transaction(|tx| {
            tx.set_settings("restore-proof", 1, &json!({"value":"before"}))?;
            Ok(())
        })
        .unwrap();
    assert_eq!(
        wait(&service, run_now(&service, &admin()).unwrap())["status"],
        "Succeeded"
    );
    let id = repository::list(&service.paths).unwrap()[0].id.clone();
    service
        .store
        .transaction(|tx| {
            tx.set_settings("restore-proof", 2, &json!({"value":"after"}))?;
            Ok(())
        })
        .unwrap();
    let request = json!({"password":"scheduled-backup-test-password","adminPassword":"","confirmationText":"RECOVER"});
    assert_eq!(
        wait(
            &service,
            stage_restore(&service, &admin(), &id, &request).unwrap()
        )["status"],
        "Succeeded"
    );
    let paths = service.paths.clone();
    service.close().unwrap();
    drop(service);
    disaster::apply_pending(&paths).unwrap();
    let restored = NativeService::open(paths).unwrap();
    assert_eq!(
        restored.store.settings("restore-proof").unwrap().unwrap()["value"],
        "before"
    );
    assert_eq!(
        status(&restored, &admin()).unwrap()["plan"]["enabled"],
        false
    );
    restored.close().unwrap();
}
