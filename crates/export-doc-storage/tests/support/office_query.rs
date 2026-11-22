use export_doc_storage::{Connection, DataScope, OfficeQuery, RecordWrite};
use serde_json::json;
pub fn exercise(c: &Connection) {
    booking_conflicts(c);
    c.begin().unwrap();
    let mut ids = vec![];
    for (owner, company, department, status, key) in [
        (71, "OFFICE-Q", "A", "Pending", ""),
        (72, "OFFICE-Q", "B", "Approved", "handler"),
        (72, "OFFICE-Q", "B", "Pending", "handler"),
        (72, "OTHER-Q", "B", "Approved", "handler"),
    ] {
        let mut body = json!({"versionNumber":1,"ownerUserId":owner,"companyScope":company,"departmentId":department,"status":status,"handlingKey":key,"createdAt":"2026-10-24T08:00:00+08:00","quantity":1,"isReturnable":true});
        let id = c
            .insert(&RecordWrite::unkeyed("supply-requests", &body))
            .unwrap();
        body["id"] = json!(id);
        c.set_body(id, &body).unwrap();
        ids.push(id);
    }
    let scope = || DataScope {
        own: true,
        user_id: 71,
        company: "OFFICE-Q",
        department: "A",
        ..Default::default()
    };
    let keys = vec!["handler".into()];
    let mut q = OfficeQuery {
        workflow_date: None,
        node_access: vec![],
        viewer: 0,
        assigned_only: false,
        context: "",
        principals: &[],
        kind: "supply-requests",
        company: "OFFICE-Q",
        view: scope(),
        issue: scope(),
        return_scope: scope(),
        handling_keys: &keys,
        keyword_handling_keys: &[],
        keyword: "",
        status: "",
        identifiers: &[],
        owner: None,
        include_inactive: false,
        low_stock: false,
        handling_only: false,
        approvals_only: false,
        can_issue: true,
        can_return: true,
        from: None,
        to: None,
        fields: &[],
        offset: 0,
        limit: 1,
    };
    let (total, page) = c.query_office(&q).unwrap();
    q.fields = &[("id", "id")];
    let projected = c.query_office(&q).unwrap();
    assert_eq!(projected.0, total);
    assert_eq!(projected.1[0], json!({"id":page[0]["id"]}));
    q.fields = &[];
    assert_eq!(total, 2);
    assert_eq!(page[0]["id"], ids[1]);
    q.offset = 1;
    assert_eq!(c.query_office(&q).unwrap().1[0]["id"], ids[0]);
    q.handling_only = true;
    q.offset = 0;
    assert_eq!(c.query_office(&q).unwrap().0, 1);
    q.can_issue = false;
    assert_eq!(c.query_office(&q).unwrap().0, 0);
    q.handling_only = false;
    q.owner = Some(71);
    assert_eq!(c.query_office(&q).unwrap().0, 1);
    q.from = Some("2026-10-24T00:00:00Z");
    assert_eq!(c.query_office(&q).unwrap().0, 0);
    q.from = None;
    q.to = Some("2026-10-24T00:00:01Z");
    assert_eq!(c.query_office(&q).unwrap().0, 1);
    q.to = None;
    q.offset = i64::MAX;
    assert!(c.query_office(&q).unwrap().1.is_empty());
    c.rollback().unwrap();
}

fn booking_conflicts(c: &Connection) {
    c.begin().unwrap();
    let states = ["Pending", "Suspended", "Approved", "InUse"];
    let start = "2026-11-22T10:00:00.000000001+08:00";
    let end = "2026-11-22T11:00:00.000000001+08:00";
    for (room, status) in states
        .iter()
        .chain(["Completed", "Draft", "Cancelled"].iter())
        .enumerate()
    {
        let body = json!({"companyScope":"BOOKING-CONFLICT","ownerUserId":71,
            "departmentId":"A","versionNumber":1,"meetingRoomId":room as i64 + 1,
            "status":status,"startsAt":start,"endsAt":end});
        c.insert(&RecordWrite::unkeyed("bookings", &body)).unwrap();
        assert_eq!(
            c.booking_overlaps("BOOKING-CONFLICT", room as i64 + 1, start, end, &states)
                .unwrap(),
            room < states.len()
        );
    }
    for (from, to, expected) in [
        (
            "2026-11-22T01:00:00Z",
            "2026-11-22T02:00:00.000000001Z",
            false,
        ),
        (
            "2026-11-22T01:00:00Z",
            "2026-11-22T02:00:00.000000002Z",
            true,
        ),
        (
            "2026-11-22T03:00:00.000000001Z",
            "2026-11-22T04:00:00Z",
            false,
        ),
        ("2026-11-22T03:00:00Z", "2026-11-22T04:00:00Z", true),
    ] {
        assert_eq!(
            c.booking_overlaps("BOOKING-CONFLICT", 1, from, to, &states)
                .unwrap(),
            expected
        );
    }
    assert!(!c.booking_overlaps("OTHER", 1, start, end, &states).unwrap());
    assert!(
        !c.booking_overlaps("BOOKING-CONFLICT", 99, start, end, &states)
            .unwrap()
    );
    assert!(
        !c.booking_overlaps(
            "BOOKING-CONFLICT",
            4,
            "2026-11-22T04:00:00Z",
            "2026-11-22T05:00:00Z",
            &states
        )
        .unwrap(),
        "an overdue handover does not extend the reserved interval"
    );
    c.insert(&RecordWrite::unkeyed("bookings", &json!({"companyScope":"BOOKING-CONFLICT","ownerUserId":71,
        "departmentId":"A","versionNumber":1,"meetingRoomId":99,"status":"Pending","startsAt":"damaged","endsAt":end}))).unwrap();
    assert!(
        c.booking_overlaps("BOOKING-CONFLICT", 99, start, end, &states)
            .is_err(),
        "corrupt instants must not become a free room or a business conflict"
    );
    c.rollback().unwrap();
}
