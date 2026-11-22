use export_doc_storage::{Connection, RecordWrite};
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
            .insert(&RecordWrite::unkeyed("import-previews", &body))
            .unwrap();
        body["id"] = json!(id);
        c.set_body(id, &body).unwrap();
        if !consumed {
            c.insert_import_preview_rows(id, r#"[{"x":1}]"#).unwrap();
        }
        ids.push(id);
    }
    let usage = c.import_preview_usage(31).unwrap();
    assert_eq!(
        (usage.count, usage.user_count, usage.bytes, usage.user_bytes),
        (2, 1, 14, 7)
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
    c.delete_import_preview_rows(ids[0]).unwrap();
    assert_eq!(c.import_preview_usage(31).unwrap().bytes, 7);
    let rows: Vec<_> = (0..501).map(|index| json!({"position":index,"changeType":if index%2==0 {"Add"} else {"Update"},"item":{"notes":"中文 é 备注","rate":"1234567890.123456789012"}})).collect();
    c.insert_import_preview_rows(ids[0], &serde_json::to_string(&rows).unwrap())
        .unwrap();
    assert_eq!(c.import_preview_row_count(ids[0], "").unwrap(), 501);
    assert_eq!(c.import_preview_row_count(ids[0], "Update").unwrap(), 250);
    let page = c.import_preview_rows(ids[0], "Update", 200, 50).unwrap();
    assert_eq!(page.len(), 50);
    assert_eq!(page.first().unwrap(), &rows[401]);
    assert_eq!(page.last().unwrap(), &rows[499]);
    assert_eq!(
        c.import_preview_rows(ids[0], "", 500, 200).unwrap(),
        rows[500..]
    );
    assert!(c.import_preview_rows(ids[0], "", 0, 201).is_err());
    let mut body = c.get("import-previews", ids[0]).unwrap().unwrap();
    body["expiresAt"] = json!("corrupted");
    c.set_body(ids[0], &body).unwrap();
    assert!(c.expired_import_previews("2026-10-25T00:00:00Z").is_err());
    c.rollback().unwrap();
}
