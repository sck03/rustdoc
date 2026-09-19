use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, invoice::InvoiceDraft,
    paths::nonce,
};
use serde_json::{Value, json};

fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    parameters: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let body = body.map(|value| contracts::overlay(contracts::object(op.id, true), &value));
    let value: Value =
        serde_json::from_slice(&service.dispatch(op, parameters, &[], body, token)?).unwrap();
    export_doc_contracts::validation::response(op.id, &value)
        .unwrap_or_else(|cause| panic!("{}: {cause}", op.id));
    Ok(value)
}

/// The same contract is exercised on SQLite and an isolated PostgreSQL 18.
pub fn exercise(service: &NativeService, admin: &str) {
    let suffix = nonce().unwrap()[..8].to_owned();
    let company = format!("OTHER-{suffix}");
    let department = format!("TEAM-{suffix}");
    let company_record = call(
        service,
        admin,
        CREATE_ORGANIZATION_COMPANY,
        &[],
        Some(json!({"code":company,"name":"第二公司","isActive":true})),
    )
    .unwrap();
    let team = call(
        service,
        admin,
        CREATE_ORGANIZATION_DEPARTMENT,
        &[],
        Some(
            json!({"code":department,"companyCode":company,"name":"第二公司部门","isActive":true}),
        ),
    )
    .unwrap();
    let renamed = call(service, admin, UPDATE_ORGANIZATION_DEPARTMENT, &[("code",department.clone())], Some(json!({"code":department,"companyCode":company,"name":"部门按代码更新","isActive":true,"expectedVersion":team["versionNumber"]}))).unwrap();
    assert_eq!(renamed["name"], "部门按代码更新");
    let template = call(service, admin, CREATE_PERMISSION_TEMPLATE, &[], Some(json!({"code":format!("COMPANY-{suffix}"),"name":"按公司查看、本人编辑","isActive":true,"grants":[
        {"resourceKey":"document.invoices","action":"view","dataScope":"company"},
        {"resourceKey":"document.invoices","action":"operate","dataScope":"own"}
    ]}))).unwrap();
    let mut accounts = vec![];
    let mut tokens = vec![];
    for (index, company, department) in [
        (0, company.as_str(), department.as_str()),
        (1, company.as_str(), department.as_str()),
        (2, "DEFAULT", "GENERAL"),
    ] {
        let username = format!("scope-{suffix}-{index}");
        let user = call(service, admin, CREATE_USER_ACCOUNT, &[], Some(json!({"username":username,"fullName":username,"role":"User","companyScope":company,"departmentId":department,"permissionTemplateId":template["id"],"isActive":true,"resetPassword":"Scope-Test-2026"}))).unwrap()["user"].clone();
        assert_eq!(user["companyScope"], company);
        assert_eq!(user["departmentId"], department);
        let login = call(
            service,
            "",
            LOGIN,
            &[],
            Some(json!({"username":username,"password":"Scope-Test-2026"})),
        )
        .unwrap();
        assert_eq!(login["user"]["companyScope"], company);
        accounts.push(user);
        tokens.push(login["accessToken"].as_str().unwrap().to_owned());
    }
    let mut invoice = InvoiceDraft::demo("2026-09-16", &format!("SCOPE-{suffix}"))
        .build()
        .unwrap();
    invoice.company_scope = "DEFAULT".into();
    let invoice = call(
        service,
        &tokens[0],
        CREATE_INVOICE,
        &[],
        Some(json!(invoice)),
    )
    .unwrap()["invoice"]
        .clone();
    assert_eq!(invoice["companyScope"], company);
    let parameters = [("id", invoice["id"].to_string())];
    call(service, &tokens[1], GET_INVOICE, &parameters, None).unwrap();
    assert_eq!(
        call(
            service,
            &tokens[1],
            UPDATE_INVOICE,
            &parameters,
            Some(invoice.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(service, &tokens[2], GET_INVOICE, &parameters, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let mut changed = accounts[0].clone();
    changed["expectedVersion"] = changed["versionNumber"].clone();
    changed["companyScope"] = json!("DEFAULT");
    let account_parameters = [("id", accounts[0]["id"].to_string())];
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_USER_ACCOUNT,
            &account_parameters,
            Some(changed.clone())
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    changed["departmentId"] = json!("GENERAL");
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        &account_parameters,
        Some(changed),
    )
    .unwrap();
    assert_eq!(
        call(service, &tokens[0], GET_CURRENT_USER, &[], None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let login = call(
        service,
        "",
        LOGIN,
        &[],
        Some(json!({"username":accounts[0]["username"],"password":"Scope-Test-2026"})),
    )
    .unwrap();
    assert_eq!(login["user"]["companyScope"], "DEFAULT");
    assert_eq!(
        call(
            service,
            login["accessToken"].as_str().unwrap(),
            GET_INVOICE,
            &parameters,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(service, admin, GET_INVOICE, &parameters, None).unwrap()["companyScope"],
        company
    );
    assert_eq!(
        call(
            service,
            admin,
            DELETE_ORGANIZATION_COMPANY,
            &[("code", company)],
            Some(json!({"expectedVersion":company_record["versionNumber"]}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
}
