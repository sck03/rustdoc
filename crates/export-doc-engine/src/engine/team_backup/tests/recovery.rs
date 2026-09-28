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
    assert!(paths.sqlite_database_path().is_file());
    assert!(paths.data_root.join("Locks/native-instance.lock").is_file());
    assert!(!paths.data_root.join("exportdoc-native.db").exists());
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
    assert!(paths.sqlite_database_path().is_file());
    assert!(!paths.data_root.join("exportdoc-native.db").exists());
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

#[test]
fn misplaced_database_blocks_new_database_creation_without_changing_original() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let original = paths.data_root.join("exportdoc-native.db");
    fs::write(&original, b"preserved database").unwrap();
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(503)));
    assert_eq!(fs::read(original).unwrap(), b"preserved database");
    assert!(!paths.sqlite_database_path().exists());
}

#[test]
fn supported_root_database_is_relocated_with_data_and_an_original_backup() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let original = paths.data_root.join("exportdoc-native.db");
    let connection = export_doc_storage::Connection::sqlite(&original).unwrap();
    connection
        .set_settings("layout-test", 1, &json!({"value":"保留已有业务"}))
        .unwrap();
    connection.checkpoint().unwrap();
    drop(connection);
    let store = Store::open(&paths).unwrap();
    assert_eq!(
        store.settings("layout-test").unwrap().unwrap()["value"],
        "保留已有业务"
    );
    assert!(paths.sqlite_database_path().is_file());
    assert!(!original.exists());
    let backup = fs::read_dir(paths.data_root.join("Backups/DatabaseLayout"))
        .unwrap()
        .next()
        .unwrap()
        .unwrap()
        .path();
    export_doc_storage::verify_sqlite_backup(&backup.join("exportdoc-native.db")).unwrap();
    drop(store);
    assert_eq!(
        Store::open(&paths)
            .unwrap()
            .settings("layout-test")
            .unwrap()
            .unwrap()["value"],
        "保留已有业务"
    );
}

#[test]
fn layout_relocation_refuses_an_old_running_instance_or_ambiguous_copies() {
    use fs2::FileExt;
    let workspace = Workspace::new();
    let paths = workspace.paths();
    drop(
        export_doc_storage::Connection::sqlite(&paths.data_root.join("exportdoc-native.db"))
            .unwrap(),
    );
    let lock = fs::File::create(paths.data_root.join("native-instance.lock")).unwrap();
    lock.lock_exclusive().unwrap();
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(429)));
    assert!(!paths.sqlite_database_path().exists());
    drop(lock);
    fs::write(paths.sqlite_database_path(), b"another database").unwrap();
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(503)));
    assert_eq!(
        fs::read(paths.sqlite_database_path()).unwrap(),
        b"another database"
    );
}
