use export_doc_storage::{Connection, SCHEMA_VERSION};
use std::{
    fs,
    path::PathBuf,
    time::{SystemTime, UNIX_EPOCH},
};

struct Database(PathBuf);
impl Database {
    fn v6() -> Self {
        let database = Self::v5();
        // Apply the released v5 -> v6 schema change, not just a version label.
        database.raw().execute_batch("BEGIN; CREATE TABLE schema_version_upgrade (version INTEGER PRIMARY KEY CHECK(version >= 5)); INSERT INTO schema_version_upgrade SELECT version FROM schema_version; DROP TABLE schema_version; ALTER TABLE schema_version_upgrade RENAME TO schema_version; UPDATE schema_version SET version=6; COMMIT;").unwrap();
        database
    }
    fn v5() -> Self {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../../.codex-runtime/schema-migrations")
            .join(format!(
                "{}-{}",
                std::process::id(),
                SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            ));
        fs::create_dir_all(&root).unwrap();
        let file = root.join("data.db");
        let raw = rusqlite::Connection::open(&file).unwrap();
        raw.execute_batch(include_str!("../src/sqlite.sql"))
            .unwrap();
        raw.execute(
            "INSERT INTO settings VALUES ('preserved', 7, ?1)",
            [r#"{"text":"中文业务数据","amount":"123.45"}"#],
        )
        .unwrap();
        Self(file)
    }
    fn raw(&self) -> rusqlite::Connection {
        rusqlite::Connection::open(&self.0).unwrap()
    }
    fn version(&self) -> i64 {
        self.raw()
            .query_row("SELECT version FROM schema_version", [], |row| row.get(0))
            .unwrap()
    }
}
impl Drop for Database {
    fn drop(&mut self) {
        fs::remove_dir_all(self.0.parent().unwrap()).unwrap();
    }
}

#[test]
fn version_six_query_migration_rolls_back_and_restores_without_losing_data() {
    let database = Database::v6();
    let original = fs::read(&database.0).unwrap();
    let backup = database.0.with_file_name("v6-backup.db");
    fs::write(&backup, &original).unwrap();
    database
        .raw()
        .execute_batch("CREATE INDEX records_template_catalog ON records(kind);")
        .unwrap();
    assert!(Connection::sqlite(&database.0).is_err());
    assert_eq!(database.version(), 6);
    let partial: i64 = database
        .raw()
        .query_row(
            "SELECT COUNT(*) FROM sqlite_schema WHERE name='records_status_page'",
            [],
            |row| row.get(0),
        )
        .unwrap();
    assert_eq!(partial, 0, "the earlier index must roll back too");
    database
        .raw()
        .execute_batch("DROP INDEX records_template_catalog;")
        .unwrap();
    for _ in 0..2 {
        let connection = Connection::sqlite(&database.0).unwrap();
        assert_eq!(
            connection.settings("preserved").unwrap().unwrap()["amount"],
            "123.45"
        );
        assert_eq!(database.version(), SCHEMA_VERSION);
    }
    let snapshot = database.0.with_file_name("v6-restored.db");
    export_doc_storage::prepare_sqlite_restore(&backup, &snapshot).unwrap();
    assert_eq!(fs::read(&backup).unwrap(), original);
    assert_eq!(
        Connection::sqlite(&snapshot)
            .unwrap()
            .settings("preserved")
            .unwrap()
            .unwrap()["text"],
        "中文业务数据"
    );
}

#[test]
fn version_five_upgrades_once_preserving_data_and_backup_readability() {
    let database = Database::v5();
    export_doc_storage::verify_sqlite_backup(&database.0).unwrap();
    assert_eq!(
        database.version(),
        5,
        "backup validation must remain read-only"
    );
    for _ in 0..2 {
        let connection = Connection::sqlite(&database.0).unwrap();
        assert_eq!(
            connection.settings("preserved").unwrap().unwrap()["text"],
            "中文业务数据"
        );
        assert_eq!(
            connection.settings("preserved").unwrap().unwrap()["amount"],
            "123.45"
        );
        assert_eq!(database.version(), SCHEMA_VERSION);
    }
}

#[test]
fn failed_migration_rolls_back_schema_and_version_and_can_retry() {
    let database = Database::v5();
    // A conflicting destination table simulates a DDL failure inside the migration.
    database
        .raw()
        .execute_batch("CREATE TABLE schema_version_upgrade(unchanged TEXT);")
        .unwrap();
    assert!(Connection::sqlite(&database.0).is_err());
    assert_eq!(database.version(), 5);
    assert!(
        database
            .raw()
            .execute("UPDATE schema_version SET version=6", [])
            .is_err(),
        "the v5 constraint survived rollback"
    );
    database
        .raw()
        .execute_batch("DROP TABLE schema_version_upgrade;")
        .unwrap();
    let connection = Connection::sqlite(&database.0).unwrap();
    assert_eq!(
        connection.settings("preserved").unwrap().unwrap()["amount"],
        "123.45"
    );
}

#[test]
fn restore_upgrades_a_snapshot_without_modifying_the_version_five_backup() {
    let backup = Database::v5();
    let original = fs::read(&backup.0).unwrap();
    let snapshot = backup.0.with_file_name("restore.sqlite3");
    export_doc_storage::prepare_sqlite_restore(&backup.0, &snapshot).unwrap();
    let live_path = backup.0.with_file_name("live.db");
    let live = Connection::sqlite(&live_path).unwrap();
    live.restore(&snapshot).unwrap();
    assert_eq!(
        live.settings("preserved").unwrap().unwrap()["text"],
        "中文业务数据"
    );
    assert_eq!(fs::read(&backup.0).unwrap(), original);
    assert!(export_doc_storage::prepare_sqlite_restore(&backup.0, &snapshot).is_err());
    // Failed upgrade leaves the live database untouched.
    backup
        .raw()
        .execute_batch("CREATE TABLE schema_version_upgrade(unchanged TEXT);")
        .unwrap();
    assert!(
        export_doc_storage::prepare_sqlite_restore(&backup.0, &snapshot.with_extension("failed"))
            .is_err()
    );
    assert_eq!(
        live.settings("preserved").unwrap().unwrap()["text"],
        "中文业务数据"
    );
}

#[test]
fn unsupported_versions_and_existing_empty_files_are_never_reinitialized() {
    for version in [4, SCHEMA_VERSION + 1] {
        let database = Database::v5();
        database.raw().execute_batch(&format!("DROP TABLE schema_version; CREATE TABLE schema_version(version INTEGER); INSERT INTO schema_version VALUES ({version});")).unwrap();
        let original = fs::read(&database.0).unwrap();
        assert!(Connection::sqlite(&database.0).is_err());
        assert_eq!(fs::read(&database.0).unwrap(), original);
    }
    let database = Database::v5();
    fs::write(&database.0, []).unwrap();
    assert!(Connection::sqlite(&database.0).is_err());
    assert_eq!(fs::metadata(&database.0).unwrap().len(), 0);
}
