use export_doc_storage::{Connection, OrganizationReference, PersonnelQuery, RecordWrite};
use serde_json::{Value, json};

fn insert(tx: &Connection, kind: &str, mut body: Value) -> i64 {
    body["versionNumber"] = json!(1);
    body["ownerUserId"] = json!(7);
    let id = tx
        .insert(&RecordWrite {
            kind,
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(id);
    tx.set_body(id, &body).unwrap();
    id
}
pub fn exercise(tx: &Connection) {
    tx.begin().unwrap();
    super::organization_fixture::unit(tx, "ORG-A", &["ORG-D", "PRIMARY", "SECONDARY"]);
    let allowed = ["departments", "users", "people", "invoices"];
    let reference = |kind, code, exclude| {
        tx.organization_referenced(kind, code, "ORG-A", exclude, &allowed)
            .unwrap()
    };
    let department = insert(
        tx,
        "departments",
        json!({"companyCode":"ORG-A","parentCode":"ORG-D","code":"CHILD"}),
    );
    assert!(reference(OrganizationReference::Company, "ORG-A", 0));
    assert!(reference(OrganizationReference::Department, "ORG-D", 0));
    assert!(!reference(
        OrganizationReference::Department,
        "ORG-D",
        department
    ));
    assert!(!reference(OrganizationReference::Company, "' OR 1=1 --", 0));
    let person = insert(
        tx,
        "people",
        json!({"companyScope":"ORG-A","departmentId":"PRIMARY","status":"Departed",
        "organization":{"primaryAssignmentId":"org-primary","additionalAssignments":[{"id":"org-secondary","departmentId":"SECONDARY","jobTitle":"兼任"}]},"profile":{"fullName":"任职查询人员"}}),
    );
    let private = json!({"notes":"x".repeat(200_000),"personalPhone":"private-contact"});
    tx.set_personnel_private_profile(person, &private).unwrap();
    assert_eq!(
        tx.personnel_private_profile(person).unwrap().unwrap(),
        private
    );
    let public = tx.get("people", person).unwrap().unwrap();
    assert!(public["profile"].get("personalPhone").is_none());
    assert!(
        public.to_string().len() < 2000,
        "public appointment reads never load the private payload"
    );
    assert!(
        reference(OrganizationReference::Department, "SECONDARY", 0),
        "past secondary appointments remain references"
    );
    assert!(!reference(
        OrganizationReference::Department,
        "SECONDARY",
        person
    ));
    assert!(
        !tx.personnel_exists(&PersonnelQuery {
            as_of: "2026-10-29",
            company: "ORG-A",
            department: "SECONDARY",
            status: "",
            keyword: "",
            search_fields: &[],
            scopes: &[],
            fields: &[("id", "id")],
            offset: 0,
            limit: 1
        })
        .unwrap(),
        "departed staff do not count as active members"
    );
    insert(
        tx,
        "users",
        json!({"companyScope":"ORG-A","departmentId":"INACTIVE","isActive":false}),
    );
    assert!(reference(OrganizationReference::Department, "INACTIVE", 0));
    insert(
        tx,
        "not-a-business-resource",
        json!({"companyCode":"IGNORED"}),
    );
    assert!(!reference(OrganizationReference::Company, "IGNORED", 0));
    assert!(tx.delete("departments", department, 1).unwrap());
    assert!(!reference(OrganizationReference::Department, "ORG-D", 0));
    let mut leaked = public;
    leaked["profile"]["personalPhone"] = json!("must-stay-private");
    assert!(
        tx.update(
            person,
            1,
            &RecordWrite {
                kind: "people",
                identity: None,
                body: &leaked
            }
        )
        .is_err(),
        "database rejects private fields in public personnel records"
    );
    tx.rollback().unwrap();
    assert!(
        tx.personnel_private_profile(person).unwrap().is_none(),
        "private and public writes roll back together"
    );
}
