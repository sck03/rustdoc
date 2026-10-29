//! Fresh baseline and rejection of experimental schemas; no legacy upgrade path.
use export_doc_storage::{Connection, SCHEMA_VERSION};
use std::{
    fs,
    path::PathBuf,
    time::{SystemTime, UNIX_EPOCH},
};
struct Database(PathBuf);
impl Database {
    fn fresh() -> Self {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../../.codex-runtime/schema-baseline")
            .join(format!(
                "{}-{}",
                std::process::id(),
                SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            ));
        fs::create_dir_all(&root).unwrap();
        let result = Self(root.join("data.db"));
        let db = Connection::sqlite(&result.0).unwrap();
        db.set_settings(
            "preserved",
            1,
            &serde_json::json!({"text":"中文数据","amount":"123.45"}),
        )
        .unwrap();
        db.checkpoint().unwrap();
        drop(db);
        result
    }
    fn raw(&self) -> rusqlite::Connection {
        rusqlite::Connection::open(&self.0).unwrap()
    }
}
impl Drop for Database {
    fn drop(&mut self) {
        fs::remove_dir_all(self.0.parent().unwrap()).unwrap();
    }
}
#[test]
fn fresh_baseline_reopens_and_backups_round_trip() {
    let database = Database::fresh();
    let backup = database.0.with_file_name("backup.db");
    {
        let db = Connection::sqlite(&database.0).unwrap();
        db.backup(&backup).unwrap();
        db.verify_backup(&backup).unwrap();
    }
    let snapshot = database.0.with_file_name("restore.db");
    export_doc_storage::prepare_sqlite_restore(&backup, &snapshot).unwrap();
    for file in [&database.0, &snapshot] {
        let db = Connection::sqlite(file).unwrap();
        assert_eq!(
            db.settings("preserved").unwrap().unwrap()["amount"],
            "123.45"
        );
    }
    assert_eq!(
        database
            .raw()
            .query_row("SELECT version FROM schema_version", [], |r| r
                .get::<_, i64>(0))
            .unwrap(),
        SCHEMA_VERSION
    );
}
#[test]
fn old_future_empty_and_incomplete_databases_are_never_reinitialized() {
    for version in [4, 5, 6, 7, 8, SCHEMA_VERSION + 1] {
        let database = Database::fresh();
        database.raw().execute_batch(&format!("DROP TABLE schema_version; CREATE TABLE schema_version(version INTEGER); INSERT INTO schema_version VALUES({version});")).unwrap();
        let original = fs::read(&database.0).unwrap();
        assert!(Connection::sqlite(&database.0).is_err());
        assert!(export_doc_storage::verify_sqlite_backup(&database.0).is_err());
        assert_eq!(fs::read(&database.0).unwrap(), original);
    }
    let database = Database::fresh();
    database
        .raw()
        .execute_batch("DROP TABLE workflow_tasks;")
        .unwrap();
    assert!(Connection::sqlite(&database.0).is_err());
    fs::write(&database.0, []).unwrap();
    assert!(Connection::sqlite(&database.0).is_err());
    assert_eq!(fs::metadata(&database.0).unwrap().len(), 0);
}
