#[path = "support/native_fixture.rs"]
mod native_fixture;

use export_doc_engine::generated_api::*;
use serde_json::{Value, json};

#[test]
fn unused_default_organization_can_be_deleted_after_admin_reassignment() {
    for assign_company in [false, true] {
        let mut fixture = native_fixture::Fixture::new();
        let directory = fixture.request(GET_ORGANIZATION_DIRECTORY, None, None);
        let company = &directory["companies"][0];
        let department = &directory["departments"][0];
        let schemes = fixture.request(LIST_PERMISSION_TEMPLATES, None, None);
        if assign_company {
            fixture.create(
                CREATE_ORGANIZATION_COMPANY,
                json!({"code":"OTHER","name":"另一公司","isActive":true}),
            );
        }
        let accounts = fixture.request(LIST_USERS, None, None);
        let mut admin = accounts["users"][0].clone();
        admin["expectedVersion"] = admin["versionNumber"].clone();
        admin["companyScope"] = json!(if assign_company { "OTHER" } else { "" });
        admin["departmentId"] = json!("");
        fixture.request(UPDATE_USER_ACCOUNT, admin["id"].as_i64(), Some(admin));
        fixture.client = Some(
            fixture
                .client()
                .login("admin".into(), String::new())
                .unwrap()
                .0,
        );

        let deletion =
            |operation, row: &Value| {
                fixture.client().json::<Value>(
            operation, &[("id", row["id"].to_string())], &[],
            Some(json!({"expectedVersion":row["versionNumber"],"reason":"清理未使用的初始组织"})),
        )
            };
        assert_eq!(
            deletion(DELETE_ORGANIZATION_COMPANY, company)
                .unwrap_err()
                .status,
            Some(409),
            "the department still belongs to the company"
        );
        deletion(DELETE_ORGANIZATION_DEPARTMENT, department)
            .expect("creator scope is not a department reference");
        deletion(DELETE_ORGANIZATION_COMPANY, company)
            .expect("global permission schemes do not belong to their creator's company");
        let remaining = fixture.request(GET_ORGANIZATION_DIRECTORY, None, None);
        assert!(remaining["departments"].as_array().unwrap().is_empty());
        assert_eq!(
            remaining["companies"].as_array().unwrap().len(),
            usize::from(assign_company)
        );
        assert_eq!(
            fixture.request(LIST_PERMISSION_TEMPLATES, None, None),
            schemes
        );
    }
}
