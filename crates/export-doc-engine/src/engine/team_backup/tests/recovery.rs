use super::*;
fn wait(service: &NativeService, job: &Value) -> Value {
    let deadline = Instant::now() + Duration::from_secs(120);
    loop {
        let value = service
            .jobs
            .get(&admin(), job["jobId"].as_str().unwrap())
            .unwrap();
        if !["Running", "Pending", "Queued"].contains(&value["status"].as_str().unwrap()) {
            assert_eq!(value["status"], "Succeeded", "{value}");
            return value;
        }
        assert!(Instant::now() < deadline, "backup job timed out");
        std::thread::sleep(Duration::from_millis(20));
    }
}
#[test]
fn disaster_package_download_restore_and_restart_preserve_data_keys_and_templates() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let service = open_service(&workspace);
    service
        .store
        .connection()
        .unwrap()
        .set_settings("recovery-test", 1, &json!({"value":"original"}))
        .unwrap();
    fs::create_dir_all(paths.data_root.join("Templates/Export")).unwrap();
    fs::write(
        paths.data_root.join("Templates/Export/custom.dtpl"),
        b"original template",
    )
    .unwrap();
    let secret = service.protector.protect("test", "private-value").unwrap();
    let created = disaster::create_package(
        &service,
        &admin(),
        &json!({"password":" package password "}),
    )
    .unwrap();
    let finished = wait(&service, &created);
    let output = service
        .jobs
        .download(&admin(), created["jobId"].as_str().unwrap())
        .unwrap();
    assert!(!output.content.is_empty());
    assert!(output.content.starts_with(b"EDM-DISASTER-RECOVERY-1"));
    let path = paths
        .data_root
        .join("DisasterRecovery")
        .join(finished["outputPath"].as_str().unwrap());
    assert_eq!(fs::read(&path).unwrap(), output.content);
    assert!(
        disaster::restore_package(
            &service,
            &admin(),
            &json!({"packagePath":path,"password":" package password ","confirmationText":"wrong"})
        )
        .is_err()
    );
    service
        .store
        .connection()
        .unwrap()
        .set_settings("recovery-test", 2, &json!({"value":"modified"}))
        .unwrap();
    fs::write(
        paths.data_root.join("Templates/Export/custom.dtpl"),
        b"changed template",
    )
    .unwrap();
    let job = disaster::restore_package(
        &service,
        &admin(),
        &json!({"packagePath":path,"password":" package password ","confirmationText":"RECOVER"}),
    )
    .unwrap();
    wait(&service, &job);
    // A second instance cannot apply a pending restore under the running API.
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(429)));
    assert_eq!(
        service.store.settings("recovery-test").unwrap().unwrap()["value"],
        "modified"
    );
    service.close().unwrap();
    drop(service);
    let restored = open_service(&workspace);
    assert_eq!(
        restored.store.settings("recovery-test").unwrap().unwrap()["value"],
        "original"
    );
    assert_eq!(
        fs::read(paths.data_root.join("Templates/Export/custom.dtpl")).unwrap(),
        b"original template"
    );
    assert_eq!(
        &*restored.protector.unprotect("test", &secret).unwrap(),
        "private-value"
    );
    assert_eq!(
        disaster::status(&restored, &admin()).unwrap()["pendingRestore"],
        false
    );
    restored.close().unwrap();
}

#[test]
fn staged_package_rejects_path_traversal_before_reading_external_files() {
    let workspace = Workspace::new();
    let marker = workspace.0.join("pending.json");
    fs::write(&marker, br#"{"stagingDirectoryName":"../../escape"}"#).unwrap();
    assert!(package::staged(&marker, package::SQLITE).is_err());
}
