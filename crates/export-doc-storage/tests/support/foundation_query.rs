use export_doc_storage::{Connection, DataScope, RecordWrite};
use serde_json::{Value, json};
fn person(
    tx: &Connection,
    unit: &str,
    number: &str,
    appointment: &str,
    owner: i64,
) -> (i64, Value) {
    let mut body = json!({"versionNumber":1,"companyScope":unit,"departmentId":"D","employeeNumber":number,"status":"Active","employmentType":"FullTime","hireDate":"2020-01-01","jobTitle":"专员","ownerUserId":owner,"profile":{"fullName":"同名人员"},"organization":{"primaryAssignmentId":appointment,"additionalAssignments":[]}});
    let id = tx
        .insert(&RecordWrite {
            kind: "people",
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(id);
    tx.set_body(id, &body).unwrap();
    let public = tx.get("people", id).unwrap().unwrap();
    assert_eq!(public["employee"]["fullName"], "同名人员");
    assert_eq!(public["employee"]["id"], id);
    (id, body)
}
pub fn exercise(tx: &Connection) {
    tx.begin().unwrap();
    super::organization_fixture::unit(tx, "FOUND-A", &["D"]);
    super::organization_fixture::unit(tx, "FOUND-B", &["D"]);
    let unit = tx.find_identity("companies", "found-a").unwrap().unwrap()["id"]
        .as_i64()
        .unwrap();
    let (person_id, mut body) = person(tx, "FOUND-A", "001", "foundation-a", 7);
    person(tx, "FOUND-B", "001", "foundation-b", 8);
    let all = DataScope {
        all: true,
        ..Default::default()
    };
    let first = tx
        .foundation_list("employments", unit, &all, 0, 20)
        .unwrap();
    assert_eq!(first["totalCount"], 1);
    let identity = first["items"][0]["identityId"].clone();
    body["status"] = json!("Departed");
    body["departedOn"] = json!("2024-06-30");
    body["versionNumber"] = json!(2);
    assert!(
        tx.update(
            person_id,
            1,
            &RecordWrite {
                kind: "people",
                identity: None,
                body: &body
            }
        )
        .unwrap()
    );
    body["status"] = json!("Active");
    body["departedOn"] = Value::Null;
    body["hireDate"] = json!("2026-10-01");
    body["versionNumber"] = json!(3);
    assert!(
        tx.update(
            person_id,
            2,
            &RecordWrite {
                kind: "people",
                identity: None,
                body: &body
            }
        )
        .unwrap()
    );
    let periods = tx
        .foundation_list("employments", unit, &all, 0, 20)
        .unwrap();
    assert_eq!(periods["totalCount"], 2);
    assert!(
        periods["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|r| r["identityId"] == identity)
    );
    assert!(
        periods["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|r| r["period"]["endsOn"] == "2024-07-01")
    );
    let public = tx
        .foundation_candidates(unit, "同名", "2026-10-31", 0, 1)
        .unwrap();
    assert_eq!(public["totalCount"], 1);
    assert!(public["items"][0]["personId"].is_string());
    assert!(public["items"][0].get("profile").is_none());
    let department = public["departments"][0]["id"].clone();
    let responsibility=tx.foundation_save("responsibilities",None,unit,0,&json!({"code":"PURCHASE","name":"采购验收","departmentId":department,"workflowKinds":["purchase"],"isActive":true,"assignments":[{"id":null,"appointmentId":"foundation-a","projectId":null,"period":{"startsOn":"2026-01-01","endsOn":null}}]})).unwrap();
    let responsibility_id = responsibility["id"].as_str().unwrap().parse().unwrap();
    assert_eq!(
        tx.responsibility_appointments(unit, responsibility_id, None, "purchase", "2026-10-31")
            .unwrap()
            .len(),
        1
    );
    assert!(
        tx.responsibility_appointments(unit, responsibility_id, None, "expense", "2026-10-31")
            .unwrap()
            .is_empty()
    );
    assert!(
        tx.foundation_save(
            "responsibilities",
            Some(responsibility_id),
            unit,
            99,
            &responsibility
        )
        .is_err()
    );
    let own = DataScope {
        company: "FOUND-A",
        own: true,
        user_id: 8,
        ..Default::default()
    };
    assert_eq!(
        tx.foundation_list("employments", unit, &own, 0, 20)
            .unwrap()["totalCount"],
        0
    );
    tx.rollback().unwrap();
}
