use export_doc_engine::{
    api::ApiError, engine::NativeService, generated_api::*, invoice::InvoiceDraft, paths::nonce,
};
use serde_json::{Value, json};
#[path = "approval_fixture.rs"]
mod workflow;

fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    parameters: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, ApiError> {
    workflow::query(service, token, op, parameters, &[], body)
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
    let renamed = call(service, admin, UPDATE_ORGANIZATION_DEPARTMENT, &[("id",team["id"].to_string())], Some(json!({"code":department,"companyCode":company,"name":"部门按稳定编号更新","isActive":true,"expectedVersion":team["versionNumber"]}))).unwrap();
    assert_eq!(renamed["name"], "部门按稳定编号更新");
    let template = call(service, admin, CREATE_PERMISSION_TEMPLATE, &[], Some(json!({"code":format!("COMPANY-{suffix}"),"name":"按公司查看、本人编辑","isActive":true,"grants":[
        {"resourceKey":"document.invoices","action":"view","dataScope":"company"},
        {"resourceKey":"document.invoices","action":"operate","dataScope":"own"}
    ]}))).unwrap();
    let template = workflow::publish_scheme(service, admin, template);
    let mut accounts = vec![];
    let mut tokens = vec![];
    for (index, company, department) in [
        (0, company.as_str(), department.as_str()),
        (1, company.as_str(), department.as_str()),
        (2, "DEFAULT", "GENERAL"),
    ] {
        let username = format!("scope-{suffix}-{index}");
        let user = call(service, admin, CREATE_USER_ACCOUNT, &[], Some(json!({"username":username,"fullName":username,"role":"User","companyScope":company,"departmentId":department,"permissionTemplateId":template["id"],"permissionSchemeVersion":template["publishedVersion"],"isActive":true,"resetPassword":"Scope-Test-2026"}))).unwrap()["user"].clone();
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
    let mut writers = vec![];
    for (index, unit, department) in [
        (0, company.as_str(), department.as_str()),
        (1, "DEFAULT", "GENERAL"),
    ] {
        let username = format!("catalog-{suffix}-{index}");
        call(service, admin, CREATE_USER_ACCOUNT, &[], Some(json!({"username":username,"fullName":username,"role":"User","companyScope":unit,"departmentId":department,"isActive":true,"resetPassword":"Scope-Test-2026","permissionGrants":[{"resourceKey":"document.master-data","action":"view","dataScope":"company"},{"resourceKey":"document.master-data","action":"operate","dataScope":"company"},{"resourceKey":"document.payments","action":"view","dataScope":"company"},{"resourceKey":"document.payments","action":"operate","dataScope":"company"}]}))).unwrap();
        let login = call(
            service,
            "",
            LOGIN,
            &[],
            Some(json!({"username":username,"password":"Scope-Test-2026"})),
        )
        .unwrap();
        writers.push(login["accessToken"].as_str().unwrap().to_owned());
    }
    for (create, update, list, fields) in [
        (
            CREATE_CUSTOMER,
            UPDATE_CUSTOMER,
            LIST_CUSTOMERS_PAGE,
            json!({"customerNameEN":format!("Shared customer {suffix}"),"displayName":"共同客户"}),
        ),
        (
            CREATE_EXPORTER,
            UPDATE_EXPORTER,
            LIST_EXPORTERS_PAGE,
            json!({"exporterNameEN":format!("Shared exporter {suffix}"),"exporterNameCN":"共同出口商"}),
        ),
        (
            CREATE_PRODUCT,
            UPDATE_PRODUCT,
            LIST_PRODUCTS,
            json!({"productCode":format!("SHARED-{suffix}"),"nameEN":"Shared product","nameCN":"共同商品"}),
        ),
    ] {
        for writer in &writers {
            let body = export_doc_engine::contracts::overlay(
                export_doc_engine::contracts::object(create.id, true),
                &fields,
            );
            let row = call(service, writer, create, &[], Some(body.clone())).unwrap();
            assert_eq!(
                call(service, writer, create, &[], Some(body))
                    .unwrap_err()
                    .status,
                Some(409)
            );
            call(
                service,
                writer,
                update,
                &[("id", row["id"].to_string())],
                Some(row),
            )
            .unwrap();
            let visible = workflow::query(
                service,
                writer,
                list,
                &[],
                &[("keyword", suffix.to_owned())],
                None,
            )
            .unwrap();
            assert_eq!(visible["totalCount"], 1, "{}", list.id);
        }
    }

    let code = format!("  shared-{suffix}  ");
    for (token, expected) in [
        (admin, 2),
        (writers[0].as_str(), 1),
        (writers[1].as_str(), 1),
    ] {
        let visible = workflow::query(
            service,
            token,
            LIST_PRODUCTS,
            &[],
            &[("productCode", code.clone())],
            None,
        )
        .unwrap();
        assert_eq!(
            visible["totalCount"], expected,
            "exact product-code lookup follows authorization scope"
        );
    }

    let options = call(
        service,
        &tokens[0],
        LIST_CUSTOM_OPTIONS,
        &[("optionType", "PaymentMethod".into())],
        None,
    )
    .unwrap();
    assert!(
        options["options"]
            .as_array()
            .unwrap()
            .iter()
            .any(|value| value == "电汇")
    );
    let mut invoice = InvoiceDraft::demo("2026-09-16", &format!("SCOPE-{suffix}"))
        .build()
        .unwrap();
    invoice.company_scope = "DEFAULT".into();
    let created = call(
        service,
        &tokens[0],
        CREATE_INVOICE,
        &[],
        Some(json!(invoice)),
    )
    .unwrap();
    assert_eq!(created["isUpdate"], false);
    let invoice = created["invoice"].clone();
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
    cross_company_documents(service, admin, &writers, &invoice, &suffix);
    let invoice = call(service, &tokens[0], GET_INVOICE, &parameters, None).unwrap();
    let updated = call(
        service,
        &tokens[0],
        UPDATE_INVOICE,
        &parameters,
        Some(invoice.clone()),
    )
    .unwrap();
    assert_eq!(updated["isUpdate"], true);
    assert_eq!(updated["invoice"]["id"], invoice["id"]);
    let mut changed = accounts[0].clone();
    changed["expectedVersion"] = changed["versionNumber"].clone();
    changed["companyScope"] = json!("DEFAULT");
    let account_parameters = [("id", accounts[0]["id"].to_string())];
    let mut valid_destination = changed.clone();
    valid_destination["departmentId"] = json!("GENERAL");
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_USER_ACCOUNT,
            &account_parameters,
            Some(valid_destination)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        call(service, &tokens[0], GET_CURRENT_USER, &[], None).unwrap()["companyScope"],
        company
    );
    let transfer = call(
        service,
        admin,
        TRANSFER_SHARED_DATABASE_OWNERSHIP,
        &[],
        Some(json!({
            "fromUserId":accounts[0]["id"], "toUserId":accounts[1]["id"],
            "includeInvoices":true, "includePayments":false, "includeOtherBusinessData":false,
            "onlyUnassigned":false, "confirmationText":"TRANSFER OWNERSHIP"
        })),
    )
    .unwrap();
    assert_eq!(transfer["updatedInvoices"], 1);
    assert_eq!(transfer["updatedPayments"], 0);
    assert_eq!(transfer["updatedOtherBusinessData"], 0);
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
            &[("id", company_record["id"].to_string())],
            Some(json!({"expectedVersion":company_record["versionNumber"]}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
}

fn cross_company_documents(
    service: &NativeService,
    admin: &str,
    writers: &[String],
    invoice: &Value,
    suffix: &str,
) {
    let username = format!("group-documents-{suffix}");
    let grants: Vec<_> = ["document.invoices", "document.payments"]
        .into_iter()
        .flat_map(|resource| {
            ["view", "operate"].map(
                move |action| json!({"resourceKey":resource,"action":action,"dataScope":"all"}),
            )
        })
        .collect();
    call(service, admin, CREATE_USER_ACCOUNT, &[], Some(json!({"username":username,"fullName":"跨公司单证职责","role":"User","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Scope-Test-2026","permissionGrants":grants}))).unwrap();
    let login = call(
        service,
        "",
        LOGIN,
        &[],
        Some(json!({"username":username,"password":"Scope-Test-2026"})),
    )
    .unwrap();
    let token = login["accessToken"].as_str().unwrap();
    let path = [("id", invoice["id"].to_string())];
    assert_eq!(
        call(service, token, GET_INVOICE, &path, None).unwrap()["id"],
        invoice["id"]
    );
    let listed = workflow::query(
        service,
        token,
        LIST_INVOICES,
        &[],
        &[("keyword", format!("SCOPE-{suffix}"))],
        None,
    )
    .unwrap();
    assert_eq!(listed["totalCount"], 1);
    let mut changed = invoice.clone();
    changed["notes"] = json!("跨公司授权编辑仍保留原归属");
    let saved =
        call(service, token, UPDATE_INVOICE, &path, Some(changed)).unwrap()["invoice"].clone();
    assert_eq!(saved["companyScope"], invoice["companyScope"]);
    for (index, writer) in writers.iter().enumerate() {
        let body = json!({"voucherNo":format!("GROUP-{suffix}-{index}"),"payeeName":"跨公司付款验收","paymentDate":"2026-11-20","cnyAmount":"123.45"});
        let payment =
            call(service, writer, CREATE_PAYMENT, &[], Some(body)).unwrap()["payment"].clone();
        let path = [("id", payment["id"].to_string())];
        assert_eq!(
            call(service, token, GET_PAYMENT, &path, None).unwrap()["companyScope"],
            payment["companyScope"]
        );
        assert_eq!(
            call(service, &writers[1 - index], GET_PAYMENT, &path, None)
                .unwrap_err()
                .status,
            Some(403)
        );
        let mut changed = payment.clone();
        changed["notes"] = json!("经跨公司授权更新");
        assert_eq!(
            call(service, token, UPDATE_PAYMENT, &path, Some(changed)).unwrap()["payment"]["companyScope"],
            payment["companyScope"]
        );
    }
    for (viewer, total) in [
        (token, 2),
        (writers[0].as_str(), 1),
        (writers[1].as_str(), 1),
    ] {
        let listed = workflow::query(
            service,
            viewer,
            LIST_PAYMENTS,
            &[],
            &[("keyword", format!("GROUP-{suffix}"))],
            None,
        )
        .unwrap();
        assert_eq!(listed["totalCount"], total);
    }
}
