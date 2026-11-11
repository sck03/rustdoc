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
        departments: &[],
        assigned_only: false,
        context: "",
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
    for (now, expected) in [
        ("2026-09-30T00:00:00.000000000Z", 2),
        ("2026-09-30T00:00:00.000000001Z", 2),
        ("2026-09-30T08:00:00+08:00", 2),
        ("2026-08-31T23:59:59.999999999Z", 0),
        ("2026-09-01T00:00:00.000000000Z", 3),
        ("2026-09-01T00:00:00.000000001Z", 3),
        ("2026-09-30T00:00:00.000Z", 2),
    ] {
        q.view = CommunicationView::Announcements { manage: false, now };
        assert_eq!(
            db.query_communications(&q).unwrap().0,
            expected,
            "validity at {now}"
        );
    }
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
    q.company = "COMMS-QUERY";
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
    let scope = |kind: &str, rank, statuses, handling_keys| NotificationScope {
        workflow_date: None,
        node_access: vec![],
        restrictions: vec![],
        approval_principals: vec![],
        kind: kind.into(),
        rank,
        statuses,
        handling_keys,
    };
    for (rank, expected) in [(0, 0), (1, 0), (2, 1), (3, 1), (4, 1)] {
        let scopes = [
            scope("oa-general", rank, vec![], None),
            scope(
                "oa-general",
                3,
                vec!["Approved".into(), "Completed".into()],
                Some(vec![]),
            ),
        ];
        let q = CommunicationQuery {
            view: CommunicationView::Notifications { scopes: &scopes },
            limit: 20,
            ..q
        };
        assert_eq!(db.query_communications(&q).unwrap().0, expected);
    }
    // Real notification pages combine permissions for several workflow kinds at once.
    let mut scopes: Vec<_> = (0..12)
        .map(|index| scope(&format!("oa-other-{index}"), 2, vec![], None))
        .collect();
    scopes.push(scope("oa-general", 2, vec![], None));
    for scope in &mut scopes {
        scope.workflow_date = Some(chrono::NaiveDate::from_ymd_opt(2026, 9, 30).unwrap());
    }
    let combined = CommunicationQuery {
        view: CommunicationView::Notifications { scopes: &scopes },
        ..q
    };
    assert_eq!(db.query_communications(&combined).unwrap().0, 1);
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
    let scopes = [scope(
        "oa-expense",
        2,
        vec!["Approved".into(), "HandedOff".into()],
        None,
    )];
    let mut finance = CommunicationQuery {
        view: CommunicationView::Notifications { scopes: &scopes },
        ..q
    };
    let (count, rows) = db.query_communications(&finance).unwrap();
    assert_eq!((count, rows.len()), (2, 1));
    finance.offset = 1;
    let (count, next) = db.query_communications(&finance).unwrap();
    assert_eq!(count, 2);
    assert_ne!(rows[0]["id"], next[0]["id"]);
    finance.offset = 0;
    finance.department = "B";
    assert_eq!(db.query_communications(&finance).unwrap().0, 0);
    finance.assigned_only = true;
    assert_eq!(db.query_communications(&finance).unwrap().0, 0);
    // A failed/reverted business transaction cannot leave published notices or receipts.
    db.rollback().unwrap();
    assert_eq!(db.query_communications(&q).unwrap().0, 0);
    assert!(
        db.find_identity("site-notification", "event-recipient")
            .unwrap()
            .is_none()
    );
}
