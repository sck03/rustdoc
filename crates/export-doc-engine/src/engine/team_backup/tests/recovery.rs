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
    assert!(output.content.starts_with(b"EDM-RECOVERY-POINT-1\n"));
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
    drop(restored);
    fs::write(paths.sqlite_database_path(), b"damaged SQLite database").unwrap();
    assert!(Store::open(&paths).is_err());
    super::super::snapshot::import_sqlite(&paths, &path, "wrong-password").unwrap_err();
    assert_eq!(
        fs::read(paths.sqlite_database_path()).unwrap(),
        b"damaged SQLite database"
    );
    super::super::snapshot::import_sqlite(&paths, &path, " package password ").unwrap();
    let recovered = open_service(&workspace);
    assert_eq!(
        recovered.store.settings("recovery-test").unwrap().unwrap()["value"],
        "original"
    );
    recovered.close().unwrap();
}

#[test]
fn staged_package_rejects_path_traversal_before_reading_external_files() {
    let workspace = Workspace::new();
    let marker = workspace.0.join("pending.json");
    fs::write(&marker, br#"{"stagingDirectoryName":"../../escape"}"#).unwrap();
    assert!(
        package::staged(
            &RuntimePaths::server(&workspace.0, &workspace.0.join("Data")).unwrap(),
            &marker,
            package::SQLITE,
            &std::sync::Arc::new(crate::temporary_space::Budget::new(workspace.0.clone()))
        )
        .is_err()
    );
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
fn current_schema_in_an_old_layout_is_rejected_without_relocation() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let original = paths.data_root.join("exportdoc-native.db");
    let connection = export_doc_storage::Connection::sqlite(&original).unwrap();
    connection
        .set_settings("layout-test", 1, &json!({"value":"保留已有业务"}))
        .unwrap();
    connection.checkpoint().unwrap();
    drop(connection);
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(503)));
    assert!(!paths.sqlite_database_path().exists());
    assert!(!paths.data_root.join("Backups/DatabaseLayout").exists());
    let connection = export_doc_storage::Connection::sqlite(&original).unwrap();
    assert_eq!(
        connection.settings("layout-test").unwrap().unwrap()["value"],
        "保留已有业务"
    );
}

#[test]
fn unsupported_root_layout_cannot_override_the_managed_database() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    drop(
        export_doc_storage::Connection::sqlite(&paths.data_root.join("exportdoc-native.db"))
            .unwrap(),
    );
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(503)));
    assert!(!paths.sqlite_database_path().exists());
    fs::write(paths.sqlite_database_path(), b"another database").unwrap();
    assert!(matches!(Store::open(&paths), Err(cause) if cause.status == Some(503)));
    assert_eq!(
        fs::read(paths.sqlite_database_path()).unwrap(),
        b"another database"
    );
}

#[test]
fn database_snapshot_stages_without_replacing_live_accounts_and_restores_after_shutdown() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let service = open_service(&workspace);
    service
        .store
        .connection()
        .unwrap()
        .set_settings("snapshot-test", 1, &json!({"value":"before"}))
        .unwrap();
    let source = crate::engine::maintenance::create(&service.store, &paths, &admin()).unwrap();
    service
        .store
        .connection()
        .unwrap()
        .set_settings("snapshot-test", 2, &json!({"value":"after"}))
        .unwrap();
    let account = crate::engine::accounts::save(&service.store, &admin(), 0, json!({"username":"later-admin","fullName":"恢复管理员","role":"Admin","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Snapshot-Test-2026"})).unwrap()["user"].clone();
    let later = auth::current_actor(&service.store, account["id"].as_i64().unwrap()).unwrap();
    let job = super::super::snapshot::stage_sqlite(&service, &later, &json!({"backupFileName":source.file_name().unwrap().to_str().unwrap(),"confirmationText":"RESTORE"})).unwrap();
    wait(&service, &job);
    assert_eq!(
        service.store.settings("snapshot-test").unwrap().unwrap()["value"],
        "after"
    );
    assert!(Store::open(&paths).is_err());
    service.close().unwrap();
    drop(service);
    let restored = open_service(&workspace);
    assert_eq!(
        restored.store.settings("snapshot-test").unwrap().unwrap()["value"],
        "before"
    );
    assert!(!disaster::marker(&paths).unwrap().exists());
    assert_eq!(
        auth::current_actor(&restored.store, later.id)
            .unwrap_err()
            .status,
        Some(404)
    );
    restored.close().unwrap();
}
