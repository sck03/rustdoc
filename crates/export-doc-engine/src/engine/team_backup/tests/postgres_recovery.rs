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
    let (ticket, _) = issue_sensitive_ticket(&actor, ACTION_RESTORE_SERVER).unwrap();
    let params = [
        (TICKET_HEADER, ticket),
        (CONFIRMATION_HEADER, "RESTORE DATABASE".into()),
        (MIGRATION_PASSWORD_HEADER, "Migration-password-2026".into()),
    ];
    migration::stage_restore_upload(
        &service,
        &actor,
        &params,
        &output.file_name,
        &output.content,
    )
    .unwrap();
    assert!(postgres::apply_pending(&paths, &maintenance, "native_owner", &app).is_err());
    service.close().unwrap();
    drop(service);
    postgres::apply_pending(&paths, &maintenance, "native_owner", &app).unwrap();
    let service = open();
    assert_eq!(
        service.store.settings("restore-test").unwrap().unwrap()["value"],
        "before"
    );
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
    postgres::restore(&service,&actor,&[],&json!({"backupFileName":name,"adminPassword":"Admin-2026-password","confirmationText":"RESTORE DATABASE"})).unwrap();
    service.close().unwrap();
    drop(service);
    postgres::apply_pending(&paths, &maintenance, "native_owner", &app).unwrap();
    let service = open();
    assert_eq!(
        service.store.settings("restore-test").unwrap().unwrap()["value"],
        "before"
    );
    service.close().unwrap();
}
