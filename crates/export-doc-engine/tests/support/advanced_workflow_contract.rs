//! Business contracts shared by real SQLite and PostgreSQL execution.
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};
#[path = "approval_fixture.rs"]
mod fixture;
struct Actor {
    user: i64,
    person: i64,
    appointment: String,
    token: String,
}
fn actor(service: &NativeService, admin: &str, name: &str) -> Actor {
    let name = format!("{name}-{}", &nonce().unwrap()[..8]);
    let grants = export_doc_domain::permissions::catalog()
        .resources
        .iter()
        .filter(|r| r.workflow_resource)
        .flat_map(|r| {
            r.actions
                .iter()
                .map(|a| json!({"resourceKey":r.key,"action":a.key,"dataScope":"company"}))
        })
        .collect::<Vec<_>>();
    let user=fixture::request(service,admin,CREATE_USER_ACCOUNT,Some(json!({"username":name,"fullName":name,"role":"OfficeManager","companyScope":"DEFAULT","departmentId":"GENERAL","permissionGrants":grants,"isActive":true,"resetPassword":"Advanced-Test-2026"})))["user"].clone();
    let person = fixture::request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeNumber":name,"departmentId":"GENERAL","jobTitle":name,"hireDate":"2026-01-01","employmentType":"FullTime","profile":{"fullName":name}}),
        ),
    );
    let person=fixture::call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":user["id"],"expectedAccountVersion":user["versionNumber"]}))).unwrap();
    let token = fixture::request(
        service,
        "",
        LOGIN,
        Some(json!({"username":name,"password":"Advanced-Test-2026"})),
    )["accessToken"]
        .as_str()
        .unwrap()
        .into();
    Actor {
        user: user["id"].as_i64().unwrap(),
        person: person["employee"]["id"].as_i64().unwrap(),
        appointment: person["organization"]["primaryAssignmentId"]
            .as_str()
            .unwrap()
            .into(),
        token,
    }
}
fn publish(
    service: &NativeService,
    admin: &str,
    kind: &str,
    stages: Value,
    applicant: &Actor,
) -> Value {
    let project_id = stages
        .as_array()
        .into_iter()
        .flatten()
        .find(|s| s["source"] == "ProjectLeader")
        .map(|s| s["sourceId"].clone())
        .unwrap_or(Value::Null);
    let mut settings = fixture::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == kind)
        .unwrap()["stages"] = stages;
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let saved = fixture::request(service, admin, SAVE_OA_APPROVAL_SETTINGS, Some(settings));
    fixture::request(
        service,
        admin,
        PUBLISH_OA_DEFINITION,
        Some(
            json!({"expectedVersion":saved["versionNumber"],"definitionKey":kind,"commandKey":nonce().unwrap(),"samples":[{"employeeId":applicant.person,"appointmentId":applicant.appointment,"amount":"0","currency":"CNY","duration":"1","category":"Other","isReturnable":true,"projectId":project_id}]}),
        ),
    )
}
fn step(key: &str, kind: &str, phase: &str, deps: &[&str], user: i64) -> Value {
    let mut value = fixture::node(
        key,
        deps,
        "Named",
        if user == 0 { json!([]) } else { json!([user]) },
    );
    value["kind"] = json!(kind);
    value["phase"] = json!(phase);
    value
}
fn command(
    service: &NativeService,
    token: &str,
    op: Operation,
    row: &Value,
    mut body: Value,
) -> Value {
    body["expectedVersion"] = row["versionNumber"].clone();
    body["commandKey"] = json!(nonce().unwrap());
    fixture::call(
        service,
        token,
        op,
        row["id"].as_i64().unwrap(),
        fixture::decision_body(service, token, op, row["id"].as_i64().unwrap(), Some(body)),
    )
    .unwrap()
}
pub fn exercise(service: &NativeService, admin: &str) {
    let applicant = actor(service, admin, "高级申请人");
    let reviewer = actor(service, admin, "职责审批人");
    let handler = actor(service, admin, "实际办理人");
    let verifier = actor(service, admin, "独立验收人");
    let directory = fixture::request(service, admin, GET_ORGANIZATION_DIRECTORY, None);
    let unit = directory["companies"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["code"] == "DEFAULT")
        .unwrap()["id"]
        .to_string();
    let duty = fixture::request(
        service,
        admin,
        SAVE_RESPONSIBILITY,
        Some(
            json!({"unitId":unit,"code":format!("duty-{}",nonce().unwrap()),"name":"高级流程责任","workflowKinds":["general"],"assignments":[{"appointmentId":reviewer.appointment,"period":{"startsOn":"2026-01-01","endsOn":null}}],"isActive":true,"expectedVersion":0,"note":"有效职责"}),
        ),
    );
    let project = fixture::request(
        service,
        admin,
        SAVE_PROJECT,
        Some(
            json!({"unitId":unit,"code":format!("project-{}",nonce().unwrap()),"name":"有限审批项目","ownerAppointmentId":reviewer.appointment,"members":[{"appointmentId":applicant.appointment,"role":"Member","period":{"startsOn":"2026-01-01","endsOn":null}}],"isActive":true,"expectedVersion":0,"note":"有限成员"}),
        ),
    );
    let positions: Value = serde_json::from_slice(
        &service
            .dispatch(
                LIST_POSITIONS,
                &[],
                &[("unitId", unit.clone()), ("pageSize", "100".into())],
                None,
                admin,
            )
            .unwrap(),
    )
    .unwrap();
    let person = fixture::call(service, admin, GET_PERSONNEL, reviewer.person, None).unwrap();
    let position = positions["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|p| p["name"] == person["jobTitle"])
        .unwrap()["id"]
        .clone();
    let mut stages = vec![];
    for (key, source, id, deps) in [
        ("position", "Position", position, vec![]),
        (
            "duty",
            "Responsibility",
            duty["id"].clone(),
            vec!["position"],
        ),
        (
            "project",
            "ProjectLeader",
            project["id"].clone(),
            vec!["duty"],
        ),
    ] {
        let mut stage = fixture::node(key, &deps, source, json!([]));
        stage["sourceId"] = id;
        stages.push(stage);
    }
    publish(service, admin, "general", json!(stages), &applicant);
    let options: Value = serde_json::from_slice(
        &service
            .dispatch(
                GET_OA_REQUEST_PROJECTS,
                &[],
                &[
                    ("kind", "general".into()),
                    ("employeeId", applicant.person.to_string()),
                    ("appointmentId", applicant.appointment.clone()),
                ],
                None,
                &applicant.token,
            )
            .unwrap(),
    )
    .unwrap();
    assert!(
        options["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|p| p["id"] == project["id"])
    );
    let outsider = fixture::request(
        service,
        &handler.token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"title":"不能借用项目身份","reason":"成员范围合同","category":"Other","projectId":project["id"]}),
        ),
    );
    assert_eq!(
        fixture::call(
            service,
            &handler.token,
            SUBMIT_GENERAL_REQUEST,
            outsider["id"].as_i64().unwrap(),
            Some(
                json!({"expectedVersion":outsider["versionNumber"],"commandKey":nonce().unwrap()})
            )
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let draft = fixture::request(
        service,
        &applicant.token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeId":applicant.person,"appointmentId":applicant.appointment,"title":"职责合同","reason":"验证三个稳定来源","category":"Other","projectId":project["id"]}),
        ),
    );
    let mut row = command(
        service,
        &applicant.token,
        SUBMIT_GENERAL_REQUEST,
        &draft,
        json!({"note":"提交"}),
    );
    assert_eq!(row["approvalPlan"]["steps"].as_array().unwrap().len(), 3);
    for source in ["Position", "Responsibility", "ProjectLeader"] {
        row = command(
            service,
            &reviewer.token,
            APPROVE_GENERAL_REQUEST,
            &row,
            json!({"note":"按职责签署"}),
        );
        assert!(
            row["approvalPlan"]["steps"]
                .as_array()
                .unwrap()
                .iter()
                .any(|s| s["origin"]["source"] == source)
        );
    }
    assert_eq!(row["status"], "Approved");
    let mut child = step("child-review", "Approval", "Decision", &[], reviewer.user);
    child["materialPolicy"] = json!({"hiddenFields":["category"],"verificationFields":[{"key":"amount","label":"核验金额","kind":"Decimal","required":true}]});
    let child_settings = publish(service, admin, "general", json!([child]), &applicant);
    let child_version = child_settings["publications"]
        .as_array()
        .unwrap()
        .iter()
        .find(|p| p["key"] == "general")
        .unwrap()["version"]
        .clone();
    let mut child = step("child", "Subflow", "Decision", &[], 0);
    child["subflow"] = json!({"definitionKey":"general","version":child_version,"inputs":[{"sourcePath":"departmentId","target":"Department"},{"sourcePath":"category","target":"Currency"}],"outputs":[{"nodeKey":"child-review","verificationKey":"amount","target":"checkedAmount","aggregation":"Sum","required":true}],"policy":{"rejected":"Block","returned":"Block","exception":"Block","compensation":"ManualReview"}});
    let mut delivery = step(
        "delivery",
        "Handling",
        "Fulfillment",
        &["child"],
        handler.user,
    );
    delivery["reviewPoints"] = json!(["已完成交付"]);
    delivery["materialPolicy"] = json!({"requireReviewPoints":true,"verificationFields":[{"key":"received","label":"已完成交付","kind":"Boolean","required":true}]});
    let mut acceptance = step(
        "acceptance",
        "Approval",
        "Fulfillment",
        &["delivery"],
        verifier.user,
    );
    acceptance["handlingAction"] = json!("Acceptance");
    acceptance["repeatedApprover"] = json!("Reject");
    let mut document = step("document", "System", "Fulfillment", &["acceptance"], 0);
    document["systemAction"] = json!("GenerateApprovalDocument");
    publish(
        service,
        admin,
        "general",
        json!([child, delivery, acceptance, document]),
        &applicant,
    );
    let draft = fixture::request(
        service,
        &applicant.token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeId":applicant.person,"appointmentId":applicant.appointment,"title":"子流程与实际办理","reason":"输入输出及独立验收","category":"Other"}),
        ),
    );
    let mut row = command(
        service,
        &applicant.token,
        SUBMIT_GENERAL_REQUEST,
        &draft,
        json!({"note":"提交"}),
    );
    let projected = fixture::call(
        service,
        &reviewer.token,
        GET_GENERAL_REQUEST,
        row["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert!(
        projected["approvalPlan"]["subflows"][0]["inputSnapshot"]["currency"].is_null(),
        "remapping must preserve the original field's material restriction"
    );
    let task = projected["reviewTaskKeys"][0].clone();
    let invalid = json!({"expectedVersion":row["versionNumber"],"commandKey":nonce().unwrap(),"taskKey":task,"note":"拒绝","verificationValues":{"unknown":"不能写入"}});
    assert_eq!(
        fixture::call(
            service,
            &reviewer.token,
            REJECT_GENERAL_REQUEST,
            row["id"].as_i64().unwrap(),
            Some(invalid)
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    row = command(
        service,
        &reviewer.token,
        REJECT_GENERAL_REQUEST,
        &row,
        json!({"taskKey":task,"note":"子流程不通过"}),
    );
    assert_eq!(row["status"], "Pending");
    let child_key = row["approvalPlan"]["subflows"][0]["key"]
        .as_str()
        .unwrap()
        .to_owned();
    row = command(
        service,
        admin,
        EXECUTE_GENERAL_REQUEST_WORKFLOW,
        &row,
        json!({"command":"RetrySubflow","subflowKey":child_key,"note":"复核后重试"}),
    );
    row = command(
        service,
        &reviewer.token,
        APPROVE_GENERAL_REQUEST,
        &row,
        json!({"note":"核验通过","verificationValues":{"amount":"0.30"}}),
    );
    assert_eq!(row["status"], "Approved");
    assert_eq!(
        row["approvalPlan"]["subflowProgress"][&child_key]["outputs"]["checkedAmount"],
        "0.3"
    );
    row = command(
        service,
        &handler.token,
        EXECUTE_GENERAL_REQUEST_FULFILLMENT,
        &row,
        json!({"nodeKey":"delivery","note":"如实交付","verificationValues":{"received":true},"confirmedReviewPoints":[0]}),
    );
    assert_eq!(row["status"], "Approved");
    row = command(
        service,
        &verifier.token,
        EXECUTE_GENERAL_REQUEST_FULFILLMENT,
        &row,
        json!({"taskKey":fixture::call(service,&verifier.token,GET_GENERAL_REQUEST,row["id"].as_i64().unwrap(),None).unwrap()["reviewTaskKeys"][0],"nodeKey":"acceptance","note":"独立核验"}),
    );
    assert_eq!(row["status"], "Completed");
    let decisions = fixture::call(
        service,
        admin,
        LIST_DECISIONS_OF_GENERAL_REQUEST,
        row["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert!(
        decisions["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["signature"]["status"] == "Rejected")
    );
    assert!(
        decisions["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["signature"]["status"] == "Approved")
    );
    let parameters = [
        ("id", row["id"].to_string()),
        ("submissionId", row["submissionId"].to_string()),
    ];
    service
        .dispatch(
            GENERATE_APPROVAL_OF_GENERAL_REQUEST,
            &parameters,
            &[],
            None,
            &applicant.token,
        )
        .unwrap();
    let start = std::time::Instant::now();
    loop {
        let current = fixture::call(
            service,
            &applicant.token,
            GET_GENERAL_REQUEST,
            row["id"].as_i64().unwrap(),
            None,
        )
        .unwrap();
        if current["approvalDocument"]["documentStatus"] == "Ready" {
            break;
        }
        assert!(start.elapsed() < std::time::Duration::from_secs(20));
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    assert!(
        service
            .download_file(
                DOWNLOAD_APPROVAL_OF_GENERAL_REQUEST,
                &parameters,
                &applicant.token
            )
            .unwrap()
            .content
            .starts_with(b"%PDF-")
    );
}
