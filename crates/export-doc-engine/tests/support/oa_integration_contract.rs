//! Regression scenarios shared unchanged by SQLite and PostgreSQL 18.
use super::oa_contract::{call, operation, workflow};
use super::organization_workflow_contract::{act, get, list, policy};
use export_doc_engine::{contracts, engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn account(service: &NativeService, admin: &str, id: &Value) -> Value {
    get(service, admin, LIST_USERS, 0)["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["id"] == *id)
        .unwrap()
        .clone()
}
fn update_account(
    service: &NativeService,
    admin: &str,
    id: &Value,
    patch: Value,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let mut body = contracts::overlay(account(service, admin, id), &patch);
    body["expectedVersion"] = body["versionNumber"].clone();
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        id.as_i64().unwrap(),
        Some(body),
    )
}
fn organization(
    service: &NativeService,
    admin: &str,
    op: Operation,
    id: i64,
    mut body: Value,
) -> Value {
    body = contracts::overlay(contracts::object(op.id, true), &body);
    serde_json::from_slice(
        &service
            .dispatch(op, &[("id", id.to_string())], &[], Some(body), admin)
            .unwrap(),
    )
    .unwrap()
}

pub fn exercise(service: &NativeService, root: &str) {
    let suffix = &nonce().unwrap()[..8];
    let company = format!("REG-{suffix}");
    let a = format!("RA-{suffix}");
    let b = format!("RB-{suffix}");
    call(
        service,
        root,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"OA集成回归","isActive":true})),
    )
    .unwrap();
    let mut departments = vec![];
    for code in [&a, &b] {
        departments.push(
            call(
                service,
                root,
                CREATE_ORGANIZATION_DEPARTMENT,
                0,
                Some(json!({"code":code,"companyCode":company,"name":code,"isActive":true})),
            )
            .unwrap(),
        );
    }
    let mut users = vec![];
    for i in 0..5 {
        let mut body = json!({"username":format!("reg-{i}-{suffix}"),"fullName":format!("集成人员{i}"),"role":if i == 0 {"Admin"} else {"OfficeEmployee"},"companyScope":company,"departmentId":if i == 1 {&a} else {&b},"isActive":true,"resetPassword":"Integration-2026-Test"});
        if i >= 2 {
            body["permissionGrants"] = json!(["view", "approve"].map(
                |action| json!({"resourceKey":"office.general","action":action,"dataScope":"own"})
            ));
        }
        users
            .push(call(service, root, CREATE_USER_ACCOUNT, 0, Some(body)).unwrap()["user"].clone());
    }
    let login = |i: usize| {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":users[i]["username"],"password":"Integration-2026-Test"})),
        )
        .unwrap()
    };
    let admin_login = login(0);
    let admin = admin_login["accessToken"].as_str().unwrap();
    let mut people = vec![Value::Null];
    for i in 1..4 {
        let person = call(service, admin, CREATE_PERSONNEL, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("REG-{i}-{suffix}"),"departmentId":users[i]["departmentId"],"jobTitle":"主职","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":users[i]["fullName"]}}))).unwrap();
        people.push(call(service, admin, LINK_PERSONNEL_ACCOUNT, person["employee"]["id"].as_i64().unwrap(), Some(json!({"expectedVersion":person["versionNumber"],"userId":users[i]["id"],"expectedAccountVersion":users[i]["versionNumber"]}))).unwrap());
    }
    let worker = people[1]["employee"]["id"].as_i64().unwrap();
    let reporting = json!({"managerEmployeeId":people[2]["employee"]["id"],"functionalManagerEmployeeIds":[],"additionalAssignments":[{"departmentId":b,"jobTitle":"兼职审批联络员","managerEmployeeId":people[2]["employee"]["id"],"functionalManagerEmployeeIds":[]}]});
    people[1] = call(service, admin, TRANSFER_PERSONNEL, worker, Some(json!({"expectedVersion":people[1]["versionNumber"],"effectiveDate":"2026-10-01","departmentId":a,"jobTitle":"主职","organization":reporting,"note":"建立兼职"}))).unwrap();
    for d in &mut departments {
        *d = organization(
            service,
            admin,
            UPDATE_ORGANIZATION_DEPARTMENT,
            d["id"].as_i64().unwrap(),
            json!({"code":d["code"],"companyCode":company,"name":d["name"],"isActive":true,"expectedVersion":d["versionNumber"],"managerEmployeeId":people[2]["employee"]["id"],"additionalLeaders":[{"employeeId":people[3]["employee"]["id"],"workflowKinds":["general"]}]}),
        );
        assert!(
            d["additionalLeaderNames"][people[3]["employee"]["id"].to_string()]
                .as_str()
                .unwrap()
                .contains("集成人员3")
        );
    }
    let sessions: Vec<_> = (1..5).map(login).collect();
    let applicant = sessions[0]["accessToken"].as_str().unwrap();
    let first = sessions[1]["accessToken"].as_str().unwrap();
    let second = sessions[2]["accessToken"].as_str().unwrap();
    let create = |department: &str| {
        call(service, applicant, operation("general","create"), 0, Some(json!({"requestKey":nonce().unwrap(),"departmentId":department,"title":"集成审批","reason":"通用回归","category":"Other"}))).unwrap()
    };
    policy(
        service,
        admin,
        "general",
        workflow::rule(
            "general",
            "",
            json!([workflow::node(
                "leaders",
                &[],
                "DepartmentLeaders",
                json!([])
            )]),
        ),
    );
    let pending = act(service, applicant, "general", "submit", &create(&a));
    assert_eq!(
        pending["approvalPlan"]["steps"].as_array().unwrap().len(),
        2
    );
    // Own scope still includes an explicitly assigned request from another department.
    for reviewer in [first, second] {
        assert_eq!(
            list(service, reviewer, "general", &[("pageSize", "1".into())])["totalCount"],
            1
        );
        assert_eq!(
            list(
                service,
                reviewer,
                "general",
                &[("approvalsOnly", "true".into())]
            )["totalCount"],
            1
        );
        let hub = get(service, reviewer, GET_OA_APPROVAL_HUB, 0);
        assert_eq!(
            hub["entries"]
                .as_array()
                .unwrap()
                .iter()
                .find(|e| e["kind"] == "general" && e["handling"] == false)
                .unwrap()["page"]["totalCount"],
            1
        );
    }
    act(service, applicant, "general", "withdraw", &pending);
    // Protect dynamic responsibilities even when there is no in-flight task.
    assert_eq!(update_account(service, admin, &users[2]["id"], json!({"permissionGrants":[{"resourceKey":"office.general","action":"view","dataScope":"own"}]})).unwrap_err().status, Some(409));
    let clearance = get(
        service,
        admin,
        GET_PERSONNEL_CLEARANCE,
        people[3]["employee"]["id"].as_i64().unwrap(),
    );
    assert_eq!(clearance["managedDepartments"].as_array().unwrap().len(), 2);
    assert_eq!(clearance["canDepart"], false);
    // Freeze the selected appointment's position rather than the primary position.
    let alternate = act(service, applicant, "general", "submit", &create(&b));
    let snapshot: Value = serde_json::from_slice(
        &service
            .dispatch(
                operation("general", "submission"),
                &[
                    ("id", alternate["id"].to_string()),
                    ("submissionId", alternate["submissionId"].to_string()),
                ],
                &[],
                None,
                applicant,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(snapshot["applicantJobTitle"], "兼职审批联络员");
    assert_eq!(snapshot["appointment"]["departmentId"], b);
    act(service, applicant, "general", "withdraw", &alternate);
    policy(
        service,
        admin,
        "general",
        workflow::rule(
            "general",
            "",
            json!([workflow::node("line", &[], "LineManager", json!([]))]),
        ),
    );
    let removal = call(service, admin, TRANSFER_PERSONNEL, worker, Some(json!({"expectedVersion":people[1]["versionNumber"],"effectiveDate":"2026-10-02","departmentId":a,"jobTitle":"主职","organization":{"managerEmployeeId":null,"functionalManagerEmployeeIds":[],"additionalAssignments":[]},"note":"不能删除已发布职责"}))).unwrap_err();
    assert_eq!(removal.status, Some(409));
    assert_eq!(
        get(service, admin, GET_PERSONNEL, worker)["versionNumber"],
        people[1]["versionNumber"]
    );
    // Independently reviewed nodes may not reuse an actual delegate's signature.
    let mut review = workflow::node("independent", &["first"], "Named", json!([users[3]["id"]]));
    review["repeatedApprover"] = json!("Reject");
    policy(
        service,
        admin,
        "general",
        workflow::rule(
            "general",
            "",
            json!([
                workflow::node("first", &[], "Named", json!([users[2]["id"]])),
                review
            ]),
        ),
    );
    let mut settings = get(service, admin, GET_OA_APPROVAL_SETTINGS, 0);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    settings["delegations"] = json!([{"key":"independent-proxy","principalUserId":users[3]["id"],"delegateUserId":users[2]["id"],"startsAt":(chrono::Utc::now()-chrono::Duration::hours(1)).to_rfc3339(),"endsAt":(chrono::Utc::now()+chrono::Duration::days(1)).to_rfc3339(),"isActive":true}]);
    call(service, admin, SAVE_OA_APPROVAL_SETTINGS, 0, Some(settings)).unwrap();
    let pending = act(service, applicant, "general", "submit", &create(&a));
    let next = act(service, first, "general", "approve", &pending);
    assert_eq!(
        list(
            service,
            first,
            "general",
            &[("approvalsOnly", "true".into()), ("pageSize", "1".into())]
        )["totalCount"],
        0
    );
    assert_eq!(
        get(
            service,
            first,
            operation("general", "get"),
            next["id"].as_i64().unwrap()
        )["canReview"],
        false
    );
    assert_eq!(
        list(
            service,
            second,
            "general",
            &[("approvalsOnly", "true".into())]
        )["totalCount"],
        1
    );
    act(service, applicant, "general", "withdraw", &next);
    // An unlinked named reviewer cannot leave the company while the definition is enabled.
    policy(
        service,
        admin,
        "general",
        workflow::rule(
            "general",
            "",
            json!([workflow::node(
                "named",
                &[],
                "Named",
                json!([users[4]["id"]])
            )]),
        ),
    );
    assert_eq!(
        update_account(
            service,
            root,
            &users[4]["id"],
            json!({"companyScope":"DEFAULT","departmentId":"GENERAL"})
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        account(service, root, &users[4]["id"])["companyScope"],
        company
    );
    ownership(service, root, &users[1]["id"]);
}

fn ownership(service: &NativeService, root: &str, target: &Value) {
    let source = get(service, root, GET_CURRENT_USER, 0)["id"].clone();
    call(service, root, CREATE_PAYMENT, 0, Some(contracts::overlay(contracts::object(CREATE_PAYMENT.id,true), &json!({"voucherNo":format!("REG-{}",nonce().unwrap()),"payeeName":"归属回归","paymentDate":"2026-10-02","cnyAmount":12.35})))).unwrap();
    call(
        service,
        root,
        CREATE_CRM_CUSTOMER,
        0,
        Some(contracts::overlay(
            contracts::object(CREATE_CRM_CUSTOMER.id, true),
            &json!({"name":"归属回归客户"}),
        )),
    )
    .unwrap();
    for mask in 1..8 {
        let summary = get(service, root, GET_SHARED_DATABASE_OWNERSHIP_SUMMARY, 0);
        let counts = summary["owners"]
            .as_array()
            .unwrap()
            .iter()
            .find(|o| o["userId"] == source)
            .unwrap();
        let body = json!({"fromUserId":source,"toUserId":target,"includeInvoices":mask&1!=0,"includePayments":mask&2!=0,"includeOtherBusinessData":mask&4!=0,"onlyUnassigned":false,"confirmationText":"TRANSFER OWNERSHIP"});
        let result = call(
            service,
            root,
            TRANSFER_SHARED_DATABASE_OWNERSHIP,
            0,
            Some(body.clone()),
        )
        .unwrap();
        for (bit, output, input) in [
            (1, "updatedInvoices", "invoiceCount"),
            (2, "updatedPayments", "paymentCount"),
            (4, "updatedOtherBusinessData", "otherBusinessDataCount"),
        ] {
            assert_eq!(
                result[output],
                if mask & bit != 0 {
                    counts[input].clone()
                } else {
                    json!(0)
                }
            );
        }
        let mut reverse = body;
        reverse["fromUserId"] = target.clone();
        reverse["toUserId"] = source.clone();
        call(
            service,
            root,
            TRANSFER_SHARED_DATABASE_OWNERSHIP,
            0,
            Some(reverse),
        )
        .unwrap();
    }
}
