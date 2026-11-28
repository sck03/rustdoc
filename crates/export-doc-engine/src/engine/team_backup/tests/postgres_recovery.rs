use super::*;
fn wait(service: &NativeService, actor: &Actor, job: &Value) -> Value {
    let deadline = Instant::now() + Duration::from_secs(120);
    loop {
        let value = service
            .jobs
            .get(actor, job["jobId"].as_str().unwrap())
            .unwrap();
        if value["status"] != "Running" {
            assert_eq!(value["status"], "Succeeded", "{value}");
            return value;
        }
        assert!(Instant::now() < deadline);
        std::thread::sleep(Duration::from_millis(20));
    }
}
#[test]
#[ignore = "requires isolated PostgreSQL 18 and native client tools"]
fn postgres_migration_and_physical_restore_preserve_roles_and_reject_live_instances() {
    let app = std::env::var("EXPORTDOC_TEST_RECOVERY_APP").unwrap();
    let maintenance = std::env::var("EXPORTDOC_TEST_RECOVERY_MAINTENANCE").unwrap();
    let source = PathBuf::from(std::env::var("EXPORTDOC_TEST_PG_BIN").unwrap());
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let bin = paths.app_root.join("Tools/PostgreSQL/bin");
    fs::create_dir_all(&bin).unwrap();
    for entry in fs::read_dir(source).unwrap() {
        let entry = entry.unwrap();
        let name = entry.file_name();
        let name = name.to_string_lossy();
        if matches!(
            name.as_ref(),
            "pg_dump" | "pg_restore" | "psql" | "pg_dump.exe" | "pg_restore.exe" | "psql.exe"
        ) || name.ends_with(".dll")
        {
            fs::copy(entry.path(), bin.join(name.as_ref())).unwrap();
        }
    }
    export_doc_storage::Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let open = || {
        NativeService::open_postgres(paths.clone(), &app, "a".repeat(32), Default::default())
            .unwrap()
    };
    let service = open();
    auth::bootstrap(
        &service.store,
        "admin",
        "Admin-2026-password",
        &"a".repeat(32),
        &"a".repeat(32),
    )
    .unwrap();
    let actor = auth::current_actor(
        &service.store,
        service.store.all("users").unwrap()[0]["id"]
            .as_i64()
            .unwrap(),
    )
    .unwrap();
    service
        .store
        .connection()
        .unwrap()
        .set_settings("restore-test", 1, &json!({"value":"before"}))
        .unwrap();
    let created=migration::create_package(&service,&actor,&json!({"password":"Migration-password-2026","confirmationText":"MIGRATE","adminPassword":"Admin-2026-password"})).unwrap();
    wait(&service, &actor, &created);
    let output = service
        .jobs
        .download(&actor, created["jobId"].as_str().unwrap())
        .unwrap();
    service
        .store
        .connection()
        .unwrap()
        .set_settings("restore-test", 2, &json!({"value":"after"}))
        .unwrap();
    let (ticket, _) = service
        .backup_tickets
        .issue_sensitive(&actor, ACTION_RESTORE_SERVER)
        .unwrap();
    let params = [
        (TICKET_HEADER, ticket),
        (CONFIRMATION_HEADER, "MIGRATE".into()),
        (MIGRATION_PASSWORD_HEADER, "Migration-password-2026".into()),
    ];
    let mut wrong_confirmation = params.clone();
    wrong_confirmation[1].1 = "RESTORE DATABASE".into();
    assert_eq!(
        super::super::upload(
            &service,
            &actor,
            STAGE_SERVER_MIGRATION_RESTORE,
            &wrong_confirmation,
            &Value::Null,
            &output.file_name,
            &output.content
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let staged = super::super::upload(
        &service,
        &actor,
        STAGE_SERVER_MIGRATION_RESTORE,
        &params,
        &Value::Null,
        &output.file_name,
        &output.content,
    )
    .unwrap();
    wait(&service, &actor, &staged);
    assert!(postgres::apply_pending(&paths, &maintenance, "native_owner", &app).is_err());
    service.close().unwrap();
    drop(service);
    postgres::apply_pending(&paths, &maintenance, "native_owner", &app).unwrap();
    let service = open();
    assert_eq!(
        service.store.settings("restore-test").unwrap().unwrap()["value"],
        "before"
    );
    let broken = postgres::postgres_root(&paths)
        .unwrap()
        .join("truncated.dump");
    fs::write(&broken, b"PGDMP").unwrap();
    let invalid_job = postgres::restore(&service, &actor, &[], &json!({"backupFileName":"truncated.dump","adminPassword":"Admin-2026-password","confirmationText":"RESTORE DATABASE"})).unwrap();
    let deadline = Instant::now() + Duration::from_secs(30);
    loop {
        let state = service
            .jobs
            .get(&actor, invalid_job["jobId"].as_str().unwrap())
            .unwrap();
        if state["status"] == "Failed" {
            break;
        }
        assert_ne!(state["status"], "Succeeded");
        assert!(Instant::now() < deadline);
        std::thread::sleep(Duration::from_millis(20));
    }
    assert!(!postgres::has_pending(&paths).unwrap());
    let backup = postgres::create_backup(&service, &actor).unwrap();
    wait(&service, &actor, &backup);
    let listed = postgres::list(&service, &actor).unwrap();
    let name = listed["backups"][0]["fileName"].as_str().unwrap();
    service
        .store
        .connection()
        .unwrap()
        .set_settings("restore-test", 2, &json!({"value":"second-change"}))
        .unwrap();
    let restoring = postgres::restore(&service,&actor,&[],&json!({"backupFileName":name,"adminPassword":"Admin-2026-password","confirmationText":"RESTORE DATABASE"})).unwrap();
    wait(&service, &actor, &restoring);
    assert_eq!(
        migration::status(&service, &actor).unwrap()["pendingRestore"],
        true
    );
    service.close().unwrap();
    drop(service);
    postgres::apply_pending(&paths, &maintenance, "native_owner", &app).unwrap();
    let service = open();
    assert_eq!(
        service.store.settings("restore-test").unwrap().unwrap()["value"],
        "before"
    );
    service.close().unwrap();
    drop(service);
    let target_app = std::env::var("EXPORTDOC_TEST_RECOVERY_TARGET_APP").unwrap();
    let target_admin = std::env::var("EXPORTDOC_TEST_RECOVERY_TARGET_MAINTENANCE").unwrap();
    let target = Workspace::new();
    let target_paths = target.paths();
    fs::create_dir_all(target_paths.data_root.join("Recovery/Inbox")).unwrap();
    fs::write(
        target_paths
            .data_root
            .join("Recovery/Inbox/transfer.edmpoint"),
        &output.content,
    )
    .unwrap();
    let maintenance_paths = crate::paths::RuntimePaths {
        app_root: paths.app_root.clone(),
        ..target_paths.clone()
    };
    let recovery = super::super::maintenance::RecoveryMaintenance::open(
        maintenance_paths,
        target_admin.clone(),
        target_app.clone(),
        "native_owner".into(),
    )
    .unwrap();
    assert!(
        NativeService::open_postgres(
            target_paths.clone(),
            &target_app,
            "a".repeat(32),
            Default::default()
        )
        .is_err()
    );
    let run = |operation: &str, body: Value| {
        recovery.start(operation, body).unwrap();
        let deadline = Instant::now() + Duration::from_secs(120);
        loop {
            let state = recovery.status().unwrap();
            if state["busy"] == false {
                assert_ne!(state["state"], "Failed", "{state}");
                return state;
            }
            assert!(Instant::now() < deadline, "maintenance timed out: {state}");
            std::thread::sleep(Duration::from_millis(20));
        }
    };
    let state = run(
        "UnlockRecoveryRepository",
        json!({"repositoryId":"","sourceKind":"Directory","locationId":"","portableFile":"transfer.edmpoint","password":"Migration-password-2026","oldInstanceIsolated":true}),
    );
    let point = state["points"][0]["id"].clone();
    assert!(point.is_string(), "{state}");
    let plan = run(
        "PreflightRecoveryPoint",
        json!({"pointId":point,"fileMode":"Local","targetLocation":""}),
    );
    assert_eq!(plan["state"], "Ready", "{plan}");
    let done = run("ApplyRecoveryPoint", json!({"planId":plan["planId"]}));
    assert_eq!(done["state"], "Completed", "{done}");
    recovery.close().unwrap();
    drop(recovery);
    let restored = NativeService::open_postgres(
        target_paths,
        &target_app,
        "a".repeat(32),
        Default::default(),
    )
    .unwrap();
    assert_eq!(
        restored.store.settings("restore-test").unwrap().unwrap()["value"],
        "before"
    );
    restored
        .store
        .read(|tx| {
            tx.verify_file_accounting()?;
            Ok(())
        })
        .unwrap();
    restored.close().unwrap();
}
