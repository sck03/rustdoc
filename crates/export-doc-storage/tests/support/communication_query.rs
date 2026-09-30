use export_doc_storage::{
    CommunicationQuery, CommunicationView, Connection, NotificationScope, RecordWrite,
};
use serde_json::{Value, json};

pub fn exercise(db: &Connection) {
    let insert = |kind: &str, identity: &str, mut body: Value| {
        body["versionNumber"] = json!(1);
        body["companyScope"] = json!("COMMS-QUERY");
        let id = db
            .insert(&RecordWrite {
                kind,
                identity: Some(identity),
                body: &body,
            })
            .unwrap();
        body["id"] = json!(id);
        db.set_body(id, &body).unwrap();
        id
    };
    db.begin().unwrap();
    let announcement = |pin, department: &str, end: &str| json!({"ownerUserId":100,"departmentId":"A","status":"Published","startsAt":"2026-09-01T00:00:00.000Z","expiresAt":end,"audienceDepartment":department,"isPinned":pin,"publishVersion":1});
    let a = insert(
        "announcement",
        "visible-pinned",
        announcement(true, "A", "2026-10-01T00:00:00.000Z"),
    );
    insert(
        "announcement",
        "expired",
        announcement(true, "", "2026-09-30T00:00:00.000Z"),
    );
    insert(
        "announcement",
        "wrong-department",
        announcement(true, "B", "2026-10-01T00:00:00.000Z"),
    );
    insert(
        "announcement",
        "visible-company",
        announcement(false, "", "2026-10-01T00:00:00.000Z"),
    );
    let mut q = CommunicationQuery {
        company: "COMMS-QUERY",
        department: "A",
        reader: 200,
        unread_only: false,
        view: CommunicationView::Announcements {
            manage: false,
            now: "2026-09-30T00:00:00.000Z",
        },
        offset: 0,
        limit: 1,
    };
    let (count, rows) = db.query_communications(&q).unwrap();
    assert_eq!(count, 2);
    assert_eq!(rows[0]["id"], a);
    insert(
        "announcement-receipt",
        "read-old-release",
        json!({"ownerUserId":200,"departmentId":"A","requestId":a,"publishVersion":0}),
    );
    q.unread_only = true;
    assert_eq!(db.query_communications(&q).unwrap().0, 2);
    insert(
        "announcement-receipt",
        "read-current-release",
        json!({"ownerUserId":200,"departmentId":"A","requestId":a,"publishVersion":1}),
    );
    assert_eq!(db.query_communications(&q).unwrap().0, 1);
    q.company = "OTHER";
    assert_eq!(db.query_communications(&q).unwrap().0, 0);
    let parent = insert(
        "oa-general",
        "parent",
        json!({"ownerUserId":100,"departmentId":"A"}),
    );
    insert(
        "site-notification",
        "event-recipient",
        json!({"ownerUserId":200,"departmentId":"A","requestId":parent,"requestKind":"oa-general","status":"Unread"}),
    );
    for (rank, expected) in [(0, 0), (1, 0), (2, 1), (3, 1), (4, 1)] {
        let scopes = [NotificationScope {
            kind: "oa-general".into(),
            rank,
            statuses: vec![],
        }];
        let q = CommunicationQuery {
            company: "COMMS-QUERY",
            department: "A",
            reader: 200,
            unread_only: true,
            view: CommunicationView::Notifications { scopes: &scopes },
            offset: 0,
            limit: 20,
        };
        assert_eq!(db.query_communications(&q).unwrap().0, expected);
    }
    // Finance status and department limits apply before counting and pagination.
    for (index, status) in ["Approved", "HandedOff", "Draft", "Pending", "Cancelled"]
        .iter()
        .enumerate()
    {
        let parent = insert(
            "oa-expense",
            &format!("expense-{index}"),
            json!({"ownerUserId":100,"departmentId":"A","status":status}),
        );
        insert(
            "site-notification",
            &format!("finance-{index}"),
            json!({"ownerUserId":200,"departmentId":"A","requestId":parent,"requestKind":"oa-expense","status":"Unread"}),
        );
    }
    let scopes = [NotificationScope {
        kind: "oa-expense".into(),
        rank: 2,
        statuses: vec!["Approved".into(), "HandedOff".into()],
    }];
    let mut finance = CommunicationQuery {
        company: "COMMS-QUERY",
        department: "A",
        reader: 200,
        unread_only: true,
        view: CommunicationView::Notifications { scopes: &scopes },
        offset: 0,
        limit: 1,
    };
    let (count, rows) = db.query_communications(&finance).unwrap();
    assert_eq!(count, 2);
    assert_eq!(rows.len(), 1);
    finance.offset = 1;
    let (count, next) = db.query_communications(&finance).unwrap();
    assert_eq!(count, 2);
    assert_ne!(rows[0]["id"], next[0]["id"]);
    finance.offset = 0;
    finance.department = "B";
    assert_eq!(db.query_communications(&finance).unwrap().0, 0);
    // A failed/reverted business transaction cannot leave published notices or receipts.
    db.rollback().unwrap();
    q.company = "COMMS-QUERY";
    assert_eq!(db.query_communications(&q).unwrap().0, 0);
    assert!(
        db.find_identity("site-notification", "event-recipient")
            .unwrap()
            .is_none()
    );
}
