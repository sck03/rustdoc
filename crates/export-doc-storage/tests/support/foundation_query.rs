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
    let id = tx.insert(&RecordWrite::unkeyed("people", &body)).unwrap();
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
        tx.update(person_id, 1, &RecordWrite::unkeyed("people", &body))
            .unwrap()
    );
    body["status"] = json!("Active");
    body["departedOn"] = Value::Null;
    body["hireDate"] = json!("2026-10-01");
    body["versionNumber"] = json!(3);
    assert!(
        tx.update(person_id, 2, &RecordWrite::unkeyed("people", &body))
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
    let options = tx
        .foundation_options(unit, "departments", "", &[], 0, 20)
        .unwrap();
    let department = options["items"][0]["id"].clone();
    let mut projects = Vec::new();
    for index in 0..105 {
        let project = tx
            .foundation_save(
                "projects",
                None,
                unit,
                0,
                &json!({
                    "code": format!("PROJECT-{index:03}"), "name": format!("项目 {index}"),
            "ownerAppointmentId": "foundation-a", "members": [{"appointmentId":"foundation-a","role":"Member","period":{"startsOn":"2026-01-01","endsOn":null}}], "isActive": index != 104
                }),
            )
            .unwrap();
        projects.push(project["id"].as_str().unwrap().parse::<i64>().unwrap());
    }
    let page = tx
        .foundation_options(unit, "projects", "", &[projects[0], projects[104]], 100, 20)
        .unwrap();
    assert_eq!(page["totalCount"], 104);
    assert_eq!(page["items"].as_array().unwrap().len(), 4);
    assert_eq!(page["selected"].as_array().unwrap().len(), 2);
    assert_eq!(page["selected"][1]["isActive"], false);
    let search = tx
        .foundation_options(unit, "projects", "PROJECT-103", &[], 0, 20)
        .unwrap();
    assert_eq!(search["totalCount"], 1);
    assert_eq!(search["items"][0]["id"], projects[103].to_string());
    let other = tx.find_identity("companies", "found-b").unwrap().unwrap()["id"]
        .as_i64()
        .unwrap();
    assert_eq!(
        tx.foundation_options(other, "projects", "", &[projects[0]], 0, 20)
            .unwrap()["selected"],
        json!([])
    );
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
    let mut restricted = responsibility.clone();
    restricted["assignments"][0]["projectId"] = json!(projects[0].to_string());
    let version = responsibility["versionNumber"].as_i64().unwrap();
    tx.foundation_save(
        "responsibilities",
        Some(responsibility_id),
        unit,
        version,
        &restricted,
    )
    .unwrap();
    assert_eq!(
        tx.responsibility_appointments(
            unit,
            responsibility_id,
            Some(projects[0]),
            "purchase",
            "2026-10-31"
        )
        .unwrap()
        .len(),
        1
    );
    assert!(
        tx.responsibility_appointments(
            unit,
            responsibility_id,
            Some(projects[1]),
            "purchase",
            "2026-10-31"
        )
        .unwrap()
        .is_empty()
    );
    assert!(
        tx.responsibility_appointments(unit, responsibility_id, None, "purchase", "2026-10-31")
            .unwrap()
            .is_empty()
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
    let codes = (0..505)
        .map(|index| format!("DEPT-{index:03}"))
        .collect::<Vec<_>>();
    super::organization_fixture::unit(
        tx,
        "FOUND-PAGED",
        &codes.iter().map(String::as_str).collect::<Vec<_>>(),
    );
    let paged_unit = tx
        .find_identity("companies", "found-paged")
        .unwrap()
        .unwrap()["id"]
        .as_i64()
        .unwrap();
    let departments = tx
        .foundation_options(paged_unit, "departments", "", &[], 500, 20)
        .unwrap();
    assert_eq!(departments["totalCount"], 505);
    assert_eq!(departments["items"].as_array().unwrap().len(), 5);
    assert_eq!(departments["items"][4]["code"], "DEPT-504");
    let filter = DataScope {
        company: "FOUND-A",
        company_wide: true,
        ..Default::default()
    };
    assert_eq!(tx.sole_exporter_name(&filter).unwrap(), None);
    for (company, name) in [
        ("FOUND-A", "  Exporter  "),
        ("FOUND-A", "EXPORTER"),
        ("FOUND-B", "Hidden name"),
    ] {
        tx.insert(&RecordWrite::unkeyed(
            "exporters",
            &json!({"companyScope":company,"versionNumber":1,"ownerUserId":7,"departmentId":"D","exporterNameCN":name}),
        ))
        .unwrap();
    }
    assert!(
        tx.sole_exporter_name(&filter)
            .unwrap()
            .unwrap()
            .eq_ignore_ascii_case("Exporter")
    );
    tx.insert(&RecordWrite::unkeyed(
        "exporters",
        &json!({"companyScope":"FOUND-A","versionNumber":1,"ownerUserId":7,"departmentId":"D","exporterNameCN":"Another"}),
    ))
    .unwrap();
    assert_eq!(tx.sole_exporter_name(&filter).unwrap(), None);
    tx.rollback().unwrap();
}
