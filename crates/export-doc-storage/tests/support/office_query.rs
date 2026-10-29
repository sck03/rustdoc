use export_doc_storage::{Connection, DataScope, OfficeQuery, RecordWrite};
use serde_json::json;
pub fn exercise(c: &Connection) {
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
            .insert(&RecordWrite {
                kind: "supply-requests",
                identity: None,
                body: &body,
            })
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
        viewer: 0,
        assigned_only: false,
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
        can_issue: true,
        can_return: true,
        from: None,
        to: None,
        offset: 0,
        limit: 1,
    };
    let (total, page) = c.query_office(&q).unwrap();
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
