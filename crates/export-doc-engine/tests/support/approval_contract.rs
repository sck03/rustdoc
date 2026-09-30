//! The same configured workflow is exercised on SQLite and PostgreSQL.
use super::oa_contract::{call, operation};
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
    call(service, token, SAVE_OA_APPROVAL_SETTINGS, 0, Some(value)).unwrap()
}
pub fn exercise(service: &NativeService, root_admin: &str) {
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
    let local = call(service, admin, GET_CURRENT_USER, 0, None).unwrap()["capabilities"]["usesOfficeRegister"]
        == true;
    let mut policy = call(service, admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    assert_eq!(policy["versionNumber"], 0);
    let expense_rule = policy["rules"]
        .as_array()
        .unwrap()
        .iter()
        .position(|r| r["kind"] == "expense")
        .unwrap();
    policy["rules"][expense_rule]["mode"] = json!("Named");
    policy["rules"][expense_rule]["approverUserIds"] = json!([users[2]["id"], users[3]["id"]]);
    let now = chrono::Utc::now();
    policy["delegations"] = json!([{"key":"first-delegation","principalUserId":users[2]["id"],"delegateUserId":users[4]["id"],"startsAt":(now-chrono::Duration::hours(1)).to_rfc3339(),"endsAt":(now+chrono::Duration::days(1)).to_rfc3339(),"isActive":true}]);
    policy = settings(service, admin, policy);
    let mut overlapping = policy.clone();
    overlapping["expectedVersion"] = policy["versionNumber"].clone();
    let mut duplicate = overlapping["delegations"][0].clone();
    duplicate["key"] = json!("overlapping");
    overlapping["delegations"]
        .as_array_mut()
        .unwrap()
        .push(duplicate);
    assert_eq!(
        call(
            service,
            admin,
            SAVE_OA_APPROVAL_SETTINGS,
            0,
            Some(overlapping)
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    assert_eq!(
        call(service, outsider, GET_OA_APPROVAL_SETTINGS, 0, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let create = || {
        let mut row=call(service,applicant,operation("expense","create"),0,Some(json!({"requestKey":nonce().unwrap(),"employeeId":if local {employees[0].clone()} else {Value::Null},"title":"逐级报销","reason":"验收完整流转","currency":"CNY","lines":[{"category":"Travel","spentOn":"2026-09-01","description":"交通","amount":"12.35"}]}))).unwrap();
        row = service
            .upload(
                operation("expense", "upload"),
                &[("id", row["id"].to_string())],
                json!({"expectedVersion":row["versionNumber"]}),
                "proof.pdf",
                b"%PDF-1.7\n%%EOF",
                applicant,
            )
            .unwrap();
        row
    };
    let mut row = act(service, applicant, "submit", &create());
    let id = row["id"].as_i64().unwrap();
    assert_eq!(
        call(
            service,
            first,
            operation("expense", "withdraw"),
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"审批人不能撤回他人申请"}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(row["approvalPlan"]["steps"].as_array().unwrap().len(), 2);
    assert_eq!(list(service, first, false)["totalCount"], 1);
    assert_eq!(list(service, second, false)["totalCount"], 0);
    assert_eq!(list(service, outsider, false)["totalCount"], 0);
    assert_eq!(list(service, delegate, false)["totalCount"], 1);
    assert_eq!(list(service, finance, true)["totalCount"], 0);
    // Proxy queues must use the principal's current account and data scope too.
    let own_scope = call(
        service,
        admin,
        CREATE_PERMISSION_TEMPLATE,
        0,
        Some(json!({
            "code":format!("OWN-{suffix}"),"name":"仅本人审批","isActive":true,
            "grants":[{"resourceKey":"office.expenses","action":"view","dataScope":"company"},
                      {"resourceKey":"office.expenses","action":"approve","dataScope":"own"}]
        })),
    )
    .unwrap();
    for change in [
        json!({"isActive":false}),
        json!({"role":"OfficeEmployee"}),
        json!({"permissionTemplateId":own_scope["id"]}),
    ] {
        let account = call(service, admin, LIST_USERS, 0, None).unwrap()["users"]
            .as_array()
            .unwrap()
            .iter()
            .find(|u| u["id"] == users[2]["id"])
            .unwrap()
            .clone();
        let mut update = account.clone();
        update["expectedVersion"] = account["versionNumber"].clone();
        for (key, value) in change.as_object().unwrap() {
            update[key] = value.clone();
        }
        let saved = call(
            service,
            admin,
            UPDATE_USER_ACCOUNT,
            users[2]["id"].as_i64().unwrap(),
            Some(update),
        )
        .unwrap();
        assert_eq!(list(service, delegate, false)["totalCount"], 0);
        assert_eq!(
            call(service, delegate, operation("expense", "get"), id, None).unwrap()["canReview"],
            false
        );
        assert_eq!(
            call(
                service,
                delegate,
                operation("expense", "approve"),
                id,
                Some(json!({"expectedVersion":row["versionNumber"],"note":"原审批人权限已失效"}))
            )
            .unwrap_err()
            .status,
            Some(403)
        );
        let mut restore = account;
        restore["expectedVersion"] = saved["user"]["versionNumber"].clone();
        call(
            service,
            admin,
            UPDATE_USER_ACCOUNT,
            users[2]["id"].as_i64().unwrap(),
            Some(restore),
        )
        .unwrap();
        assert_eq!(list(service, delegate, false)["totalCount"], 1);
    }
    // Account mutations revoke old sessions, including after restoring permissions.
    let refreshed_first = call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":users[2]["username"],"password":"Approval-Review-2026"})),
    )
    .unwrap()["accessToken"]
        .as_str()
        .unwrap()
        .to_owned();
    let first = &refreshed_first;
    assert_eq!(
        call(
            service,
            second,
            operation("expense", "approve"),
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"越级"}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    row = act(service, applicant, "remind", &row);
    assert_eq!(
        call(
            service,
            applicant,
            operation("expense", "remind"),
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"重复催办"}))
        )
        .unwrap_err()
        .status,
        Some(429)
    );
    assert_eq!(
        call(service, delegate, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        2
    );
    // Disable proxy immediately; the in-flight sequence survives a policy edit.
    policy["delegations"][0]["isActive"] = json!(false);
    policy["rules"][expense_rule]["mode"] = json!("Single");
    policy["rules"][expense_rule]["approverUserIds"] = json!([]);
    policy = settings(service, admin, policy);
    assert_eq!(
        call(service, delegate, operation("expense", "get"), id, None).unwrap()["canReview"],
        false
    );
    assert_eq!(
        call(
            service,
            delegate,
            operation("expense", "approve"),
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"已失效代理"}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    policy["delegations"][0]["isActive"] = json!(true);
    policy["delegations"][0]["startsAt"] = json!((now - chrono::Duration::hours(2)).to_rfc3339());
    policy["delegations"][0]["endsAt"] = json!((now - chrono::Duration::hours(1)).to_rfc3339());
    policy = settings(service, admin, policy);
    assert_eq!(
        call(service, delegate, operation("expense", "get"), id, None).unwrap()["canReview"],
        false
    );
    assert_eq!(list(service, delegate, false)["totalCount"], 0);
    policy["delegations"][0]["startsAt"] = json!((now - chrono::Duration::hours(1)).to_rfc3339());
    policy["delegations"][0]["endsAt"] = json!((now + chrono::Duration::days(1)).to_rfc3339());
    policy = settings(service, admin, policy);
    row = act(service, delegate, "approve", &row);
    assert_eq!(row["status"], "Pending");
    assert_eq!(
        row["approvalPlan"]["steps"][0]["actedByUserId"],
        users[4]["id"]
    );
    assert_eq!(
        row["approvalPlan"]["steps"][0]["delegationKey"],
        "first-delegation"
    );
    assert_eq!(list(service, finance, true)["totalCount"], 0);
    assert_eq!(
        call(service, finance, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        0
    );
    assert_eq!(list(service, second, false)["totalCount"], 1);
    row = act(service, second, "approve", &row);
    assert_eq!(row["status"], "Approved");
    assert_eq!(list(service, finance, true)["totalCount"], 1);
    assert_eq!(
        call(service, finance, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        1
    );
    row = act(service, finance, "complete", &row);
    assert_eq!(row["status"], "HandedOff");
    let history = call(
        service,
        applicant,
        operation("expense", "history"),
        id,
        None,
    )
    .unwrap();
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["action"] == "approve-step"
                && e["approvalPlan"]["steps"][0]["delegationKey"] == "first-delegation")
    );
    // Default single-step still works for the next submission.
    let single = act(service, applicant, "submit", &create());
    assert_eq!(
        act(service, first, "approve", &single)["status"],
        "Approved"
    );
    // The organisation route resolves the leaf manager then its parent manager.
    policy["rules"][expense_rule]["mode"] = json!("DepartmentChain");
    policy = settings(service, admin, policy);
    let chain = act(service, applicant, "submit", &create());
    assert_eq!(
        chain["approvalPlan"]["steps"][0]["approverUserId"],
        users[2]["id"]
    );
    assert_eq!(
        chain["approvalPlan"]["steps"][1]["approverUserId"],
        users[3]["id"]
    );
    assert!(
        call(
            service,
            admin,
            GET_PERSONNEL_CLEARANCE,
            employees[1].as_i64().unwrap(),
            None
        )
        .unwrap()["approvalCount"]
            .as_i64()
            .unwrap()
            > 0
    );
    let rejected = act(service, first, "reject", &chain);
    assert_eq!(rejected["status"], "Rejected");
    let resubmitted = act(service, applicant, "submit", &rejected);
    let first_done = act(service, first, "approve", &resubmitted);
    let approved = act(service, second, "approve", &first_done);
    let notice = call(service, finance, LIST_NOTIFICATIONS, 0, None).unwrap()["items"][0]["id"]
        .as_i64()
        .unwrap();
    act(service, first, "void", &approved);
    assert_eq!(
        call(service, finance, READ_NOTIFICATION, notice, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    // Company admins cannot access another company's workflow configuration.
    assert_eq!(
        call(service, root_admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap()["versionNumber"],
        0
    );
    assert_eq!(policy["rules"][expense_rule]["mode"], "DepartmentChain");
    let mut root_department = departments[0].clone();
    root_department["expectedVersion"] = root_department["versionNumber"].clone();
    root_department["managerEmployeeId"] = Value::Null;
    service
        .dispatch(
            UPDATE_ORGANIZATION_DEPARTMENT,
            &[("code", root.clone())],
            &[],
            Some(root_department),
            admin,
        )
        .unwrap();
    let draft = create();
    assert_eq!(
        call(
            service,
            applicant,
            operation("expense", "submit"),
            draft["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":draft["versionNumber"],"note":"缺少上级负责人"}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    assert_eq!(
        call(
            service,
            applicant,
            operation("expense", "get"),
            draft["id"].as_i64().unwrap(),
            None
        )
        .unwrap()["status"],
        "Draft"
    );
}
