use export_doc_storage::{Connection, RecordQuery, RecordWrite};
use serde_json::json;
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    for (kind, details) in [
        (
            "oa-leave",
            json!({"leave":{"startsOn":"2026-10-28","endsOn":"2026-10-29"}}),
        ),
        (
            "oa-travel",
            json!({"travel":{"startsOn":"2026-10-30","endsOn":"2026-10-31"}}),
        ),
        (
            "oa-overtime",
            json!({"overtime":{"startsAt":"2026-10-28T00:00:00+08:00","endsAt":"2026-10-28T02:00:00+08:00"}}),
        ),
    ] {
        let mut body = json!({"companyScope":"PERIOD-Q","departmentId":"A","employeeId":42,"ownerUserId":42,"status":"Approved","versionNumber":1});
        body.as_object_mut()
            .unwrap()
            .extend(details.as_object().unwrap().clone());
        c.insert(&RecordWrite {
            kind,
            identity: None,
            body: &body,
        })
        .unwrap();
    }
    for (kind, start, end, count) in [
        ("oa-leave", "2026-10-29", "2026-10-30", 1),
        ("oa-leave", "2026-10-30", "2026-10-31", 0),
        ("oa-travel", "2026-10-28", "2026-10-29", 0),
        ("oa-travel", "2026-10-31", "2026-11-01", 1),
        (
            "oa-overtime",
            "2026-10-27T17:00:00Z",
            "2026-10-27T19:00:00Z",
            1,
        ),
        (
            "oa-overtime",
            "2026-10-27T18:00:00Z",
            "2026-10-27T19:00:00Z",
            0,
        ),
    ] {
        let mut q = RecordQuery {
            kind,
            company: "PERIOD-Q",
            employee: Some(42),
            period: Some((start, end)),
            limit: 20,
            ..Default::default()
        };
        assert_eq!(c.query_records(&q).unwrap().0, count, "{kind} {start}");
        q.company = "OTHER";
        assert_eq!(c.query_records(&q).unwrap().0, 0);
    }
    c.rollback().unwrap();
}
