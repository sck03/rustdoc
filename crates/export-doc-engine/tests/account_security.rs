#[path = "support/native_fixture.rs"]
mod native_fixture;
use export_doc_engine::generated_api::*;
use serde_json::{Value, json};

#[test]
fn self_service_password_change_preserves_permissions_and_revokes_only_its_account() {
    let fixture = native_fixture::Fixture::new();
    let account = fixture.create(CREATE_USER_ACCOUNT, json!({"username":"password-self","fullName":"密码测试员工","role":"OfficeEmployee","isActive":true,"companyScope":"DEFAULT","departmentId":"GENERAL","resetPassword":"Original-Pass-2026","permissionGrants":[]}));
    let (worker, _) = fixture
        .client()
        .login("password-self".into(), "Original-Pass-2026".into())
        .unwrap();
    let (other_session, _) = fixture
        .client()
        .login("password-self".into(), "Original-Pass-2026".into())
        .unwrap();
    worker.assert_rejected(LIST_USERS, &[], None, 403);
    for body in [
        json!({"currentPassword":"wrong","newPassword":"Changed-Pass-2026"}),
        json!({"currentPassword":"Original-Pass-2026","newPassword":"short"}),
        json!({"currentPassword":"Original-Pass-2026","newPassword":"Original-Pass-2026"}),
        json!({"newPassword":"Changed-Pass-2026"}),
    ] {
        worker.assert_rejected(CHANGE_OWN_PASSWORD, &[], Some(body), 400);
        let current: Value = worker.json(GET_CURRENT_USER, &[], &[], None).unwrap();
        assert_eq!(current["id"], account["user"]["id"]);
    }
    let changed: Value = worker
        .json(
            CHANGE_OWN_PASSWORD,
            &[],
            &[],
            Some(json!({"currentPassword":"Original-Pass-2026","newPassword":"Changed-Pass-2026"})),
        )
        .unwrap();
    assert_eq!(changed, json!({"success":true}));
    for expired in [&worker, &other_session] {
        expired.assert_rejected(GET_CURRENT_USER, &[], None, 401);
    }
    assert_eq!(
        fixture
            .client()
            .login("password-self".into(), "Original-Pass-2026".into())
            .err()
            .unwrap()
            .status,
        Some(401)
    );
    let (updated, _) = fixture
        .client()
        .login("password-self".into(), "Changed-Pass-2026".into())
        .unwrap();
    updated.assert_rejected(LIST_USERS, &[], None, 403);
    let users: Value = fixture.client().json(LIST_USERS, &[], &[], None).unwrap();
    let saved = users["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|user| user["id"] == account["user"]["id"])
        .unwrap();
    for key in [
        "role",
        "companyScope",
        "departmentId",
        "permissionGrants",
        "permissionTemplateId",
        "permissionSchemeVersion",
    ] {
        assert_eq!(
            saved[key], account["user"][key],
            "Password changes preserve {key}"
        );
    }
}

#[test]
fn desktop_initial_empty_password_can_be_changed_in_every_edition() {
    for edition in ["Full", "Document", "Sales"] {
        let fixture = native_fixture::Fixture::edition(edition);
        fixture.request(
            CHANGE_OWN_PASSWORD,
            None,
            Some(json!({"currentPassword":"","newPassword":"新的登录密码一二三四"})),
        );
        fixture
            .client()
            .assert_rejected(GET_CURRENT_USER, &[], None, 401);
        let (_, user) = fixture
            .client()
            .login("admin".into(), "新的登录密码一二三四".into())
            .unwrap();
        assert_eq!(user.capabilities.product_edition, edition);
    }
}
