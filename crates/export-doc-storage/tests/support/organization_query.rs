use export_doc_storage::{Connection, OrganizationReference, PersonnelQuery, RecordWrite};
use serde_json::{Value, json};

fn insert(tx: &Connection, kind: &str, mut body: Value) -> i64 {
    body["versionNumber"] = json!(1);
    body["ownerUserId"] = json!(7);
    let id = tx.insert(&RecordWrite::unkeyed(kind, &body)).unwrap();
    body["id"] = json!(id);
    tx.set_body(id, &body).unwrap();
    id
}
pub fn exercise(tx: &Connection) {
    reporting_counts(tx);
    reference_ownership(tx);
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
        json!({"companyScope":"ORG-A","departmentId":"PRIMARY","status":"Departed","employeeNumber":"ORG-P1","employmentType":"FullTime","hireDate":"2026-01-01","departedOn":"2026-10-01","jobTitle":"职员",
        "organization":{"primaryAssignmentId":"org-primary","additionalAssignments":[{"id":"org-secondary","companyCode":"ORG-A","departmentId":"SECONDARY","jobTitle":"兼任","startsOn":"2026-01-01","endsOn":"2026-10-01"}]},"profile":{"fullName":"任职查询人员"}}),
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
            attention_through: None,
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
        json!({"companyScope":"ORG-A","departmentId":"INACTIVE","isActive":false,"username":"inactive-query-user","fullName":"停用账号","role":"OfficeEmployee","permissionGrants":[],"useRoleDefaults":false}),
    );
    assert!(reference(OrganizationReference::Department, "INACTIVE", 0));
    let mut account_ids = vec![];
    for index in 0..205 {
        account_ids.push(insert(tx, "users", json!({"companyScope":"ORG-A","departmentId":"PRIMARY","isActive":true,"username":format!("page-account-{index}"),"fullName":"公开候选人","role":"OfficeEmployee","permissionGrants":[],"useRoleDefaults":false,"privateMarker":"not-projected"})));
    }
    account_ids.reverse();
    let first = tx
        .unit_account_page("ORG-A", "2026-10-31", false, None, 100)
        .unwrap();
    assert_eq!(first.len(), 100);
    assert!(first.iter().all(|row| row.get("privateMarker").is_none()));
    let second = tx
        .unit_account_page(
            "ORG-A",
            "2026-10-31",
            false,
            first.last().unwrap()["id"].as_i64(),
            100,
        )
        .unwrap();
    let third = tx
        .unit_account_page(
            "ORG-A",
            "2026-10-31",
            false,
            second.last().unwrap()["id"].as_i64(),
            100,
        )
        .unwrap();
    let found: Vec<_> = first
        .iter()
        .chain(&second)
        .chain(&third)
        .map(|row| row["id"].as_i64().unwrap())
        .collect();
    assert_eq!(
        found, account_ids,
        "cursor pagination preserves scope, ordering and every eligible account"
    );
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
        tx.update(person, 1, &RecordWrite::unkeyed("people", &leaked))
            .is_err(),
        "database rejects private fields in public personnel records"
    );
    tx.rollback().unwrap();
    assert!(
        tx.personnel_private_profile(person).unwrap().is_none(),
        "private and public writes roll back together"
    );
}

fn reporting_counts(tx: &Connection) {
    tx.begin().unwrap();
    super::organization_fixture::unit(tx, "REPORT-A", &["D"]);
    super::organization_fixture::unit(tx, "REPORT-B", &["D"]);
    let person = |number: &str, status: &str, company: &str, organization: Value| {
        json!({
            "companyScope":company,"departmentId":"D","status":status,"employeeNumber":number,
            "employmentType":"FullTime","hireDate":"2026-01-01","jobTitle":"职员",
            "departedOn":if status=="Departed" {Some("2026-10-01")} else {None},
            "organization":organization,"profile":{"fullName":number}
        })
    };
    let manager = insert(
        tx,
        "people",
        person(
            "manager",
            "Active",
            "REPORT-A",
            json!({"primaryAssignmentId":"report-manager"}),
        ),
    );
    let org = |id: &str| {
        json!({"primaryAssignmentId":id,"managerEmployeeId":manager,"managerAssignmentId":"report-manager",
        "functionalManagerEmployeeIds":[manager],"functionalManagerAssignmentIds":["report-manager"]})
    };
    let mut multiple = org("report-current");
    multiple["additionalAssignments"] = json!([{"id":"report-extra","companyCode":"REPORT-A","departmentId":"D","jobTitle":"兼任",
        "startsOn":"2026-01-01","endsOn":"","managerEmployeeId":manager,"managerAssignmentId":"report-manager"}]);
    insert(
        tx,
        "people",
        person("current", "Active", "REPORT-A", multiple),
    );
    insert(
        tx,
        "people",
        person("departed", "Departed", "REPORT-A", org("report-departed")),
    );
    let mut future = org("report-future");
    future["primaryStartsOn"] = json!("2027-01-01");
    insert(tx, "people", person("future", "Active", "REPORT-A", future));
    insert(
        tx,
        "people",
        person(
            "expired",
            "Active",
            "REPORT-B",
            json!({"primaryAssignmentId":"report-b", "additionalAssignments":[{
                "id":"report-expired","companyCode":"REPORT-A","departmentId":"D","jobTitle":"兼任","startsOn":"2026-01-01","endsOn":"2026-10-01",
                "managerEmployeeId":manager,"managerAssignmentId":"report-manager"
            }]}),
        ),
    );
    assert_eq!(
        tx.reporting_reference_count("REPORT-A", manager, Some("2026-11-28"))
            .unwrap(),
        1
    );
    assert_eq!(
        tx.reporting_reference_count("REPORT-A", manager, None)
            .unwrap(),
        4
    );
    assert_eq!(
        tx.reporting_reference_count("REPORT-B", manager, None)
            .unwrap(),
        0
    );
    assert_eq!(
        tx.reporting_reference_count("' OR 1=1 --", manager, None)
            .unwrap(),
        0
    );
    tx.rollback().unwrap();
}

fn reference_ownership(tx: &Connection) {
    tx.begin().unwrap();
    let company = insert(
        tx,
        "companies",
        json!({"code":"REF-A","name":"A","companyScope":"REF-A","departmentId":"CREATOR","isActive":true}),
    );
    let other = insert(
        tx,
        "companies",
        json!({"code":"REF-B","name":"B","companyScope":"REF-B","isActive":true}),
    );
    let department = insert(
        tx,
        "departments",
        json!({"companyCode":"REF-A","companyScope":"REF-A","code":"SIBLING","departmentId":"CREATOR","isActive":true}),
    );
    insert(
        tx,
        "permission-templates",
        json!({"code":"GLOBAL","ownerUnitId":null,"companyScope":"REF-A","departmentId":"CREATOR"}),
    );
    let allowed = [
        "companies",
        "departments",
        "permission-templates",
        "users",
        "people",
        "invoices",
        "unit-access",
    ];
    let references = |kind, code, exclude| {
        tx.organization_referenced(kind, code, "REF-A", exclude, &allowed)
            .unwrap()
    };
    assert!(
        !references(OrganizationReference::Department, "CREATOR", 0),
        "company, sibling department and global scheme creator metadata is not membership"
    );
    assert!(
        references(OrganizationReference::Company, "REF-A", company),
        "a department belongs to its explicit company"
    );
    tx.delete("departments", department, 1).unwrap();
    assert!(
        !references(OrganizationReference::Company, "REF-A", company),
        "global schemes do not own their creator's company"
    );

    let owned = insert(
        tx,
        "permission-templates",
        json!({"code":"OWNED","ownerUnitId":company,"companyScope":"REF-B","departmentId":"CREATOR"}),
    );
    assert!(
        references(OrganizationReference::Company, "REF-A", company),
        "unit schemes use ownerUnitId, independently of creator scope"
    );
    assert!(!references(OrganizationReference::Company, "REF-B", other));
    assert!(!references(OrganizationReference::Department, "CREATOR", 0));
    tx.delete("permission-templates", owned, 1).unwrap();

    let child = insert(
        tx,
        "companies",
        json!({"code":"REF-CHILD","name":"child","companyScope":"REF-CHILD","parentCompanyCode":"REF-A","isActive":true}),
    );
    assert!(references(OrganizationReference::Company, "REF-A", company));
    tx.delete("companies", child, 1).unwrap();
    let account = insert(
        tx,
        "users",
        json!({"username":"reference-user","role":"Admin","companyScope":"REF-B","departmentId":"","isActive":true}),
    );
    let role = insert(
        tx,
        "unit-access",
        json!({"userId":account,"companyCode":"REF-B","mode":"Administration","companyScope":"REF-A","departmentId":"CREATOR","permissionGrants":[],"isActive":true}),
    );
    assert!(
        !references(OrganizationReference::Company, "REF-A", company),
        "administration roles reference their target company"
    );
    assert!(
        !references(OrganizationReference::Department, "CREATOR", 0),
        "administration roles have no department appointment"
    );
    assert!(references(OrganizationReference::Company, "REF-B", account));
    tx.delete("unit-access", role, 1).unwrap();
    assert!(
        references(OrganizationReference::Company, "REF-B", other),
        "real account assignment remains protected"
    );

    let invoice = insert(
        tx,
        "invoices",
        json!({"companyScope":"REF-A","departmentId":"CREATOR"}),
    );
    assert!(
        references(OrganizationReference::Department, "CREATOR", 0),
        "business scope is a real reference"
    );
    assert!(references(OrganizationReference::Company, "REF-A", company));
    tx.delete("invoices", invoice, 1).unwrap();
    assert!(!references(
        OrganizationReference::Company,
        "REF-A",
        company
    ));
    assert!(!references(OrganizationReference::Department, "CREATOR", 0));
    tx.rollback().unwrap();
}
