use export_doc_storage::{Connection, FileObject, FileWrite, RecordWrite};
use serde_json::json;

pub fn exercise(c: &Connection) {
    let before = c.file_statistics().unwrap();
    c.begin().unwrap();
    let mut body =
        json!({"companyScope":"FILE-QUOTA-A","versionNumber":1,"ownerUserId":1,"departmentId":"D"});
    let record = c
        .insert(&RecordWrite::unkeyed("file-accounting-test", &body))
        .unwrap();
    let object = FileObject {
        digest: "a".repeat(64),
        size: 7,
    };
    for kind in ["first", "second"] {
        c.insert_file(&FileWrite {
            record_id: record,
            kind,
            file_name: "test.txt",
            media_type: "text/plain",
            object: &object,
            created_at: "2026-11-14T00:00:00Z",
        })
        .unwrap();
    }
    assert_eq!(c.file_usage("FILE-QUOTA-A").unwrap(), 14);
    c.verify_file_accounting().unwrap();
    body["companyScope"] = json!("FILE-QUOTA-B");
    c.set_body(record, &body).unwrap();
    assert_eq!(c.file_usage("FILE-QUOTA-A").unwrap(), 0);
    assert_eq!(c.file_usage("FILE-QUOTA-B").unwrap(), 14);
    c.delete_files(record, Some("first")).unwrap();
    assert_eq!(c.file_usage("FILE-QUOTA-B").unwrap(), 7);
    c.verify_file_accounting().unwrap();
    c.delete_files(record, None).unwrap();
    c.remove_unused_file(&object.digest).unwrap();
    assert_eq!(c.file_statistics().unwrap(), before);
    c.verify_file_accounting().unwrap();
    c.rollback().unwrap();
    assert_eq!(c.file_usage("FILE-QUOTA-B").unwrap(), 0);
    c.verify_file_accounting().unwrap();
}
