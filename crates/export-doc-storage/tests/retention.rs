use export_doc_storage::{Connection, FileObject, FileWrite, RecordWrite};
use serde_json::json;
#[path = "support/workspace.rs"]
mod workspace;
fn exercise(db: &Connection) {
    db.begin().unwrap();
    let record = db
        .insert(&RecordWrite::unkeyed(
            "oa-submission",
            &json!({"companyScope":"TEST","versionNumber":1,"ownerUserId":1,"departmentId":"D"}),
        ))
        .unwrap();
    let object = FileObject {
        archive_identity: String::new(),
        digest: "b".repeat(64),
        size: 7,
    };
    for kind in ["original", "signed"] {
        db.insert_file(&FileWrite {
            kind,
            record_id: record,
            file_name: "formal.pdf",
            media_type: "application/pdf",
            object: &object,
            created_at: "2026-11-27T00:00:00Z",
        })
        .unwrap();
    }
    db.delete_files(record, Some("original")).unwrap();
    db.delete_files(record, None).unwrap();
    assert!(db.delete("oa-submission", record, 1).unwrap());
    let rows = db.retained_files("", 100).unwrap();
    let rows = rows.as_array().unwrap();
    assert_eq!(rows.len(), 2);
    assert_eq!(rows[0]["basisDate"], serde_json::Value::Null);
    assert!(db.file_is_referenced(&object.digest).unwrap());
    assert!(db.file_objects("", 100, true).unwrap().is_empty());
    assert_eq!(db.file_statistics().unwrap()["protectedBytes"], 7);
    db.archive_file(&"c".repeat(32), &object.digest).unwrap();
    assert_eq!(
        db.file_objects("", 100, false).unwrap()[0].archive_identity,
        "c".repeat(32)
    );
    db.archive_file(&"d".repeat(32), &object.digest).unwrap();
    assert_eq!(
        db.file_objects("", 100, false).unwrap()[0].archive_identity,
        "d".repeat(32)
    );
    db.verify_file_accounting().unwrap();
    assert!(db.save_retention_policy("absent", 1, 4).is_err());
    db.save_retention_policy("signed-documents", 365, 0)
        .unwrap();
    assert!(db.save_retention_policy("signed-documents", 1, 0).is_err());
    for row in rows {
        db.classify_retained_file(
            row["id"].as_str().unwrap(),
            "signed-documents",
            "2025-01-01",
            1,
        )
        .unwrap();
    }
    db.hold_retained_file(&object.digest, true, "audit hold", 0)
        .unwrap();
    assert!(
        db.remove_retained_file(rows[0]["id"].as_str().unwrap(), 2)
            .is_err()
    );
    db.hold_retained_file(&object.digest, false, "audit finished", 1)
        .unwrap();
    for row in rows {
        db.remove_retained_file(row["id"].as_str().unwrap(), 2)
            .unwrap();
    }
    assert!(!db.file_is_referenced(&object.digest).unwrap());
    assert_eq!(db.file_statistics().unwrap()["protectedBytes"], 0);
    db.archive_file(&"c".repeat(32), &object.digest).unwrap();
    assert_eq!(
        db.file_objects("", 100, false).unwrap()[0].archive_identity,
        "c".repeat(32)
    );
    db.archive_file(&"d".repeat(32), &object.digest).unwrap();
    assert_eq!(
        db.file_objects("", 100, false).unwrap()[0].archive_identity,
        "d".repeat(32)
    );
    db.verify_file_accounting().unwrap();
    db.rollback().unwrap();
}
#[test]
fn sqlite_retention_preserves_formal_deletion_and_accounting() {
    let root = workspace::create("retention");
    let db = Connection::sqlite(&root.join("data.db")).unwrap();
    exercise(&db);
    drop(db);
    workspace::remove(&root).unwrap();
}
#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires isolated PostgreSQL 18"]
fn postgres_retention_preserves_formal_deletion_and_accounting() {
    let maintenance = std::env::var("EXPORTDOC_TEST_POSTGRES_MAINTENANCE").unwrap();
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let db = Connection::postgres(&std::env::var("EXPORTDOC_TEST_POSTGRES_APP").unwrap()).unwrap();
    exercise(&db);
}
