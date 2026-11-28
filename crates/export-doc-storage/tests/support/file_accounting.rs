use export_doc_storage::{Connection, FileObject, FileWrite, RecordWrite};
use serde_json::json;

pub fn exercise(c: &Connection) {
    let before = c.file_statistics().unwrap();
    c.begin().unwrap();
    let mut body =
        json!({"companyScope":"FILE-QUOTA-A","versionNumber":1,"ownerUserId":1,"departmentId":"D"});
    let record = c
        .insert(&RecordWrite::unkeyed("report-images", &body))
        .unwrap();
    let object = FileObject {
        archive_identity: String::new(),
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
    c.locate_file("online", &object.digest).unwrap();
    c.archive_file("archive", &object.digest).unwrap();
    let states = c
        .file_object_states(&[object.digest.clone(), "f".repeat(64)])
        .unwrap();
    assert_eq!(states.len(), 1);
    assert_eq!(states[0].references, 2);
    assert_eq!(states[0].object.archive_identity, "archive");
    c.publish_online_file("online", &object.digest).unwrap();
    assert!(
        c.file_info(record, "first")
            .unwrap()
            .unwrap()
            .object
            .archive_identity
            .is_empty()
    );
    assert!(c.file_object_states(&[]).unwrap().is_empty());
    assert!(
        c.file_object_states(&vec![object.digest.clone(); 1001])
            .is_err()
    );
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
