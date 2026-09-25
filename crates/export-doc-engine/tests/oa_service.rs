#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
#[path = "support/oa_contract.rs"]
mod oa_contract;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

#[test]
fn sqlite_office_approvals_attachments_and_concurrency() {
    let mut fixture = native_fixture::Fixture::new();
    let employee = fixture.employee();
    fixture.client.take();
    let service = NativeService::open(fixture.paths.clone()).unwrap();
    let login = oa_contract::call(
        &service,
        "",
        LOGIN,
        0,
        Some(json!({"username":"admin","password":""})),
    )
    .unwrap();
    let token = login["accessToken"].as_str().unwrap();
    oa_contract::exercise(&service, token, token, employee["employee"]["id"].as_i64());
    let person_id = employee["employee"]["id"].as_i64().unwrap();
    let draft=oa_contract::call(&service,token,oa_contract::operation("general","create"),0,Some(json!({"requestKey":nonce().unwrap(),"employeeId":person_id,"title":"未结交接","reason":"IT事项","category":"IT"}))).unwrap();
    let clearance =
        oa_contract::call(&service, token, GET_PERSONNEL_CLEARANCE, person_id, None).unwrap();
    assert_eq!(clearance["approvalCount"], 1);
    assert_eq!(clearance["isClear"], false);
    let id = draft["id"].as_i64().unwrap();
    oa_contract::call(
        &service,
        token,
        oa_contract::operation("general", "cancel"),
        id,
        Some(json!({"expectedVersion":draft["versionNumber"],"note":"测试结清"})),
    )
    .unwrap();
    let clearance =
        oa_contract::call(&service, token, GET_PERSONNEL_CLEARANCE, person_id, None).unwrap();
    assert_eq!(clearance["approvalCount"], 0);
    service.close().unwrap();
    drop(service);
    let reopened = NativeService::open(fixture.paths.clone()).unwrap();
    let login = oa_contract::call(
        &reopened,
        "",
        LOGIN,
        0,
        Some(json!({"username":"admin","password":""})),
    )
    .unwrap();
    let record: Value = oa_contract::call(
        &reopened,
        login["accessToken"].as_str().unwrap(),
        oa_contract::operation("general", "get"),
        id,
        None,
    )
    .unwrap();
    assert_eq!(record["status"], "Cancelled");
    reopened.close().unwrap();
}

#[test]
fn request_scope_is_enforced_for_viewers_and_other_company_administrators() {
    let fixture = native_fixture::Fixture::new();
    let person = fixture.employee();
    let body = json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"部门内部申请","reason":"测试权限隔离","category":"Certificate"});
    let row = fixture.request(
        oa_contract::operation("general", "create"),
        None,
        Some(body),
    );
    fixture.create(CREATE_USER_ACCOUNT,json!({"username":"oa-viewer","fullName":"普通员工","role":"OfficeEmployee","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Office-Scope-2026"}));
    fixture.create(
        CREATE_ORGANIZATION_COMPANY,
        json!({"code":"OTHER","name":"另一个公司","isActive":true}),
    );
    fixture.create(
        CREATE_ORGANIZATION_DEPARTMENT,
        json!({"code":"OTHER-HR","name":"行政部","companyCode":"OTHER","isActive":true}),
    );
    fixture.create(CREATE_USER_ACCOUNT,json!({"username":"oa-other","fullName":"另一公司管理员","role":"Admin","companyScope":"OTHER","departmentId":"OTHER-HR","isActive":true,"resetPassword":"Office-Scope-2026"}));
    for username in ["oa-viewer", "oa-other"] {
        let client = fixture
            .client()
            .clone()
            .login(username.into(), "Office-Scope-2026".into())
            .unwrap()
            .0;
        let path = [("id", row["id"].to_string())];
        assert_eq!(
            client
                .json::<Value>(oa_contract::operation("general", "get"), &path, &[], None)
                .unwrap_err()
                .status,
            Some(403)
        );
        let page: Value = client
            .json(
                oa_contract::operation("general", "list"),
                &[],
                &[("mineOnly", "false".into())],
                None,
            )
            .unwrap();
        assert_eq!(page["totalCount"], 0);
        assert_eq!(
            client
                .upload(
                    oa_contract::operation("general", "upload"),
                    &path,
                    json!({"expectedVersion":row["versionNumber"]}),
                    "cross.pdf",
                    b"%PDF-1.7\n%%EOF"
                )
                .unwrap_err()
                .status,
            Some(403)
        );
    }
}
