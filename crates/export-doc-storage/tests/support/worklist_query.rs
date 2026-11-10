use export_doc_storage::{Connection, DataScope, RecordWrite, ScopedQuery};
use serde_json::{Value, json};
fn insert(db: &Connection, kind: &str, mut value: Value, owner: i64) -> i64 {
    value["versionNumber"] = json!(1);
    value["companyScope"] = json!("WORKLIST");
    value["departmentId"] = json!("D");
    value["ownerUserId"] = json!(owner);
    let id = db.insert(&RecordWrite::unkeyed(kind, &value)).unwrap();
    value["id"] = json!(id);
    db.set_body(id, &value).unwrap();
    id
}
pub fn exercise(db: &Connection) {
    db.begin().unwrap();
    assert_eq!(db.settings_revision("version-test").unwrap(), 0);
    assert!(
        db.compare_set_settings("version-test", 0, &json!({"value":1}))
            .unwrap()
    );
    assert!(
        !db.compare_set_settings("version-test", 0, &json!({"value":2}))
            .unwrap()
    );
    assert!(
        db.compare_set_settings("version-test", 1, &json!({"value":3}))
            .unwrap()
    );
    assert!(
        !db.compare_set_settings("version-test", 1, &json!({"value":4}))
            .unwrap()
    );
    assert_eq!(db.settings_revision("version-test").unwrap(), 2);
    assert_eq!(db.settings("version-test").unwrap().unwrap()["value"], 3);
    let scopes = [DataScope {
        all: true,
        ..Default::default()
    }];
    let mut query = ScopedQuery {
        kind: "invoices",
        scopes: &scopes,
        company: Some("WORKLIST"),
        fields: &[("id", "id")],
        limit: 37,
    };
    let mut ids = vec![];
    for index in 0..405 {
        let active = index % 2 == 0;
        let id = insert(
            db,
            "invoices",
            json!({"status":if active{"Draft"}else{"Confirmed"},"private":"not projected"}),
            1,
        );
        if active {
            ids.push(id);
        }
    }
    insert(db, "invoices", json!({"status":"Draft"}), 2);
    let mut cursor = None;
    let mut found = vec![];
    loop {
        let (next, rows) = db
            .scan_worklist(&query, 1, &scopes[0], &Default::default(), cursor)
            .unwrap();
        if rows.is_empty() {
            break;
        }
        assert!(rows.iter().all(|row| row.as_object().unwrap().len() == 1));
        found.extend(rows.iter().map(|row| row["id"].as_i64().unwrap()));
        cursor = next;
    }
    ids.reverse();
    assert_eq!(found, ids);
    let scan = export_doc_storage::WorklistScan {
        source: "invoice-review",
        ascending: true,
    };
    let after = ids[100];
    let (_, page) = db
        .scan_worklist(&query, 1, &scopes[0], &scan, Some(after))
        .unwrap();
    assert_eq!(
        page.iter()
            .map(|row| row["id"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        ids.iter()
            .rev()
            .copied()
            .filter(|id| *id > after)
            .take(query.limit as usize)
            .collect::<Vec<_>>()
    );
    let pending = insert(db, "supply-requests", json!({"status":"Pending"}), 1);
    insert(db, "supply-requests", json!({"status":"Issued"}), 1);
    query.kind = "supply-requests";
    let (_, selected) = db
        .scan_worklist(
            &query,
            1,
            &scopes[0],
            &export_doc_storage::WorklistScan {
                source: "supply-approval",
                ascending: true,
            },
            None,
        )
        .unwrap();
    assert_eq!(selected.len(), 1);
    assert_eq!(selected[0]["id"], pending);
    let visible = insert(db, "crm-customers", json!({"name":"可见客户"}), 1);
    let hidden = insert(db, "crm-customers", json!({"name":"不可见客户"}), 2);
    let follow = insert(
        db,
        "crm-follow-ups",
        json!({"crmCustomerId":visible,"isCompleted":false}),
        1,
    );
    insert(
        db,
        "crm-follow-ups",
        json!({"crmCustomerId":hidden,"isCompleted":false}),
        1,
    );
    insert(
        db,
        "crm-follow-ups",
        json!({"crmCustomerId":visible,"isCompleted":true}),
        1,
    );
    query.kind = "crm-follow-ups";
    let customer_scope = DataScope {
        own: true,
        company: "WORKLIST",
        user_id: 1,
        ..Default::default()
    };
    let (_, rows) = db
        .scan_worklist(&query, 1, &customer_scope, &Default::default(), None)
        .unwrap();
    assert_eq!(rows.len(), 1);
    assert_eq!(rows[0]["id"], follow);
    assert_eq!(rows[0]["customerName"], "可见客户");
    let mut keys = vec![];
    let mut batch_ids = vec![];
    for index in 0..205 {
        let key = format!("单位:'批量:{index}");
        let mut value = json!({"versionNumber":1,"key":key,"ownerUserId":1});
        let id = db
            .insert(&RecordWrite {
                kind: "batch-lookup",
                identity: Some(&key),
                body: &value,
            })
            .unwrap();
        value["id"] = json!(id);
        db.set_body(id, &value).unwrap();
        batch_ids.push(id);
        keys.push(key);
    }
    keys.extend(keys.clone());
    keys.push("missing".into());
    let rows = db
        .records_by_identities("batch-lookup", &keys, &[("id", "id")])
        .unwrap();
    assert_eq!(rows.len(), 205);
    assert!(rows.iter().all(|row| row.as_object().unwrap().len() == 1));
    batch_ids.reverse();
    let rows = db
        .records_by_ids("batch-lookup", &batch_ids, &[("id", "id")])
        .unwrap();
    assert_eq!(
        rows.iter()
            .map(|row| row["id"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        batch_ids
    );
    assert!(
        db.records_by_identities("other-kind", &keys, &[])
            .unwrap()
            .is_empty()
    );
    db.rollback().unwrap();
}
