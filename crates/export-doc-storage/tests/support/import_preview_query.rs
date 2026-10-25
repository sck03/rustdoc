use export_doc_storage::{BlobWrite, Connection, RecordWrite};
use serde_json::json;

pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let mut ids = vec![];
    for (owner, expires, consumed) in [
        (31, "2026-10-25T08:00:00+08:00", false),
        (32, "2026-10-25T00:00:00.000000001Z", false),
        (31, "2026-10-25T00:00:00Z", true),
    ] {
        let mut body = json!({"ownerUserId":owner,"companyScope":"PREVIEW","departmentId":"D","versionNumber":1,"expiresAt":expires,"consumed":consumed});
        let id = c
            .insert(&RecordWrite {
                kind: "import-previews",
                identity: None,
                body: &body,
            })
            .unwrap();
        body["id"] = json!(id);
        c.set_body(id, &body).unwrap();
        if !consumed {
            c.insert_blob(&BlobWrite {
                kind: "import-previews",
                record_id: id,
                file_name: "preview.json",
                media_type: "application/json",
                digest: "test",
                content: b"[1]",
                created_at: "2026-10-25T00:00:00Z",
            })
            .unwrap();
        }
        ids.push(id);
    }
    let usage = c.import_preview_usage(31).unwrap();
    assert_eq!(
        (usage.count, usage.user_count, usage.bytes, usage.user_bytes),
        (2, 1, 6, 3)
    );
    let expired = c.expired_import_previews("2026-10-25T00:00:00Z").unwrap();
    assert_eq!(
        expired
            .iter()
            .map(|r| r["id"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        vec![ids[0], ids[2]]
    );
    assert!(expired.iter().all(|r| r.get("companyScope").is_none()));
    c.delete_blob(ids[0], "import-previews").unwrap();
    assert_eq!(c.import_preview_usage(31).unwrap().bytes, 3);
    let mut body = c.get("import-previews", ids[0]).unwrap().unwrap();
    body["expiresAt"] = json!("corrupted");
    c.set_body(ids[0], &body).unwrap();
    assert!(c.expired_import_previews("2026-10-25T00:00:00Z").is_err());
    c.rollback().unwrap();
}
