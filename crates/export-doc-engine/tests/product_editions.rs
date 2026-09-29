#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{api::ApiClient, generated_api::*};
use serde_json::{Value, json};

#[test]
fn editions_limit_admin_capabilities_and_requests_after_login_and_renewal() {
    for edition in ["Full", "Document", "Sales", "Administration"] {
        let fixture = native_fixture::Fixture::edition(edition);
        let user = fixture.request(GET_CURRENT_USER, None, None);
        assert_eq!(user["capabilities"]["productEdition"], edition);
        let grants = user["capabilities"]["permissions"].as_array().unwrap();
        assert_eq!(
            user["capabilities"]["canManageUsers"],
            matches!(edition, "Full" | "Administration")
        );
        let policy = export_doc_domain::permissions::ProductEdition::parse(edition).unwrap();
        assert!(
            grants
                .iter()
                .all(|grant| policy.allows(grant["resourceKey"].as_str().unwrap()))
        );
        for (operation, resource) in [
            (LIST_INVOICES, "document.invoices"),
            (GET_CRM_DASHBOARD, "sales.customers"),
            (LIST_PERSONNEL, "office.people"),
            (LIST_MEETING_ROOMS, "office.rooms"),
            (LIST_OFFICE_SUPPLIES, "office.supplies"),
            (LIST_USERS, "system.users"),
            (LIST_PERMISSION_TEMPLATES, "system.permissions"),
        ] {
            let result: Result<Value, _> = fixture.client().json(operation, &[], &[], None);
            if policy.allows(resource) {
                assert!(result.is_ok(), "{edition} {}: {result:?}", operation.id);
            } else {
                assert_eq!(
                    result.unwrap_err().status,
                    Some(403),
                    "{edition} {}",
                    operation.id
                );
            }
        }
        let worklist = fixture.request(GET_WORKLIST, None, None);
        assert!(worklist.is_object());
        fixture.request(GET_LICENSE_STATUS, None, None);
        if matches!(edition, "Full" | "Administration") {
            fixture.employee();
            fixture.request(GET_ORGANIZATION_DIRECTORY, None, None);
            for kind in [
                "Leave", "Overtime", "Expense", "Travel", "Purchase", "General",
            ] {
                let operation = ALL_OPERATIONS
                    .iter()
                    .find(|op| op.id == format!("List{kind}Request"))
                    .unwrap();
                fixture.request(*operation, None, None);
            }
        }
        let renewed = fixture.request(RENEW_SESSION, None, None);
        assert_eq!(renewed["user"]["capabilities"]["productEdition"], edition);
    }
}

#[test]
fn opening_the_same_database_in_another_edition_preserves_data_and_denies_other_workspaces() {
    let mut fixture = native_fixture::Fixture::new();
    let employee = fixture.employee();
    fixture.client.take();
    let (client, _) = ApiClient::native_edition(fixture.paths.clone(), Default::default(), "Sales")
        .unwrap()
        .login("admin".into(), String::new())
        .unwrap();
    let denied: Result<Value, _> = client.json(
        GET_PERSONNEL,
        &[("id", employee["id"].to_string())],
        &[],
        None,
    );
    assert_eq!(denied.unwrap_err().status, Some(403));
    let denied: Result<Value, _> = client.json(CREATE_PERSONNEL, &[], &[], Some(json!({})));
    assert_eq!(denied.unwrap_err().status, Some(403));
    drop(client);
    fixture.client = Some(
        ApiClient::native_edition(fixture.paths.clone(), Default::default(), "Administration")
            .unwrap()
            .login("admin".into(), String::new())
            .unwrap()
            .0,
    );
    let restored = fixture.request(GET_PERSONNEL, employee["id"].as_i64(), None);
    assert_eq!(restored["employeeNumber"], employee["employeeNumber"]);
}

#[test]
fn unknown_edition_is_rejected_before_opening_a_database() {
    let fixture = native_fixture::Fixture::new();
    assert!(
        ApiClient::native_edition(fixture.paths.clone(), Default::default(), "Unknown").is_err()
    );
}
