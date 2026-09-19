#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{contracts, generated_api::*};
use serde_json::{Value, json};

#[test]
fn permission_catalog_preserves_builtin_rules_and_explains_effective_scopes() {
    let fixture = native_fixture::Fixture::new();
    let catalog = fixture.request(LIST_PERMISSION_TEMPLATES, None, None);
    export_doc_contracts::validation::response(LIST_PERMISSION_TEMPLATES.id, &catalog).unwrap();
    let admin = catalog["templates"]
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["code"] == "Admin")
        .unwrap();
    let result: Result<Value, _> = fixture.client().json(
        UPDATE_PERMISSION_TEMPLATE,
        &[("id", admin["id"].to_string())],
        &[],
        Some(json!({
            "id":admin["id"],"code":"Admin","name":"改名","description":"","isActive":false,
            "grants":[],"expectedVersion":admin["versionNumber"],
        })),
    );
    assert_eq!(result.unwrap_err().status, Some(409));
    let result: Result<Value, _> = fixture.client().json(
        DELETE_PERMISSION_TEMPLATE,
        &[("id", admin["id"].to_string())],
        &[("expectedVersion", admin["versionNumber"].to_string())],
        None,
    );
    assert_eq!(result.unwrap_err().status, Some(409));
    let request = json!({"code":"DOC-TEAM","name":"部门单证","description":"业务权限方案","isActive":true,"isSystem":true,
        "grants":[{"resourceKey":"document.invoices","action":"view","dataScope":"department"}]});
    let saved = fixture.create(CREATE_PERMISSION_TEMPLATE, request.clone());
    assert_eq!(saved["isSystem"], false);
    assert!(
        saved["effectiveGrants"]
            .as_array()
            .unwrap()
            .iter()
            .any(|grant| grant["source"] == "dependency"
                && grant["sourceResourceKey"] == "document.invoices"
                && grant["dataScope"] == "department")
    );
    export_doc_contracts::validation::response(CREATE_PERMISSION_TEMPLATE.id, &saved).unwrap();
    for grants in [
        json!([{"resourceKey":"system.users","action":"manage","dataScope":"all"}]),
        json!([{"resourceKey":"common.product-reference","action":"view","dataScope":"all"}]),
        json!([{"resourceKey":"document.invoices","action":"view","dataScope":"invalid"}]),
    ] {
        let mut bad = contracts::overlay(
            contracts::object(CREATE_PERMISSION_TEMPLATE.id, true),
            &request,
        );
        bad["code"] = json!("BAD");
        bad["grants"] = grants;
        assert_eq!(
            fixture
                .client()
                .json::<Value>(CREATE_PERMISSION_TEMPLATE, &[], &[], Some(bad))
                .unwrap_err()
                .status,
            Some(400)
        );
    }
    let user = fixture.create(CREATE_USER_ACCOUNT, json!({"username":"scheme-user","fullName":"权限验收","role":"User",
        "permissionTemplateId":saved["id"],"companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Scope-Test-2026"}));
    assert!(user["user"]["id"].as_i64().is_some());
    let user_list = fixture.request(LIST_USERS, None, None);
    let account = user_list["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["username"] == "scheme-user")
        .unwrap();
    assert_eq!(account["permissionTemplateName"], "部门单证");
    let result: Result<Value, _> = fixture.client().json(
        DELETE_PERMISSION_TEMPLATE,
        &[("id", saved["id"].to_string())],
        &[("expectedVersion", saved["versionNumber"].to_string())],
        None,
    );
    assert_eq!(result.unwrap_err().status, Some(409));
}
