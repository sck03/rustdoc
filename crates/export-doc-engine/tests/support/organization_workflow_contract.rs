//! One service contract runs unchanged against SQLite and a real PostgreSQL database.
use super::oa_contract::{call, operation, workflow};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn login(service: &NativeService, username: &Value) -> Value {
    call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":username,"password":"Organization-2026-Test"})),
    )
    .unwrap()
}
fn get(service: &NativeService, token: &str, operation: Operation, id: i64) -> Value {
    call(service, token, operation, id, None).unwrap()
}
fn act(service: &NativeService, token: &str, kind: &str, action: &str, row: &Value) -> Value {
    call(
        service,
        token,
        operation(kind, action),
        row["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":row["versionNumber"],"note":"组织审批合同验收"})),
    )
    .unwrap()
}
fn list(service: &NativeService, token: &str, kind: &str, extra: &[(&str, String)]) -> Value {
    let mut query = vec![("mineOnly", "false".into())];
    query.extend_from_slice(extra);
    serde_json::from_slice(
        &service
            .dispatch(operation(kind, "list"), &[], &query, None, token)
            .unwrap(),
    )
    .unwrap()
}
fn serial(kind: &str, stages: Value) -> Value {
    let nodes = stages
        .as_array()
        .unwrap()
        .iter()
        .enumerate()
        .map(|(i, s)| {
            let previous = format!("n{}", i.saturating_sub(1));
            let deps = if i == 0 {
                vec![]
            } else {
                vec![previous.as_str()]
            };
            let mut node = workflow::node(
                &format!("n{i}"),
                &deps,
                s["source"].as_str().unwrap(),
                s["approverUserIds"].clone(),
            );
            if s["decision"].is_string() {
                node["decision"] = s["decision"].clone();
            }
            node
        })
        .collect::<Vec<_>>();
    workflow::rule(
        kind,
        if kind == "personnel" { "Hire" } else { "" },
        json!(nodes),
    )
}

fn rule_key(kind: &str) -> String {
    if kind == "personnel" {
        "personnel:Hire".into()
    } else {
        kind.into()
    }
}
fn policy(service: &NativeService, token: &str, kind: &str, rule: Value) -> Value {
    let mut settings = get(service, token, GET_OA_APPROVAL_SETTINGS, 0);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    *settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["kind"] == kind)
        .unwrap() = rule;
    let key = rule_key(kind);
    let saved = call(service, token, SAVE_OA_APPROVAL_SETTINGS, 0, Some(settings)).unwrap();
    call(
        service,
        token,
        PUBLISH_OA_DEFINITION,
        0,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":key})),
    )
    .unwrap()
}

pub fn exercise(service: &NativeService, root: &str) {
    exercise_inner(service, root, false);
}

pub fn exercise_nodes(service: &NativeService, root: &str) {
    exercise_inner(service, root, true);
}

fn exercise_inner(service: &NativeService, root: &str, nodes: bool) {
    let suffix = &nonce().unwrap()[..8];
    let company = format!("ORG-{suffix}");
    let a = format!("A-{suffix}");
    let b = format!("B-{suffix}");
    let company_row = call(service,root,CREATE_ORGANIZATION_COMPANY,0,Some(json!({"code":company,"name":"组织闭环验收","isActive":true,"positions":[{"name":"组织协调专员","isActive":true}]}))).unwrap();
    for code in [&a, &b] {
        call(
            service,
            root,
            CREATE_ORGANIZATION_DEPARTMENT,
            0,
            Some(json!({"code":code,"companyCode":company,"name":code,"isActive":true})),
        )
        .unwrap();
    }
    let mut users = vec![];
    for (index, role) in [
        "Admin",
        "OfficeEmployee",
        "OfficeManager",
        "OfficeManager",
        "OfficeManager",
        "OfficeManager",
        "OfficeManager",
    ]
    .into_iter()
    .enumerate()
    {
        let mut body = json!({"username":format!("org-{index}-{suffix}"),"fullName":format!("任职人员{index}"),"role":role,"companyScope":company,"departmentId":if index == 1 {&a} else {&b},"isActive":true,"resetPassword":"Organization-2026-Test","oaAssignedOnly":index == 2 || index == 6});
        if index == 4 {
            body["permissionGrants"] = json!(
                [
                    "office.personnel-changes",
                    "office.leave",
                    "office.overtime",
                    "office.expenses",
                    "office.travel",
                    "office.purchase",
                    "office.general",
                    "office.rooms",
                    "office.supplies"
                ]
                .into_iter()
                .flat_map(|resource| ["view", "approve"].map(
                    |action| json!({"resourceKey":resource,"action":action,"dataScope":"company"})
                ))
                .collect::<Vec<_>>()
            );
        }
        let user = call(service, root, CREATE_USER_ACCOUNT, 0, Some(body)).unwrap();
        users.push(user["user"].clone());
    }
    let admin_login = login(service, &users[0]["username"]);
    let admin = admin_login["accessToken"].as_str().unwrap();
    let mut people = vec![Value::Null];
    for index in 1..users.len() {
        let person = call(service,admin,CREATE_PERSONNEL,0,Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("ORG-{index}-{suffix}"),"departmentId":users[index]["departmentId"],"jobTitle":if index == 4 {"总经理"} else {"组织协调专员"},"employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":users[index]["fullName"]}}))).unwrap();
        let person = call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":users[index]["id"],"expectedAccountVersion":users[index]["versionNumber"]}))).unwrap();
        people.push(person);
    }
    let mut directory = get(service, admin, LIST_USERS, 0);
    for user in &mut users {
        *user = directory["users"]
            .as_array()
            .unwrap()
            .iter()
            .find(|u| u["id"] == user["id"])
            .unwrap()
            .clone();
    }
    let company_save = json!({"code":company,"name":"组织闭环验收","isActive":true,"expectedVersion":company_row["versionNumber"],"executiveEmployeeId":people[4]["employee"]["id"],"positions":[{"name":"组织协调专员","isActive":true}]});
    service
        .dispatch(
            UPDATE_ORGANIZATION_COMPANY,
            &[("code", company.clone())],
            &[],
            Some(company_save),
            admin,
        )
        .unwrap();
    let worker_id = people[1]["employee"]["id"].as_i64().unwrap();
    let organization = json!({"managerEmployeeId":people[2]["employee"]["id"],"functionalManagerEmployeeIds":[people[3]["employee"]["id"]],"additionalAssignments":[{"departmentId":b,"jobTitle":"行政兼职","managerEmployeeId":people[2]["employee"]["id"],"functionalManagerEmployeeIds":[people[3]["employee"]["id"]]}]});
    people[1] = call(service,admin,TRANSFER_PERSONNEL,worker_id,Some(json!({"expectedVersion":people[1]["versionNumber"],"effectiveDate":"2026-10-01","departmentId":a,"jobTitle":"组织协调专员","organization":organization,"note":"建立兼职和矩阵汇报"}))).unwrap();
    assert_eq!(
        people[1]["organization"]["additionalAssignments"][0]["departmentId"],
        b
    );
    let options = get(service, admin, GET_PERSONNEL_OPTIONS, 0);
    assert_eq!(options["positions"][0]["name"], "组织协调专员");
    let loop_change = call(service,admin,TRANSFER_PERSONNEL,people[2]["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":people[2]["versionNumber"],"effectiveDate":"2026-10-01","departmentId":b,"jobTitle":"经理","organization":{"managerEmployeeId":worker_id},"note":"拒绝循环"}))).unwrap_err();
    assert_eq!(loop_change.status, Some(400));
    assert_eq!(
        get(
            service,
            admin,
            GET_PERSONNEL,
            people[2]["employee"]["id"].as_i64().unwrap()
        )["versionNumber"],
        people[2]["versionNumber"]
    );
    let clearance = get(
        service,
        admin,
        GET_PERSONNEL_CLEARANCE,
        people[2]["employee"]["id"].as_i64().unwrap(),
    );
    assert_eq!(clearance["reportingCount"], 1);
    assert_eq!(clearance["canDepart"], false);
    assert_eq!(
        get(
            service,
            admin,
            GET_PERSONNEL_CLEARANCE,
            people[4]["employee"]["id"].as_i64().unwrap()
        )["isCompanyExecutive"],
        true
    );
    let logins = users
        .iter()
        .map(|u| login(service, &u["username"]))
        .collect::<Vec<_>>();
    let tokens = logins
        .iter()
        .map(|l| l["accessToken"].as_str().unwrap())
        .collect::<Vec<_>>();
    assert_eq!(
        logins[1]["user"]["employee"]["organization"]["additionalAssignments"][0]["departmentId"],
        b
    );
    let create = |kind: &str, extra: Value| {
        let mut body = json!({"requestKey":nonce().unwrap(),"title":"组织闭环申请","reason":"分派可见与任职范围合同","departmentId":b,"category":"Other"});
        for (key, value) in extra.as_object().unwrap() {
            body[key] = value.clone();
        }
        call(service, tokens[1], operation(kind, "create"), 0, Some(body)).unwrap()
    };
    if nodes {
        // A manager who also owns a later responsibility signs each node explicitly.
        let department = get(service, admin, GET_ORGANIZATION_DIRECTORY, 0)["departments"]
            .as_array()
            .unwrap()
            .iter()
            .find(|row| row["code"] == b)
            .unwrap()
            .clone();
        let mut managed = department.clone();
        managed["managerEmployeeId"] = people[4]["employee"]["id"].clone();
        managed["expectedVersion"] = managed["versionNumber"].clone();
        service
            .dispatch(
                UPDATE_ORGANIZATION_DEPARTMENT,
                &[("code", b.clone())],
                &[],
                Some(managed),
                admin,
            )
            .unwrap();
        policy(
            service,
            admin,
            "general",
            serial(
                "general",
                json!([{"source":"DepartmentManager","approverUserIds":[]},{"source":"Named","approverUserIds":[users[3]["id"],users[4]["id"]]}]),
            ),
        );
        let dual_draft = create("general", json!({}));
        let preview = get(
            service,
            tokens[1],
            operation("general", "approval-preview"),
            dual_draft["id"].as_i64().unwrap(),
        );
        assert_eq!(
            preview["approvalPlan"]["steps"].as_array().unwrap().len(),
            3
        );
        assert_eq!(preview["approvalPlan"]["steps"][0]["stage"], 0);
        assert_eq!(preview["approvalRoutingNotes"].as_array().unwrap().len(), 0);
        assert_eq!(
            get(
                service,
                tokens[1],
                GET_GENERAL_REQUEST,
                dual_draft["id"].as_i64().unwrap()
            )["versionNumber"],
            dual_draft["versionNumber"]
        );
        let dual = act(service, tokens[1], "general", "submit", &dual_draft);
        assert_eq!(
            dual["approvalPlan"]["steps"],
            preview["approvalPlan"]["steps"]
        );
        let dual = act(service, tokens[4], "general", "approve", &dual);
        let dual = act(service, tokens[3], "general", "approve", &dual);
        assert_eq!(dual["status"], "Pending");
        assert_eq!(
            act(service, tokens[4], "general", "approve", &dual)["status"],
            "Approved"
        );
        let mut restored = department;
        restored["expectedVersion"] = json!(restored["versionNumber"].as_i64().unwrap() + 1);
        service
            .dispatch(
                UPDATE_ORGANIZATION_DEPARTMENT,
                &[("code", b.clone())],
                &[],
                Some(restored),
                admin,
            )
            .unwrap();

        // The submitter and applicant coincide; the leader's own signature is omitted.
        policy(
            service,
            admin,
            "general",
            serial(
                "general",
                json!([{"source":"Named","approverUserIds":[users[2]["id"],users[3]["id"]]}]),
            ),
        );
        let self_draft = call(service,tokens[2],CREATE_GENERAL_REQUEST,0,Some(json!({"requestKey":nonce().unwrap(),"departmentId":b,"title":"负责人本人申请","reason":"回避验证","category":"Other"}))).unwrap();
        let own = act(service, tokens[2], "general", "submit", &self_draft);
        assert_eq!(own["approvalPlan"]["steps"].as_array().unwrap().len(), 1);
        assert_eq!(
            own["approvalPlan"]["steps"][0]["approverUserId"],
            users[3]["id"]
        );
        assert_eq!(
            get(
                service,
                tokens[2],
                GET_GENERAL_REQUEST,
                own["id"].as_i64().unwrap()
            )["canReview"],
            false
        );
        act(service, tokens[3], "general", "approve", &own);

        policy(
            service,
            admin,
            "general",
            serial(
                "general",
                json!([{"source":"Named","decision":"Any","approverUserIds":[users[2]["id"],users[3]["id"]]},{"source":"CompanyExecutive","approverUserIds":[]}]),
            ),
        );
        let either = act(
            service,
            tokens[1],
            "general",
            "submit",
            &create("general", json!({})),
        );
        let either = act(service, tokens[3], "general", "approve", &either);
        assert_eq!(either["status"], "Pending");
        assert_eq!(either["approvalPlan"]["steps"][0]["status"], "Skipped");
        assert!(either["approvalPlan"]["steps"][0]["actedByUserId"].is_null());
        assert_eq!(
            get(
                service,
                tokens[2],
                GET_GENERAL_REQUEST,
                either["id"].as_i64().unwrap()
            )["canReview"],
            false
        );
        act(service, tokens[4], "general", "approve", &either);
        return;
    }
    let parallel = serial(
        "general",
        json!([{"source":"Named","approverUserIds":[users[2]["id"],users[3]["id"]]},{"source":"CompanyExecutive","approverUserIds":[]}]),
    );
    policy(service, admin, "general", parallel);
    let draft = create("general", json!({}));
    let draft = service
        .upload(
            operation("general", "upload"),
            &[("id", draft["id"].to_string())],
            json!({"expectedVersion":draft["versionNumber"]}),
            "assignment.pdf",
            b"%PDF-1.7\n%%EOF",
            tokens[1],
        )
        .unwrap();
    assert_eq!(draft["departmentId"], b);
    assert_eq!(list(service, tokens[2], "general", &[])["totalCount"], 0);
    let mut row = act(service, tokens[1], "general", "submit", &draft);
    assert_eq!(row["approvalPlan"]["steps"][0]["stage"], 0);
    assert_eq!(row["approvalPlan"]["steps"][1]["stage"], 0);
    assert_eq!(
        row["approvalPlan"]["steps"][2]["approverUserId"],
        users[4]["id"]
    );
    assert_eq!(
        list(
            service,
            tokens[2],
            "general",
            &[("approvalsOnly", "true".into())]
        )["totalCount"],
        1
    );
    assert_eq!(
        list(
            service,
            tokens[3],
            "general",
            &[("approvalsOnly", "true".into())]
        )["totalCount"],
        1
    );
    assert_eq!(list(service, tokens[6], "general", &[])["totalCount"], 0);
    let attachment = row["attachments"][0]["id"].to_string();
    assert_eq!(
        service
            .download_file(
                operation("general", "download"),
                &[
                    ("id", row["id"].to_string()),
                    ("attachmentId", attachment.clone())
                ],
                tokens[6]
            )
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    assert!(
        service
            .download_file(
                operation("general", "download"),
                &[("id", row["id"].to_string()), ("attachmentId", attachment)],
                tokens[2]
            )
            .is_ok()
    );
    assert_eq!(
        get(service, tokens[6], LIST_NOTIFICATIONS, 0)["totalCount"],
        0
    );
    assert!(
        get(service, tokens[2], LIST_NOTIFICATIONS, 0)["totalCount"]
            .as_i64()
            .unwrap()
            > 0
    );
    for action in ["get", "history"] {
        assert_eq!(
            call(
                service,
                tokens[6],
                operation("general", action),
                row["id"].as_i64().unwrap(),
                None
            )
            .unwrap_err()
            .status,
            Some(403)
        );
    }
    assert_eq!(
        get(
            service,
            tokens[4],
            operation("general", "get"),
            row["id"].as_i64().unwrap()
        )["canReview"],
        false
    );
    row = act(service, tokens[3], "general", "approve", &row);
    assert_eq!(row["status"], "Pending");
    assert_eq!(
        get(
            service,
            tokens[4],
            operation("general", "get"),
            row["id"].as_i64().unwrap()
        )["canReview"],
        false
    );
    // Remove future routing, then prove the submitted snapshot still protects its signer.
    policy(
        service,
        admin,
        "general",
        serial(
            "general",
            json!([{"source":"Named","approverUserIds":[users[4]["id"]]}]),
        ),
    );
    let mut revoke = users[2].clone();
    revoke["expectedVersion"] = revoke["versionNumber"].clone();
    revoke["permissionGrants"] = json!([]);
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_USER_ACCOUNT,
            users[2]["id"].as_i64().unwrap(),
            Some(revoke.clone())
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    row = call(service,admin,REASSIGN_GENERAL_REQUEST_APPROVAL,row["id"].as_i64().unwrap(),Some(json!({"expectedVersion":row["versionNumber"],"fromUserId":users[2]["id"],"toUserId":users[5]["id"],"note":"移交等待中的签署"}))).unwrap();
    assert_eq!(
        row["approvalPlan"]["steps"][1]["actedByUserId"],
        users[3]["id"]
    );
    assert_eq!(list(service, tokens[2], "general", &[])["totalCount"], 0);
    assert_eq!(
        get(service, tokens[2], LIST_NOTIFICATIONS, 0)["totalCount"],
        0
    );
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        users[2]["id"].as_i64().unwrap(),
        Some(revoke),
    )
    .unwrap();
    row = act(service, tokens[5], "general", "approve", &row);
    assert_eq!(row["status"], "Pending");
    row = act(service, tokens[4], "general", "approve", &row);
    assert_eq!(row["status"], "Approved");
    let history = get(
        service,
        admin,
        operation("general", "history"),
        row["id"].as_i64().unwrap(),
    );
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|event| event["action"] == "approval-reassign")
    );
    let mut executive = users[4].clone();
    executive["expectedVersion"] = executive["versionNumber"].clone();
    executive["oaAssignedOnly"] = json!(true);
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        users[4]["id"].as_i64().unwrap(),
        Some(executive),
    )
    .unwrap();
    // Currency-specific conditions skip only the budget node; final review remains.
    let mut budget = workflow::node(
        "budget",
        &[],
        "Named",
        json!([users[3]["id"], users[5]["id"]]),
    );
    budget["conditions"] = json!([{"field":"Currency","operator":"Equal","value":"CNY"},{"field":"Amount","operator":"GreaterOrEqual","value":"100"},{"field":"Amount","operator":"Less","value":"200"}]);
    policy(
        service,
        admin,
        "purchase",
        workflow::rule(
            "purchase",
            "",
            json!([
                budget,
                workflow::node("final", &["budget"], "CompanyExecutive", json!([]))
            ]),
        ),
    );
    for (amount, currency, matched) in [
        ("99.99", "CNY", false),
        ("100", "CNY", true),
        ("200", "CNY", false),
        ("100", "USD", false),
    ] {
        let draft = create(
            "purchase",
            json!({"currency":currency,"purchaseLines":[{"name":"办公用品","specification":"标准","quantity":"1","unit":"件","unitPrice":amount}]}),
        );
        let pending = act(service, tokens[1], "purchase", "submit", &draft);
        assert_eq!(pending["approvalPlan"]["nodes"][0]["skipped"], !matched);
        if matched {
            let rejected = act(service, tokens[3], "purchase", "reject", &pending);
            assert_eq!(rejected["status"], "Rejected");
            assert_eq!(
                get(
                    service,
                    tokens[5],
                    operation("purchase", "get"),
                    pending["id"].as_i64().unwrap()
                )["canReview"],
                false
            );
        } else {
            act(service, tokens[1], "purchase", "withdraw", &pending);
        }
    }
    // A template change must also preserve CEO obligations, transactionally.
    let template = call(service,admin,CREATE_PERMISSION_TEMPLATE,0,Some(json!({"code":format!("ceo-{suffix}"),"name":"CEO方案","isActive":true,"grants":logins[4]["user"]["capabilities"]["permissions"].as_array().unwrap().iter().filter(|g| g["resourceKey"].as_str().unwrap().starts_with("office.")).cloned().collect::<Vec<_>>()}))).unwrap();
    directory = get(service, admin, LIST_USERS, 0);
    let mut executive = directory["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["id"] == users[4]["id"])
        .unwrap()
        .clone();
    executive["permissionTemplateId"] = template["id"].clone();
    executive["expectedVersion"] = executive["versionNumber"].clone();
    executive["permissionGrants"] = Value::Null;
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        users[4]["id"].as_i64().unwrap(),
        Some(executive),
    )
    .unwrap();
    let mut broken = template.clone();
    broken["expectedVersion"] = broken["versionNumber"].clone();
    broken["grants"] = json!([]);
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_PERMISSION_TEMPLATE,
            template["id"].as_i64().unwrap(),
            Some(broken)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    // Resolve line and functional managers from the selected appointment, not job-title strings.
    let mut restore = get(service, admin, LIST_USERS, 0)["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["id"] == users[2]["id"])
        .unwrap()
        .clone();
    restore["expectedVersion"] = restore["versionNumber"].clone();
    restore["permissionGrants"] = Value::Null;
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        users[2]["id"].as_i64().unwrap(),
        Some(restore),
    )
    .unwrap();
    policy(
        service,
        admin,
        "general",
        serial(
            "general",
            json!([{"source":"LineManager","approverUserIds":[]},{"source":"FunctionalManagers","approverUserIds":[]},{"source":"CompanyExecutive","approverUserIds":[]}]),
        ),
    );
    let matrix = act(
        service,
        tokens[1],
        "general",
        "submit",
        &create("general", json!({})),
    );
    assert_eq!(
        matrix["approvalPlan"]["steps"]
            .as_array()
            .unwrap()
            .iter()
            .map(|s| s["approverUserId"].clone())
            .collect::<Vec<_>>(),
        vec![
            users[2]["id"].clone(),
            users[3]["id"].clone(),
            users[4]["id"].clone()
        ]
    );
    act(service, tokens[1], "general", "withdraw", &matrix);
    policy(
        service,
        admin,
        "personnel",
        serial(
            "personnel",
            json!([{"source":"LineManager","approverUserIds":[]}]),
        ),
    );
    let registration = export_doc_engine::contracts::overlay(
        export_doc_engine::contracts::object(CREATE_PERSONNEL.id, true),
        &json!({"requestKey":"nested-hire","employeeNumber":format!("AP-HIRE-{suffix}"),"departmentId":a,"jobTitle":"普通员工","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":true,"profile":{"fullName":"待入职人员"},"organization":{"managerEmployeeId":people[4]["employee"]["id"],"functionalManagerEmployeeIds":[],"additionalAssignments":[{"departmentId":b,"jobTitle":"兼职","managerEmployeeId":null,"functionalManagerEmployeeIds":[]}]}}),
    );
    let hire = call(service,admin,CREATE_PERSONNEL_CHANGE_REQUEST,0,Some(json!({"requestKey":nonce().unwrap(),"title":"矩阵任职入职审批","reason":"按拟入职关系选取负责人","personnelChange":{"action":"Hire","registration":registration}}))).unwrap();
    let pending = act(service, admin, "personnel", "submit", &hire);
    assert_eq!(
        pending["approvalPlan"]["steps"][0]["approverUserId"],
        users[4]["id"]
    );
    let executive = login(service, &users[4]["username"]);
    let hired = act(
        service,
        executive["accessToken"].as_str().unwrap(),
        "personnel",
        "approve",
        &pending,
    );
    assert_eq!(hired["status"], "Completed");
    membership_scope(service, admin, &users[6], &people[6], &users[1], &a, &b);
}

fn membership_scope(
    service: &NativeService,
    admin: &str,
    user: &Value,
    reader: &Value,
    worker_account: &Value,
    a: &str,
    b: &str,
) {
    let registration = json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("HIRE-{}",nonce().unwrap()),"departmentId":b,"jobTitle":"普通员工","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":"入职多任职合同"},"organization":{"managerEmployeeId":reader["employee"]["id"],"additionalAssignments":[{"departmentId":a,"jobTitle":"兼职"}]}});
    let hire = call(
        service,
        admin,
        CREATE_PERSONNEL,
        0,
        Some(registration.clone()),
    )
    .unwrap();
    assert_eq!(
        hire["organization"]["additionalAssignments"][0]["departmentId"],
        a
    );
    let mut invalid = registration;
    invalid["requestKey"] = json!(nonce().unwrap());
    invalid["employeeNumber"] = json!(format!("BAD-{}", nonce().unwrap()));
    invalid["organization"]["additionalAssignments"][0]["departmentId"] =
        json!("MISSING-DEPARTMENT");
    assert_eq!(
        call(service, admin, CREATE_PERSONNEL, 0, Some(invalid))
            .unwrap_err()
            .status,
        Some(400)
    );

    let mut account = get(service, admin, LIST_USERS, 0)["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|v| v["id"] == user["id"])
        .unwrap()
        .clone();
    account["expectedVersion"] = account["versionNumber"].clone();
    account["permissionGrants"] =
        json!([{"resourceKey":"office.general","action":"view","dataScope":"department"}]);
    account["oaAssignedOnly"] = json!(false);
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        user["id"].as_i64().unwrap(),
        Some(account),
    )
    .unwrap();
    let applicant = login(service, &worker_account["username"]);
    let body = json!({"requestKey":nonce().unwrap(),"departmentId":a,"title":"主职之外的部门数据","reason":"任职权限撤销合同","category":"Other"});
    let row = call(
        service,
        applicant["accessToken"].as_str().unwrap(),
        CREATE_GENERAL_REQUEST,
        0,
        Some(body),
    )
    .unwrap();
    let initial = login(service, &user["username"]);
    assert_eq!(
        call(
            service,
            initial["accessToken"].as_str().unwrap(),
            GET_GENERAL_REQUEST,
            row["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let id = reader["employee"]["id"].as_i64().unwrap();
    let moved = call(service,admin,TRANSFER_PERSONNEL,id,Some(json!({"expectedVersion":reader["versionNumber"],"effectiveDate":"2026-10-01","departmentId":b,"jobTitle":"部门阅览员","organization":{"additionalAssignments":[{"departmentId":a,"jobTitle":"跨部门阅览"}]},"note":"增加兼职范围"}))).unwrap();
    let expanded = login(service, &user["username"]);
    let token = expanded["accessToken"].as_str().unwrap();
    assert_eq!(
        get(
            service,
            token,
            GET_GENERAL_REQUEST,
            row["id"].as_i64().unwrap()
        )["id"],
        row["id"]
    );
    assert!(
        list(service, token, "general", &[])["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|v| v["id"] == row["id"])
    );
    call(service,admin,TRANSFER_PERSONNEL,id,Some(json!({"expectedVersion":moved["versionNumber"],"effectiveDate":"2026-10-02","departmentId":b,"jobTitle":"部门阅览员","organization":{},"note":"移除兼职并撤销范围"}))).unwrap();
    assert_eq!(
        call(
            service,
            token,
            GET_GENERAL_REQUEST,
            row["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(401)
    );
    let narrowed = login(service, &user["username"]);
    let token = narrowed["accessToken"].as_str().unwrap();
    assert_eq!(
        call(
            service,
            token,
            GET_GENERAL_REQUEST,
            row["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert!(
        !list(service, token, "general", &[])["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|v| v["id"] == row["id"])
    );
}
