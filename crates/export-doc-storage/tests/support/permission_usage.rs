use export_doc_storage::{Connection, RecordWrite};
use serde_json::{Value, json};

fn insert(c: &Connection, kind: &str, body: &Value) -> i64 {
    let mut value = body.clone();
    value["ownerUserId"] = json!(0);
    value["companyScope"] = json!("USAGE");
    value["departmentId"] = json!("");
    c.insert(&RecordWrite::unkeyed(kind, &value)).unwrap()
}

pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    super::organization_fixture::unit(c, "USAGE", &[]);
    let draft =
        json!({"code":"usage","name":"Usage","grants":[],"isActive":true,"versionNumber":1});
    let scheme = insert(c, "permission-templates", &draft);
    let unpublished = insert(
        c,
        "permission-templates",
        &json!({"code":"unpublished","grants":[],"versionNumber":1}),
    );
    for expected in 1..=3 {
        assert_eq!(
            c.publish_permission_scheme(scheme, &draft, 0, "2026-11-17T00:00:00Z")
                .unwrap(),
            expected
        );
    }
    let account = json!({"username":"usage-a","fullName":"Usage","role":"OfficeEmployee","isActive":true,
        "versionNumber":1,"permissionTemplateId":scheme,"permissionSchemeVersion":1});
    let first = insert(c, "users", &account);
    let mut second = account.clone();
    second["username"] = json!("usage-b");
    second["permissionGrants"] = Value::Null;
    second["permissionSchemeVersion"] = json!(3);
    second["isActive"] = json!(false);
    insert(c, "users", &second);
    let mut independent = account.clone();
    independent["username"] = json!("usage-independent");
    independent["permissionGrants"] = json!([]);
    insert(c, "users", &independent);
    let role = json!({"userId":first,"companyCode":"USAGE","mode":"Administration","isActive":true,
        "versionNumber":1,"permissionTemplateId":scheme,"permissionSchemeVersion":2});
    let role_id = insert(c, "unit-access", &role);
    assert!(c.permission_scheme_usage(&[]).unwrap().is_empty());
    let mut ids = vec![scheme, unpublished, scheme];
    ids.extend(1_000_000..1_000_211); // Cross the bound-parameter batch boundary.
    let summaries = c.permission_scheme_usage(&ids).unwrap();
    assert_eq!(summaries.len(), 213);
    let usage = &summaries[&scheme];
    assert_eq!(usage.account_reference_count, 2);
    assert_eq!(usage.unit_role_reference_count, 1);
    assert_eq!(usage.published_version, Some(3));
    assert_eq!(summaries[&unpublished].published_version, None);
    assert_eq!(summaries[&unpublished].account_reference_count, 0);
    assert_eq!(summaries[&1_000_000].unit_role_reference_count, 0);
    for (kind, id) in [("unit-access", role_id), ("users", first)] {
        let mut detached = c.get(kind, id).unwrap().unwrap();
        detached["permissionGrants"] = json!([]);
        detached["versionNumber"] = json!(2);
        assert!(
            c.update(id, 1, &RecordWrite::unkeyed(kind, &detached))
                .unwrap()
        );
    }
    let usage = c.permission_scheme_usage(&[scheme]).unwrap();
    assert_eq!(usage[&scheme].account_reference_count, 1);
    assert_eq!(usage[&scheme].unit_role_reference_count, 0);
    c.rollback().unwrap();
    assert_eq!(
        c.permission_scheme_usage(&[scheme]).unwrap()[&scheme].published_version,
        None
    );
}
