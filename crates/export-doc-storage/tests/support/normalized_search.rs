use export_doc_storage::{Connection, DataScope, GenericQuery, RecordWrite};
use serde_json::json;

pub fn exercise(db: &Connection) {
    db.begin().unwrap();
    let mut body = json!({"versionNumber":1,"ownerUserId":1,"companyScope":"SEARCH","departmentId":"D","customerNameEN":"CAFE\u{301}","name":"Original","b":2,"a":1});
    let id = db.insert(&RecordWrite::unkeyed("invoices", &body)).unwrap();
    body["id"] = json!(id);
    db.set_body(id, &body).unwrap();
    let query = |keyword: &str| {
        db.query_generic(&GenericQuery {
            kind: "invoices",
            scope: DataScope {
                company_wide: true,
                company: "SEARCH",
                ..Default::default()
            },
            keyword,
            fields: &[("id", "id")],
            limit: 20,
            ..Default::default()
        })
        .unwrap()
        .0
    };
    assert_eq!(query("café"), 1, "cached words keep NFC/case normalization");
    assert_eq!(
        query("\"a\":1,\"b\":2"),
        1,
        "cached structured search uses canonical key order"
    );
    assert_eq!(
        query("\"name\":\"Original\""),
        1,
        "structured searches keep the compact JSON contract"
    );
    body["name"] = json!("Changed");
    db.set_body(id, &body).unwrap();
    assert_eq!(query("Original"), 0, "metadata follows every body write");
    assert_eq!(query("Changed"), 1);
    let mut large = body.clone();
    large["customerNameEN"] = json!("Large-search-marker");
    large["padding"] = json!("x".repeat(65537));
    db.insert(&RecordWrite::unkeyed("invoices", &large))
        .unwrap();
    assert_eq!(
        query("Large-search-marker"),
        1,
        "over-limit documents remain fully searchable"
    );
    db.rollback().unwrap();
    assert_eq!(
        query("Changed"),
        0,
        "rolled-back metadata cannot outlive the record"
    );
    field_selection(db);
}

fn field_selection(db: &Connection) {
    db.begin().unwrap();
    let mut ids = vec![];
    for (company, status, invoice, extra) in [
        ("SEARCH", "Pending", "CAFÉ%_", ""),
        ("SEARCH", "Pending", "cafe\u{301}%_", ""),
        ("OTHER", "Pending", "café%_", ""),
        ("SEARCH", "Done", "café%_", ""),
        ("SEARCH", "Pending", "unrelated", "café%_"),
    ] {
        let mut body = json!({"versionNumber":1,"ownerUserId":1,"companyScope":company,"departmentId":"D","businessType":"AgentConsignment","status":status,"invoiceNo":invoice,"_manifest":{"secret":extra,"padding":"x".repeat(10000)}});
        let id = db
            .insert(&RecordWrite::unkeyed("sw-batches", &body))
            .unwrap();
        body["id"] = json!(id);
        db.set_body(id, &body).unwrap();
        ids.push(id);
    }
    db.commit().unwrap();
    db.begin_read().unwrap();
    let query = |offset, count| {
        db.query_record_fields(
            &GenericQuery {
                kind: "sw-batches",
                scope: DataScope {
                    company_wide: true,
                    company: "SEARCH",
                    ..Default::default()
                },
                keyword: "café%_",
                filters: &[
                    ("status", "Pending".into()),
                    ("businessType", "AgentConsignment".into()),
                ],
                fields: &[("id", "id"), ("invoiceNo", "invoiceNo")],
                offset,
                limit: 1,
                ..Default::default()
            },
            &["batchReference", "invoiceNo", "contractNo", "referenceNo"],
            count,
        )
        .unwrap()
    };
    let (count, first) = query(0, true);
    assert_eq!(
        count, 2,
        "scope, filters, NFC and literal wildcard search agree"
    );
    assert_eq!(first[0]["id"], ids[1]);
    assert_eq!(
        first[0].as_object().unwrap().len(),
        2,
        "manifest and secret are never materialized"
    );
    assert_eq!(query(1, false).1[0]["id"], ids[0]);
    assert_eq!(query(i64::MAX, true).0, 2);
    assert!(query(i64::MAX, true).1.is_empty());
    db.commit().unwrap();
}
