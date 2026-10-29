//! The same configured workflow is exercised on SQLite and PostgreSQL.
use super::oa_contract::{call, operation, workflow};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn act(service: &NativeService, token: &str, action: &str, row: &Value) -> Value {
    call(
        service,
        token,
        operation("expense", action),
        row["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":row["versionNumber"],"note":"审批流程验收"})),
    )
    .unwrap()
}
fn list(service: &NativeService, token: &str, finance: bool) -> Value {
    serde_json::from_slice(
        &service
            .dispatch(
                operation("expense", "list"),
                &[],
                &[
                    ("mineOnly", "false".into()),
                    (
                        if finance {
                            "financeOnly"
                        } else {
                            "approvalsOnly"
                        },
                        "true".into(),
                    ),
                ],
                None,
                token,
            )
            .unwrap(),
    )
    .unwrap()
}
fn settings(service: &NativeService, token: &str, mut value: Value) -> Value {
    value["expectedVersion"] = value["versionNumber"].clone();
    let saved = call(service, token, SAVE_OA_APPROVAL_SETTINGS, 0, Some(value)).unwrap();
    call(
        service,
        token,
        PUBLISH_OA_DEFINITION,
        0,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"expense"})),
    )
    .unwrap()
}
pub fn exercise(service: &NativeService, root_admin: &str) {
    let root_policy = call(service, root_admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    let suffix = &nonce().unwrap()[..8];
    let company = format!("FLOW-{suffix}");
    let root = format!("ROOT-{suffix}");
    let leaf = format!("LEAF-{suffix}");
    call(
        service,
        root_admin,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"审批验收公司","isActive":true})),
    )
    .unwrap();
    let mut departments = vec![];
    for (code, parent) in [(&root, None), (&leaf, Some(&root))] {
        departments.push(call(service,root_admin,CREATE_ORGANIZATION_DEPARTMENT,0,Some(json!({"code":code,"companyCode":company,"name":code,"parentCode":parent,"isActive":true}))).unwrap());
    }
    let mut users = vec![];
    let mut tokens = vec![];
    for (name, role, department) in [
        ("admin", "Admin", &root),
        ("applicant", "OfficeEmployee", &leaf),
        ("first", "OfficeManager", &leaf),
        ("second", "OfficeManager", &root),
        ("delegate", "OfficeManager", &leaf),
        ("outsider", "OfficeManager", &leaf),
        ("finance", "Finance", &root),
    ] {
        let username = format!("{name}-{suffix}");
        let result=call(service,root_admin,CREATE_USER_ACCOUNT,0,Some(json!({"username":username,"fullName":name,"role":role,"companyScope":company,"departmentId":department,"isActive":true,"resetPassword":"Approval-Review-2026"}))).unwrap();
        users.push(result["user"].clone());
        tokens.push(
            call(
                service,
                "",
                LOGIN,
                0,
                Some(json!({"username":username,"password":"Approval-Review-2026"})),
            )
            .unwrap()["accessToken"]
                .as_str()
                .unwrap()
                .to_owned(),
        );
    }
    let admin = &tokens[0];
    let mut employees = vec![];
    for index in [1, 2, 3] {
        let person=call(service,admin,CREATE_PERSONNEL,0,Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("FLOW-{index}-{suffix}"),"departmentId":users[index]["departmentId"],"jobTitle":"流程岗位","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":users[index]["fullName"]}}))).unwrap();
        call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":users[index]["id"],"expectedAccountVersion":users[index]["versionNumber"]}))).unwrap();
        employees.push(person["employee"]["id"].clone());
    }
    // Linking personnel changes account versions, so use fresh sessions.
    for index in 1..=3 {
        tokens[index] = call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":users[index]["username"],"password":"Approval-Review-2026"})),
        )
        .unwrap()["accessToken"]
            .as_str()
            .unwrap()
            .into();
    }
    let [admin, applicant, first, second, delegate, outsider, finance]: [&String; 7] =
        std::array::from_fn(|i| &tokens[i]);
    for (index, person) in [(0, &employees[2]), (1, &employees[1])] {
        let department = &mut departments[index];
        department["expectedVersion"] = department["versionNumber"].clone();
        department["managerEmployeeId"] = person.clone();
        *department = serde_json::from_slice(
            &service
                .dispatch(
                    UPDATE_ORGANIZATION_DEPARTMENT,
                    &[("code", department["code"].as_str().unwrap().into())],
                    &[],
                    Some(department.clone()),
                    admin,
                )
                .unwrap(),
        )
        .unwrap();
    }

    let mut policy = call(service, admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    let expense = policy["rules"]
        .as_array()
        .unwrap()
        .iter()
        .position(|r| r["kind"] == "expense")
        .unwrap();
    policy["rules"][expense]["stages"] = json!([
        workflow::node("department", &[], "DepartmentManager", json!([])),
        workflow::node("review", &["department"], "Named", json!([users[3]["id"]]))
    ]);
    policy["delegations"] = json!([{"key":"leave-agent","principalUserId":users[2]["id"],"delegateUserId":users[4]["id"],
        "startsAt":(chrono::Utc::now()-chrono::Duration::hours(1)).to_rfc3339(),"endsAt":(chrono::Utc::now()+chrono::Duration::days(1)).to_rfc3339(),"isActive":true}]);
    policy = settings(service, admin, policy);
    let create = || {
        let row=call(service,applicant,operation("expense","create"),0,Some(json!({"requestKey":nonce().unwrap(),"title":"报销流程验收","reason":"凭证核对","currency":"CNY",
            "lines":[{"category":"Travel","spentOn":"2026-09-01","description":"交通","amount":"12.35"}]}))).unwrap();
        service
            .upload(
                operation("expense", "upload"),
                &[("id", row["id"].to_string())],
                json!({"expectedVersion":row["versionNumber"]}),
                "proof.pdf",
                b"%PDF-1.7\n%%EOF",
                applicant,
            )
            .unwrap()
    };
    let mut row = act(service, applicant, "submit", &create());
    assert_eq!(row["approvalPlan"]["policyVersion"], 1);
    assert_eq!(list(service, first, false)["totalCount"], 1);
    assert_eq!(list(service, delegate, false)["totalCount"], 1);
    assert_eq!(list(service, second, false)["totalCount"], 0);
    assert_eq!(list(service, outsider, false)["totalCount"], 0);
    for token in [applicant, outsider, second] {
        assert_eq!(call(service,token,operation("expense","approve"),row["id"].as_i64().unwrap(),Some(json!({"expectedVersion":row["versionNumber"],"taskKey":row["approvalPlan"]["steps"][0]["taskKey"],"note":"越权"}))).unwrap_err().status,Some(403));
    }
    row = act(service, applicant, "remind", &row);
    assert_eq!(
        call(
            service,
            applicant,
            operation("expense", "remind"),
            row["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":row["versionNumber"],"note":"频繁催办"}))
        )
        .unwrap_err()
        .status,
        Some(429)
    );
    // A new publication does not mutate assignments or versions of in-flight requests.
    policy["rules"][expense]["stages"] = json!([workflow::node(
        "review",
        &[],
        "Named",
        json!([users[3]["id"]])
    )]);
    policy = settings(service, admin, policy);
    assert_eq!(policy["publications"][0]["version"], 2);
    row = act(service, delegate, "approve", &row);
    assert_eq!(row["status"], "Pending");
    assert_eq!(row["approvalPlan"]["policyVersion"], 1);
    assert_eq!(
        row["approvalPlan"]["steps"][0]["actedByUserId"],
        users[4]["id"]
    );
    assert_eq!(
        row["approvalPlan"]["steps"][0]["delegationKey"],
        "leave-agent"
    );
    assert_eq!(list(service, second, false)["totalCount"], 1);
    row = act(service, second, "approve", &row);
    assert_eq!(row["status"], "Approved");
    assert_eq!(list(service, finance, true)["totalCount"], 1);
    assert_eq!(
        act(service, finance, "complete", &row)["status"],
        "HandedOff"
    );

    // Return allows a new frozen submission; rejection is terminal.
    let returned = act(
        service,
        second,
        "return",
        &act(service, applicant, "submit", &create()),
    );
    assert_eq!(returned["status"], "Returned");
    let resubmitted = act(service, applicant, "submit", &returned);
    assert_eq!(resubmitted["submissionNumber"], 2);
    let rejected = act(service, second, "reject", &resubmitted);
    assert_eq!(
        call(
            service,
            applicant,
            operation("expense", "submit"),
            rejected["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":rejected["versionNumber"],"note":"不能复活最终驳回"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );

    // Two parallel roots join before a repeated responsible person signs again.
    policy["rules"][expense]["stages"] = json!([
        workflow::node("a", &[], "Named", json!([users[2]["id"]])),
        workflow::node("b", &[], "Named", json!([users[3]["id"]])),
        workflow::node("join", &["a", "b"], "Named", json!([users[2]["id"]]))
    ]);
    policy = settings(service, admin, policy);
    let mut parallel = act(service, applicant, "submit", &create());
    assert_eq!(list(service, first, false)["totalCount"], 1);
    assert_eq!(list(service, second, false)["totalCount"], 1);
    parallel = act(service, first, "approve", &parallel);
    assert_eq!(list(service, first, false)["totalCount"], 0);
    parallel = act(service, second, "approve", &parallel);
    assert_eq!(list(service, first, false)["totalCount"], 1);
    parallel = act(service, first, "approve", &parallel);
    assert_eq!(parallel["status"], "Approved");

    let waiting = act(service, applicant, "submit", &create());
    let disabled = call(
        service,
        admin,
        DISABLE_OA_DEFINITION,
        0,
        Some(json!({"expectedVersion":policy["versionNumber"],"definitionKey":"expense"})),
    )
    .unwrap();
    assert_eq!(disabled["publications"][0]["enabled"], false);
    assert_eq!(
        call(
            service,
            applicant,
            operation("expense", "submit"),
            create()["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":2,"note":"停用流程"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let waiting = act(service, first, "approve", &waiting);
    let waiting = act(service, second, "approve", &waiting);
    act(service, first, "approve", &waiting);
    assert_eq!(
        call(service, root_admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap(),
        root_policy
    );
}
