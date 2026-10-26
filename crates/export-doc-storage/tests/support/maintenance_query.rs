use export_doc_storage::{
    AuditQuery, AuditWrite, Connection, DataScope, ErrorKind, GenericQuery, RecordOrder,
    RecordWrite,
};
use serde_json::{Value, json};

fn insert(connection: &Connection, kind: &str, mut body: Value) -> i64 {
    body["versionNumber"] = json!(1);
    body["companyScope"] = json!("QUERY");
    body["ownerUserId"] = json!(7);
    let id = connection
        .insert(&RecordWrite {
            kind,
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(id);
    connection.set_body(id, &body).unwrap();
    id
}
pub fn exercise(connection: &Connection) {
    connection.begin().unwrap();
    let category = insert(
        connection,
        "attachment-categories",
        json!({"name":"资料分类"}),
    );
    for status in ["Active", "Archived"] {
        insert(
            connection,
            "attachments",
            json!({"categoryId":category,"invoiceId":991,"status":status,"large":"x".repeat(10_000)}),
        );
    }
    assert_eq!(
        connection
            .attachment_category_counts("QUERY")
            .unwrap()
            .get(&category),
        Some(&2)
    );
    assert!(
        connection
            .attachment_category_counts("OTHER")
            .unwrap()
            .is_empty()
    );
    assert!(
        connection
            .record_exists("attachments", &[("categoryId", category.to_string())])
            .unwrap()
    );
    assert!(
        !connection
            .record_exists("attachments", &[("invoiceId", "992".into())])
            .unwrap()
    );
    for (time, marker) in [
        ("2026-10-26T09:00:00.000000002+08:00", "second"),
        ("2026-10-26T01:00:00.000000001Z", "first"),
        ("2026-10-26T01:00:00.000000003Z", "third"),
    ] {
        insert(
            connection,
            "office-events",
            json!({"requestKind":"bookings","requestId":99,"createdAt":time,"updatedAt":time,"marker":marker}),
        );
    }
    insert(
        connection,
        "office-events",
        json!({"requestKind":"bookings","requestId":100,"createdAt":"2026-10-27T00:00:00Z"}),
    );
    let query = GenericQuery {
        kind: "office-events",
        scope: DataScope {
            all: true,
            ..Default::default()
        },
        filters: &[
            ("requestId", "99".into()),
            ("requestKind", "bookings".into()),
        ],
        keyword: "",
        search_root: None,
        fields: &[("id", "id"), ("marker", "marker")],
        offset: 1,
        limit: 1,
    };
    let (total, rows) = connection
        .query_ordered_records(&query, RecordOrder::CreatedDescending)
        .unwrap();
    assert_eq!(total, 3);
    assert_eq!(rows[0]["marker"], "second");
    assert!(!rows[0].as_object().unwrap().contains_key("createdAt"));
    let (_, rows) = connection
        .query_ordered_records(&query, RecordOrder::UpdatedDescending)
        .unwrap();
    assert_eq!(rows[0]["marker"], "second");
    connection.rollback().unwrap();

    connection.begin().unwrap();
    for index in 0..300 {
        connection.append_audit_details(&AuditWrite { kind: "invoices", record_id: 991, version: index + 1,
            action: if index % 2 == 0 { "create" } else { "edit" }, actor_id: 7,
            occurred_at: &format!("2026-10-26T09:00:00.{index:09}+08:00"), note: "" },
            &json!({"oldValues":null,"newValues":{"invoiceNo":format!("needle-{index}"),"large":"x".repeat(1000)}})).unwrap();
    }
    let mut query = AuditQuery {
        invoice: "needle",
        entity: "Invoice",
        action: "Added",
        user: "7",
        keyword: "NEEDLE",
        start: None,
        end: None,
        entities: &[("invoices", "Invoice", "发票")],
        offset: 1,
        limit: 3,
        count_all: true,
    };
    let (total, rows) = connection.query_audits(&query).unwrap();
    assert_eq!(total, 150);
    assert_eq!(rows.len(), 3);
    assert_eq!(rows[0]["versionNumber"], 297);
    assert_eq!(rows[0]["body"]["oldValues"], Value::Null);
    assert!(serde_json::to_vec(&rows).unwrap().len() < 5000);
    query.start = Some(
        chrono::DateTime::parse_from_rfc3339("2026-10-26T01:00:00.000000250Z")
            .unwrap()
            .to_utc(),
    );
    query.offset = 0;
    assert_eq!(connection.query_audits(&query).unwrap().0, 25);
    query.end = query.start;
    assert_eq!(connection.query_audits(&query).unwrap().0, 1);
    query.invoice = "";
    query.entity = "";
    query.action = "";
    query.user = "";
    query.keyword = "";
    query.start = None;
    query.end = None;
    assert_eq!(connection.query_audits(&query).unwrap().0, 300);
    query.count_all = false;
    assert_eq!(connection.query_audits(&query).unwrap().1.len(), 3);
    connection
        .append_audit(&AuditWrite {
            kind: "invoices",
            record_id: 991,
            version: 301,
            action: "edit",
            actor_id: 7,
            occurred_at: "broken time",
            note: "",
        })
        .unwrap();
    assert_eq!(
        connection.query_audits(&query).unwrap_err().kind,
        ErrorKind::Unavailable
    );
    connection.rollback().unwrap();
}
