//! Fresh baseline and rejection of experimental schemas; no legacy upgrade path.
use export_doc_storage::{Connection, SCHEMA_VERSION};
#[path = "support/workspace.rs"]
mod test_workspace;
use std::{fs, path::PathBuf};
struct Database(PathBuf);
#[test]
fn handling_directory_pages_beyond_one_hundred_and_preserves_scope() {
    let database = Database::fresh();
    let db = Connection::sqlite(&database.0).unwrap();
    for i in 0..105 {
        db.save_handling_service("test", &serde_json::json!({"key":format!("service-{i:03}"),"name":format!("办理事项{i}"),"category":"Seal","handlerUserIds":[],"isActive":true})).unwrap();
    }
    let page = db
        .search_handling_services("test", "Seal", "office.general", "", "active", "", 100)
        .unwrap();
    assert_eq!(page.len(), 100);
    let tail = db
        .search_handling_services(
            "test",
            "Seal",
            "office.general",
            "",
            "active",
            page.last().unwrap()["key"].as_str().unwrap(),
            100,
        )
        .unwrap();
    assert_eq!(tail.len(), 5);
    assert!(
        db.get_handling_service("other", "service-000")
            .unwrap()
            .is_none()
    );
    assert_eq!(
        db.get_handling_service("test", "service-104")
            .unwrap()
            .unwrap()["revision"],
        1
    );
}
impl Database {
    fn fresh() -> Self {
        let root = test_workspace::create("schema-baseline");
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
        test_workspace::remove(self.0.parent().unwrap()).unwrap();
    }
}
#[test]
fn fresh_baseline_reopens_and_backups_round_trip() {
    let database = Database::fresh();
    let backup = database.0.with_file_name("backup.db");
    {
        let db = Connection::sqlite(&database.0).unwrap();
        db.backup(&backup, &mut |_| Ok(())).unwrap();
        export_doc_storage::verify_sqlite_backup(&backup).unwrap();
    }
    for file in [&database.0, &backup] {
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
    for version in [SCHEMA_VERSION - 1, SCHEMA_VERSION + 1] {
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

#[test]
fn snapshot_checks_capacity_before_copying_and_preserves_source_on_failure() {
    let database = Database::fresh();
    let target = database.0.with_file_name("no-space.db");
    let db = Connection::sqlite(&database.0).unwrap();
    let mut checked = false;
    let result = db.backup(&target, &mut |bytes| {
        assert!(bytes > 0);
        checked = true;
        Err(export_doc_storage::Error::unavailable(
            "injected disk budget exhaustion",
        ))
    });
    assert!(checked && result.is_err());
    assert_eq!(
        db.settings("preserved").unwrap().unwrap()["amount"],
        "123.45"
    );
}
