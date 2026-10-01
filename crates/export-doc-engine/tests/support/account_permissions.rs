use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};
#[path = "payment_printing_contract.rs"]
mod payment_printing_contract;

fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let parameters = if id > 0 {
        vec![("id", id.to_string())]
    } else {
        vec![]
    };
    let body = body.map(|body| contracts::overlay(contracts::object(op.id, true), &body));
    Ok(serde_json::from_slice(&service.dispatch(op, &parameters, &[], body, token)?).unwrap())
}
fn login(service: &NativeService, username: &str) -> Value {
    call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":username,"password":"Account-Review-2026"})),
    )
    .unwrap()
}
fn token(login: &Value) -> &str {
    login["accessToken"].as_str().unwrap()
}
fn update(service: &NativeService, admin: &str, user: &Value, patch: Value) -> Value {
    let mut body = contracts::overlay(user.clone(), &patch);
    body["expectedVersion"] = user["versionNumber"].clone();
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        user["id"].as_i64().unwrap(),
        Some(body),
    )
    .unwrap()["user"]
        .clone()
}

/// The identical account/group contract runs on SQLite and real PostgreSQL 18.
pub fn exercise(service: &NativeService, admin: &str) {
    let suffix = nonce().unwrap();
    let catalog = call(service, admin, LIST_PERMISSION_TEMPLATES, 0, None).unwrap();
    let employee = catalog["templates"]
        .as_array()
        .unwrap()
        .iter()
        .find(|t| t["code"] == "OfficeEmployee")
        .unwrap()
        .clone();
    let template_id = employee["id"].as_i64().unwrap();
    let read = json!({"resourceKey":"office.people","action":"view","dataScope":"company"});
    let mut accounts = vec![];
    for index in 0..3 {
        let body = json!({"username":format!("profile-{suffix}-{index}"),"fullName":"权限回归员工","role":"OfficeEmployee","isActive":true,
            "companyScope":"DEFAULT","departmentId":"GENERAL","resetPassword":"Account-Review-2026",
            "permissionTemplateId":if index == 1 { json!(template_id) } else { Value::Null },
            "permissionGrants":if index == 2 { json!([read]) } else { Value::Null }});
        accounts.push(
            call(service, admin, CREATE_USER_ACCOUNT, 0, Some(body)).unwrap()["user"].clone(),
        );
    }
    let sessions: Vec<_> = accounts
        .iter()
        .map(|u| login(service, u["username"].as_str().unwrap()))
        .collect();
    for session in &sessions {
        assert_eq!(
            session["user"]["capabilities"]["canUseDocumentWorkspace"],
            false
        );
        assert_eq!(
            session["user"]["capabilities"]["canUseSalesWorkspace"],
            false
        );
        assert_eq!(
            call(service, token(session), LIST_INVOICES, 0, None)
                .unwrap_err()
                .status,
            Some(403)
        );
        assert_eq!(
            call(service, token(session), LIST_USERS, 0, None)
                .unwrap_err()
                .status,
            Some(403)
        );
        assert_eq!(
            call(service, token(session), GET_PERSONNEL, 1, None)
                .unwrap_err()
                .status,
            Some(403)
        );
    }
    payment_printing_contract::exercise(service, admin, &sessions[0], &sessions[1]);
    let mut group = employee.clone();
    group["expectedVersion"] = employee["versionNumber"].clone();
    group["grants"] = json!([read]);
    group["disabledModules"] = json!(["office.people"]);
    let saved = call(
        service,
        admin,
        UPDATE_PERMISSION_TEMPLATE,
        template_id,
        Some(group),
    )
    .unwrap();
    for session in &sessions[..2] {
        assert_eq!(
            call(service, token(session), GET_CURRENT_USER, 0, None)
                .unwrap_err()
                .status,
            Some(401)
        );
    }
    // A custom account does not inherit later changes from its role's group.
    call(service, token(&sessions[2]), LIST_PERSONNEL, 0, None).unwrap();
    let inherited = login(service, accounts[0]["username"].as_str().unwrap());
    assert_eq!(
        call(service, token(&inherited), LIST_PERSONNEL, 0, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert!(
        inherited["user"]["capabilities"]["enabledModules"]
            .as_array()
            .unwrap()
            .is_empty()
    );

    accounts[2] = update(
        service,
        admin,
        &accounts[2],
        json!({"disabledModules":["office.people"]}),
    );
    assert_eq!(
        call(service, token(&sessions[2]), GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let disabled = login(service, accounts[2]["username"].as_str().unwrap());
    assert_eq!(
        call(service, token(&disabled), LIST_PERSONNEL, 0, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    accounts[2] = update(
        service,
        admin,
        &accounts[2],
        json!({"permissionGrants":[],"disabledModules":[]}),
    );
    let empty = login(service, accounts[2]["username"].as_str().unwrap());
    assert!(
        empty["user"]["capabilities"]["permissions"]
            .as_array()
            .unwrap()
            .is_empty()
    );
    for patch in [
        json!({"disabledModules":["unknown.module"]}),
        json!({"permissionGrants":[{"resourceKey":"system.users","action":"manage","dataScope":"all"}]}),
        json!({"permissionGrants":[{"resourceKey":"office.people","action":"view","dataScope":"unknown"}]}),
    ] {
        let mut body = contracts::overlay(accounts[2].clone(), &patch);
        body["expectedVersion"] = accounts[2]["versionNumber"].clone();
        assert_eq!(
            call(
                service,
                admin,
                UPDATE_USER_ACCOUNT,
                accounts[2]["id"].as_i64().unwrap(),
                Some(body)
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    let mut restored = employee;
    restored["expectedVersion"] = saved["versionNumber"].clone();
    restored["grants"] = json!(
        restored["grants"]
            .as_array()
            .unwrap()
            .iter()
            .filter(|g| {
                catalog["resources"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .any(|r| r["key"] == g["resourceKey"] && r["isTechnical"] == false)
            })
            .cloned()
            .collect::<Vec<_>>()
    );
    call(
        service,
        admin,
        UPDATE_PERMISSION_TEMPLATE,
        template_id,
        Some(restored),
    )
    .unwrap();
    accounts[2] = update(
        service,
        admin,
        &accounts[2],
        json!({"permissionGrants":null}),
    );
    let restored_login = login(service, accounts[2]["username"].as_str().unwrap());
    call(service, token(&restored_login), LIST_PERSONNEL, 0, None).unwrap();
    assert!(
        restored_login["user"]["capabilities"]["enabledModules"]
            .as_array()
            .unwrap()
            .iter()
            .any(|m| m == "office.leave")
    );

    // A suspended group keeps its configuration but exposes no effective grants.
    let mut group = call(
        service,
        admin,
        CREATE_PERMISSION_TEMPLATE,
        0,
        Some(json!({
            "code":format!("suspended-{suffix}"),"name":"停用分组回归","isActive":true,
            "grants":[read],"disabledModules":[]
        })),
    )
    .unwrap();
    accounts[2] = update(
        service,
        admin,
        &accounts[2],
        json!({"permissionTemplateId":group["id"]}),
    );
    let active_group_login = login(service, accounts[2]["username"].as_str().unwrap());
    group["expectedVersion"] = group["versionNumber"].clone();
    group["isActive"] = json!(false);
    let inactive = call(
        service,
        admin,
        UPDATE_PERMISSION_TEMPLATE,
        group["id"].as_i64().unwrap(),
        Some(group),
    )
    .unwrap();
    assert_eq!(inactive["effectiveGrants"], json!([]));
    assert_eq!(inactive["grants"], json!([read]));
    assert_eq!(
        call(
            service,
            token(&active_group_login),
            GET_CURRENT_USER,
            0,
            None
        )
        .unwrap_err()
        .status,
        Some(401)
    );
    let disabled_group_login = login(service, accounts[2]["username"].as_str().unwrap());
    assert_eq!(
        disabled_group_login["user"]["capabilities"]["permissions"],
        json!([])
    );

    // Administrator identity cannot be partly restricted by a selected employee group.
    let promoted = update(
        service,
        admin,
        &accounts[2],
        json!({"role":"Admin","permissionTemplateId":template_id,"permissionGrants":[],"disabledModules":["office.people"]}),
    );
    assert!(promoted["permissionTemplateId"].is_null());
    assert!(promoted["permissionGrants"].is_null());
    let administrator = login(service, promoted["username"].as_str().unwrap());
    assert!(
        administrator["user"]["capabilities"]["enabledModules"]
            .as_array()
            .unwrap()
            .iter()
            .any(|m| m == "document.invoices")
    );
    call(service, token(&administrator), LIST_USERS, 0, None).unwrap();
}
