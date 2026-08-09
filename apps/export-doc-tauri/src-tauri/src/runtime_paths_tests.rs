use super::*;

#[test]
fn reads_runtime_path_arguments_from_split_values() {
    let args = vec![
        OsString::from("--data-root"),
        OsString::from("D:\\ExportDocManagerData"),
        OsString::from("--app-root"),
        OsString::from("D:\\ExportDocManager"),
    ];

    assert_eq!(
        runtime_arg_value_from(args.clone(), "--data-root"),
        Some(PathBuf::from("D:\\ExportDocManagerData"))
    );
    assert_eq!(
        runtime_arg_value_from(args, "--app-root"),
        Some(PathBuf::from("D:\\ExportDocManager"))
    );
}

#[test]
fn reads_runtime_path_arguments_from_equals_values() {
    let args = vec![
        OsString::from("--data-root=D:\\ExportDocManagerData"),
        OsString::from("--app-root=D:\\ExportDocManager"),
    ];

    assert_eq!(
        runtime_arg_value_from(args.clone(), "--data-root"),
        Some(PathBuf::from("D:\\ExportDocManagerData"))
    );
    assert_eq!(
        runtime_arg_value_from(args, "--app-root"),
        Some(PathBuf::from("D:\\ExportDocManager"))
    );
}

#[test]
fn reads_persisted_absolute_runtime_data_root() {
    let app_root = fresh_test_dir("absolute-runtime-data-root");
    let config_root = app_root.join("platform-config");
    fs::create_dir_all(&config_root).unwrap();
    let data_root = absolute_test_data_root("configured-business-data");
    let config = RuntimePathsConfig {
        schema_version: RUNTIME_PATHS_CONFIG_SCHEMA_VERSION,
        data_root: data_root.to_string_lossy().into_owned(),
        source: Some("test".to_owned()),
    };
    fs::write(
        runtime_paths_config_path(&config_root),
        serde_json::to_string(&config).unwrap(),
    )
    .unwrap();

    assert_eq!(
        read_persisted_data_root(&config_root, &app_root).unwrap(),
        Some(data_root)
    );
    assert!(!runtime_paths_config_path(&app_root).exists());
}

#[test]
fn rejects_unsupported_runtime_paths_config_schema() {
    let app_root = fresh_test_dir("unsupported-runtime-paths-schema");
    let config_root = app_root.join("platform-config");
    fs::create_dir_all(&config_root).unwrap();
    fs::write(
        runtime_paths_config_path(&config_root),
        r#"{"schemaVersion":2,"dataRoot":"BusinessData"}"#,
    )
    .unwrap();

    let error = read_persisted_data_root(&config_root, &app_root)
        .unwrap_err()
        .to_string();

    assert!(error.contains("Unsupported runtime paths config schema version 2"));
}

#[test]
fn unsupported_schema_does_not_fall_back_to_older_backup() {
    let app_root = fresh_test_dir("unsupported-runtime-paths-schema-with-backup");
    let config_root = app_root.join("platform-config");
    fs::create_dir_all(&config_root).unwrap();
    fs::write(
        runtime_paths_config_path(&config_root),
        r#"{"schemaVersion":2,"dataRoot":"NewBusinessData"}"#,
    )
    .unwrap();
    fs::write(
        runtime_paths_config_backup_path(&config_root),
        r#"{"schemaVersion":1,"dataRoot":"OldBusinessData"}"#,
    )
    .unwrap();

    let error = read_persisted_data_root(&config_root, &app_root)
        .unwrap_err()
        .to_string();

    assert!(error.contains("Unsupported runtime paths config schema version 2"));
}

#[test]
fn resolves_persisted_relative_runtime_data_root_against_app_root() {
    let app_root = fresh_test_dir("relative-runtime-data-root");
    let config_root = app_root.join("platform-config");
    fs::create_dir_all(&config_root).unwrap();
    fs::write(
        runtime_paths_config_path(&config_root),
        r#"{"schemaVersion":1,"dataRoot":"BusinessData"}"#,
    )
    .unwrap();

    assert_eq!(
        read_persisted_data_root(&config_root, &app_root).unwrap(),
        Some(app_root.join("BusinessData"))
    );
}

#[test]
fn persists_runtime_data_root_as_valid_config() {
    let app_root = fresh_test_dir("persist-runtime-data-root");
    let config_root = app_root.join("platform-config");
    let data_root = app_root.join("BusinessData");

    persist_runtime_data_root(&config_root, &data_root).unwrap();

    assert_eq!(
        read_persisted_data_root(&config_root, &app_root).unwrap(),
        Some(data_root)
    );
    assert!(runtime_paths_config_path(&config_root).exists());
    assert!(!runtime_paths_config_path(&app_root).exists());
}

#[test]
fn persist_keeps_previous_runtime_paths_config_as_backup() {
    let app_root = fresh_test_dir("backup-runtime-data-root");
    let config_root = app_root.join("platform-config");
    let first_data_root = app_root.join("FirstBusinessData");
    let second_data_root = app_root.join("SecondBusinessData");

    persist_runtime_data_root(&config_root, &first_data_root).unwrap();
    persist_runtime_data_root(&config_root, &second_data_root).unwrap();

    assert_eq!(
        read_persisted_data_root(&config_root, &app_root).unwrap(),
        Some(second_data_root)
    );
    assert_eq!(
        read_data_root_from_config(&runtime_paths_config_backup_path(&config_root), &app_root)
            .unwrap(),
        first_data_root
    );
}

#[test]
fn falls_back_to_backup_when_runtime_paths_config_is_corrupted() {
    let app_root = fresh_test_dir("recover-runtime-data-root");
    let config_root = app_root.join("platform-config");
    let first_data_root = app_root.join("FirstBusinessData");
    let second_data_root = app_root.join("SecondBusinessData");

    persist_runtime_data_root(&config_root, &first_data_root).unwrap();
    persist_runtime_data_root(&config_root, &second_data_root).unwrap();
    fs::write(runtime_paths_config_path(&config_root), "{broken-json").unwrap();

    assert_eq!(
        read_persisted_data_root(&config_root, &app_root).unwrap(),
        Some(first_data_root)
    );
}

#[test]
fn detects_valid_portable_runtime_marker() {
    let app_root = fresh_test_dir("portable-runtime-marker");
    fs::create_dir_all(&app_root).unwrap();
    fs::write(
        app_root.join(PORTABLE_RUNTIME_MARKER_FILE_NAME),
        r#"{"schemaVersion":1,"mode":"portable"}"#,
    )
    .unwrap();
    fs::write(app_root.join(RUNTIME_LAYOUT_MANIFEST_FILE_NAME), "{}").unwrap();

    assert!(is_portable_runtime(&app_root).unwrap());
}

#[test]
fn accepts_external_portable_root_for_appimage_and_app_bundle_layouts() {
    let root = fresh_test_dir("external-portable-runtime-marker");
    let app_root = root.join("packaged-resources");
    let portable_root = root.join("ExportDocManager-portable");
    fs::create_dir_all(&app_root).unwrap();
    fs::create_dir_all(&portable_root).unwrap();
    fs::write(app_root.join(RUNTIME_LAYOUT_MANIFEST_FILE_NAME), "{}").unwrap();
    fs::write(
        portable_root.join(PORTABLE_RUNTIME_MARKER_FILE_NAME),
        r#"{"schemaVersion":1,"mode":"portable"}"#,
    )
    .unwrap();

    validate_portable_runtime_marker(&portable_root, &app_root).unwrap();
    let data_root = resolve_data_root(&portable_root, &portable_root, None, true).unwrap();

    assert_eq!(data_root, portable_root.join("App_Data"));
    assert!(data_root.join("Database").is_dir());
    assert!(!app_root.join("App_Data").exists());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn accepts_writable_data_root_without_requiring_a_secondary_volume() {
    let data_root = env::temp_dir()
        .join("ExportDocManagerRuntimePathTests")
        .join(format!("single-volume-{}", std::process::id()));
    let _ = fs::remove_dir_all(&data_root);

    ensure_runtime_data_root_is_usable(&data_root).unwrap();

    assert!(data_root.join("Database").is_dir());
    assert!(data_root.join("Logs").is_dir());
    assert!(data_root.join("WebView").is_dir());
    fs::remove_dir_all(data_root).unwrap();
}

#[test]
fn schedules_and_applies_data_root_migration_before_runtime_start() {
    let root = fresh_test_dir("data-root-migration");
    let app_root = root.join("app");
    let config_root = root.join("platform-config");
    let source_root = root.join("source-data");
    let target_root = root.join("target-data");
    fs::create_dir_all(&app_root).unwrap();
    ensure_runtime_data_directories(&source_root).unwrap();
    fs::write(source_root.join("Database").join("data.db"), b"database").unwrap();
    fs::create_dir_all(&target_root).unwrap();
    persist_runtime_data_root(&config_root, &source_root).unwrap();
    let paths = RuntimePaths {
        app_root: app_root.clone(),
        data_root: source_root.clone(),
        log_root: source_root.join("Logs"),
        sidecar_path: app_root.join("sidecar").join(sidecar_file_name()),
        runtime_config_root: config_root.clone(),
        portable: false,
    };

    let scheduled = schedule_data_root_migration(&paths, &target_root).unwrap();
    assert!(scheduled.restart_required);
    assert!(pending_data_root_migration_path(&config_root).exists());

    apply_pending_data_root_migration(&config_root).unwrap();

    assert!(!source_root.exists());
    assert_eq!(
        fs::read(target_root.join("Database").join("data.db")).unwrap(),
        b"database"
    );
    assert_eq!(
        read_persisted_data_root(&config_root, &app_root).unwrap(),
        Some(fs::canonicalize(&target_root).unwrap())
    );
    assert!(!pending_data_root_migration_path(&config_root).exists());
}

#[test]
fn rejects_nested_data_root_migration_target() {
    let root = fresh_test_dir("nested-data-root-migration");
    let source_root = root.join("source-data");
    ensure_runtime_data_directories(&source_root).unwrap();
    let target_root = source_root.join("nested-target");
    fs::create_dir_all(&target_root).unwrap();

    let error = validate_distinct_migration_roots(
        &fs::canonicalize(&source_root).unwrap(),
        &fs::canonicalize(&target_root).unwrap(),
    )
    .unwrap_err()
    .to_string();

    assert!(error.contains("不能互相包含"));
}

fn fresh_test_dir(name: &str) -> PathBuf {
    let root = env::current_dir()
        .unwrap()
        .join("target")
        .join("runtime-path-tests")
        .join(format!("{name}-{}", std::process::id()));
    let _ = fs::remove_dir_all(&root);
    root
}

fn absolute_test_data_root(name: &str) -> PathBuf {
    env::current_dir()
        .unwrap()
        .join("target")
        .join("runtime-path-tests")
        .join("external-data")
        .join(name)
}
