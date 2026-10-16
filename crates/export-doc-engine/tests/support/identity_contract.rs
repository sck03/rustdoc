use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};

fn request(
    service: &NativeService,
    token: &str,
    op: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let body = body.map(|value| contracts::overlay(contracts::object(op.id, true), &value));
    let value =
        serde_json::from_slice(&service.dispatch(op, parameters, query, body, token)?).unwrap();
    export_doc_contracts::validation::response(op.id, &value).unwrap();
    Ok(value)
}

pub fn exercise(service: &NativeService, admin: &str) {
    let suffix = &nonce().unwrap()[..8];
    let department = format!("IDENTITY-{suffix}");
    let name = format!("同名人员{suffix}");
    let call = |op, parameters: &[(&str, String)], query: &[(&str, String)], body| {
        request(service, admin, op, parameters, query, body)
    };
    let create = |op, body| call(op, &[], &[], Some(body)).unwrap();
    let mut organization = create(
        CREATE_ORGANIZATION_DEPARTMENT,
        json!({"code":department,"companyCode":"DEFAULT","name":"同名人员测试部门","isActive":true}),
    );
    let mut people = Vec::new();
    let mut accounts = Vec::new();
    let mut tokens = Vec::new();
    for index in 0..2 {
        let person = create(
            CREATE_PERSONNEL,
            json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("ID-{suffix}-{index}"),"departmentId":department,"jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":false,"profile":{"fullName":name,"personalPhone":format!("private-{suffix}")}}),
        );
        if index == 0 {
            let mut disabled = organization.clone();
            disabled["expectedVersion"] = disabled["versionNumber"].clone();
            disabled["isActive"] = json!(false);
            assert_eq!(
                call(
                    UPDATE_ORGANIZATION_DEPARTMENT,
                    &[("code", department.clone())],
                    &[],
                    Some(disabled)
                )
                .unwrap_err()
                .status,
                Some(409),
                "unlinked employees also prevent department deactivation"
            );
        }
        let username = if index == 0 {
            format!("{}{suffix}", "同".repeat(70))
        } else {
            format!("namesake-{suffix}")
        };
        let mut account = create(CREATE_USER_ACCOUNT, json!({"username":username,"fullName":name,"role":"OfficeEmployee","departmentId":department,"companyScope":"DEFAULT","isActive":true,"resetPassword":"Identity-Contract-2026"}))["user"].clone();
        let linked = call(LINK_PERSONNEL_ACCOUNT, &[("id",person["employee"]["id"].to_string())], &[], Some(json!({"userId":account["id"],"expectedAccountVersion":account["versionNumber"],"expectedVersion":person["versionNumber"]}))).unwrap();
        let login = request(
            service,
            "",
            LOGIN,
            &[],
            &[],
            Some(json!({"username":username,"password":"Identity-Contract-2026"})),
        )
        .unwrap();
        assert_eq!(login["user"]["id"], account["id"]);
        assert_eq!(login["user"]["fullName"], name);
        tokens.push(login["accessToken"].as_str().unwrap().to_owned());
        account["versionNumber"] = linked["account"]["versionNumber"].clone();
        accounts.push(account);
        people.push(linked);
    }
    for page in 1..=2 {
        let found = call(
            LIST_PERSONNEL,
            &[],
            &[
                ("keyword", name.clone()),
                ("pageSize", "1".into()),
                ("pageNumber", page.to_string()),
            ],
            None,
        )
        .unwrap();
        assert_eq!(found["totalCount"], 2);
        assert_eq!(found["items"][0]["id"], people[page - 1]["employee"]["id"]);
        assert!(!found.to_string().contains("private-"));
    }
    assert_eq!(
        call(
            LIST_PERSONNEL,
            &[],
            &[("keyword", format!("private-{suffix}"))],
            None
        )
        .unwrap()["totalCount"],
        0
    );
    people[0] = call(UPDATE_PERSONNEL, &[("id",people[0]["employee"]["id"].to_string())], &[], Some(json!({"expectedVersion":people[0]["versionNumber"],"profile":people[0]["profile"],"employmentType":"FullTime","contractEndsOn":"2026-09-30"}))).unwrap();
    let attention = call(
        LIST_PERSONNEL,
        &[],
        &[("keyword", name.clone()), ("attentionOnly", "true".into())],
        None,
    )
    .unwrap();
    assert_eq!(attention["totalCount"], 1);
    assert_eq!(attention["items"][0]["id"], people[0]["employee"]["id"]);
    let mut profile = people[1]["profile"].clone();
    profile["identityValidUntil"] = json!("2026-09-30");
    people[1] = call(UPDATE_PERSONNEL, &[("id",people[1]["employee"]["id"].to_string())], &[], Some(json!({"expectedVersion":people[1]["versionNumber"],"profile":profile,"employmentType":"FullTime"}))).unwrap();
    let attention = call(
        LIST_PERSONNEL,
        &[],
        &[
            ("keyword", name.clone()),
            ("attentionOnly", "true".into()),
            ("pageSize", "1".into()),
            ("pageNumber", "2".into()),
        ],
        None,
    )
    .unwrap();
    assert_eq!(
        attention["totalCount"], 2,
        "identity expiry participates in the bounded attention projection"
    );
    assert_eq!(attention["items"][0]["id"], people[1]["employee"]["id"]);
    let mut disabled = accounts[0].clone();
    disabled["isActive"] = json!(false);
    disabled["expectedVersion"] = disabled["versionNumber"].clone();
    call(
        UPDATE_USER_ACCOUNT,
        &[("id", accounts[0]["id"].to_string())],
        &[],
        Some(disabled),
    )
    .unwrap();
    assert_eq!(
        request(service, &tokens[0], GET_CURRENT_USER, &[], &[], None)
            .unwrap_err()
            .status,
        Some(401)
    );
    assert_eq!(
        request(service, &tokens[1], GET_CURRENT_USER, &[], &[], None).unwrap()["id"],
        accounts[1]["id"]
    );
    let mut department_body = organization.clone();
    department_body["expectedVersion"] = organization["versionNumber"].clone();
    department_body["isActive"] = json!(false);
    assert_eq!(
        call(
            UPDATE_ORGANIZATION_DEPARTMENT,
            &[("code", department.clone())],
            &[],
            Some(department_body)
        )
        .unwrap_err()
        .status,
        Some(409),
        "active personnel cannot be stranded in a disabled department"
    );
    for invalid_id in [0, -1] {
        let mut body = organization.clone();
        body["expectedVersion"] = organization["versionNumber"].clone();
        body["managerEmployeeId"] = json!(invalid_id);
        assert_eq!(
            call(
                UPDATE_ORGANIZATION_DEPARTMENT,
                &[("code", department.clone())],
                &[],
                Some(body)
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    organization["expectedVersion"] = organization["versionNumber"].clone();
    organization["managerEmployeeId"] = people[1]["employee"]["id"].clone();
    let saved = call(
        UPDATE_ORGANIZATION_DEPARTMENT,
        &[("code", department.clone())],
        &[],
        Some(organization),
    )
    .unwrap();
    assert_eq!(
        saved["managerEmployeeNumber"],
        people[1]["employee"]["employeeNumber"]
    );
    let directory = call(GET_ORGANIZATION_DIRECTORY, &[], &[], None).unwrap();
    assert_eq!(
        directory["departments"]
            .as_array()
            .unwrap()
            .iter()
            .find(|row| row["code"] == department)
            .unwrap()["managerEmployeeNumber"],
        people[1]["employee"]["employeeNumber"]
    );
    let user = request(service, &tokens[1], GET_CURRENT_USER, &[], &[], None).unwrap();
    let mut application = json!({"requestKey":nonce().unwrap(),"title":"同名身份核对","reason":"按编号关联本人","category":"Other","currency":"CNY"});
    if user["capabilities"]["usesOfficeRegister"] == true {
        application["employeeId"] = people[1]["employee"]["id"].clone();
    }
    let application = request(
        service,
        &tokens[1],
        CREATE_GENERAL_REQUEST,
        &[],
        &[],
        Some(application),
    )
    .unwrap();
    assert_eq!(
        application["employeeNumber"],
        people[1]["employee"]["employeeNumber"]
    );
    assert_eq!(application["employeeId"], people[1]["employee"]["id"]);
    call(DEPART_PERSONNEL, &[("id",people[0]["employee"]["id"].to_string())], &[], Some(json!({"expectedVersion":people[0]["versionNumber"],"effectiveDate":user["businessDate"],"note":"同名同事的申请不应阻止本人离职"}))).unwrap();
    let reader_name = format!("directory-{suffix}");
    create(
        CREATE_USER_ACCOUNT,
        json!({"username":reader_name,"fullName":"受限目录账号","role":"OfficeEmployee","departmentId":department,"companyScope":"DEFAULT","isActive":true,"resetPassword":"Identity-Contract-2026","permissionGrants":[{"resourceKey":"office.people","action":"view","dataScope":"company"},{"resourceKey":"office.people","action":"view-details","dataScope":"own"}]}),
    );
    let reader = request(
        service,
        "",
        LOGIN,
        &[],
        &[],
        Some(json!({"username":reader_name,"password":"Identity-Contract-2026"})),
    )
    .unwrap();
    let departed = [("keyword", name.clone()), ("status", "Departed".into())];
    assert_eq!(
        call(LIST_PERSONNEL, &[], &departed, None).unwrap()["totalCount"],
        1
    );
    assert_eq!(
        request(
            service,
            reader["accessToken"].as_str().unwrap(),
            LIST_PERSONNEL,
            &[],
            &departed,
            None
        )
        .unwrap()["totalCount"],
        0,
        "departed personnel require the matching private-record scope"
    );
    let duplicate = json!({"requestKey":nonce().unwrap(),"employeeNumber":people[0]["employee"]["employeeNumber"],"departmentId":department,"jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":false,"profile":{"fullName":"另一人"}});
    assert_eq!(
        call(CREATE_PERSONNEL, &[], &[], Some(duplicate))
            .unwrap_err()
            .status,
        Some(409)
    );
}
