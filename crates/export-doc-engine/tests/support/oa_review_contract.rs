//! Public regressions for participation limits and stable appointments, shared by both stores.
#[path = "approval_fixture.rs"]
mod fixture;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn publish(service: &NativeService, admin: &str, node: Value) -> Value {
    let mut settings = fixture::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap()["stages"] = json!([node]);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let saved = fixture::save_settings(service, admin, settings).unwrap();
    fixture::request(
        service,
        admin,
        PUBLISH_OA_DEFINITION,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"general"})),
    );
    saved
}
fn submit(service: &NativeService, token: &str, draft: &Value) -> Value {
    let id = draft["id"].as_i64().unwrap();
    fixture::call(
        service,
        token,
        SUBMIT_GENERAL_REQUEST,
        id,
        fixture::decision_body(
            service,
            token,
            SUBMIT_GENERAL_REQUEST,
            id,
            Some(json!({"expectedVersion":draft["versionNumber"],"note":"提交回归申请"})),
        ),
    )
    .unwrap()
}
fn sign(service: &NativeService, token: &str, row: &Value) -> Value {
    let id = row["id"].as_i64().unwrap();
    fixture::call(
        service,
        token,
        APPROVE_GENERAL_REQUEST,
        id,
        fixture::decision_body(
            service,
            token,
            APPROVE_GENERAL_REQUEST,
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"真实人员签署"})),
        ),
    )
    .unwrap()
}
fn read(service: &NativeService, token: &str, op: Operation, query: &[(&str, String)]) -> Value {
    serde_json::from_slice(&service.dispatch(op, &[], query, None, token).unwrap()).unwrap()
}
pub fn exercise(service: &NativeService, admin: &str) {
    participation_limits(service, admin);
    same_department_appointments(service, admin);
}

fn participation_limits(service: &NativeService, admin: &str) {
    let suffix = &nonce().unwrap()[..8];
    let (reviewer_id, old_token) = fixture::reviewer(service, admin);
    let (replacement_id, replacement) = fixture::reviewer(service, admin);
    let identity = fixture::request(service, &old_token, GET_CURRENT_USER, None);
    let a = format!("SCOPE-A-{suffix}");
    let b = format!("SCOPE-B-{suffix}");
    let mut departments = vec![];
    let mut applicants = vec![];
    for code in [&a, &b] {
        departments.push(fixture::request(
            service,
            admin,
            CREATE_ORGANIZATION_DEPARTMENT,
            Some(json!({"code":code,"name":code,"companyCode":"DEFAULT","isActive":true})),
        ));
        applicants.push(fixture::request(service, admin, CREATE_PERSONNEL, Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":code,"jobTitle":"范围验收人员","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":code}}))));
    }
    let mut node = fixture::node("scope-review", &[], "Named", json!([reviewer_id]));
    node["materialPolicy"] = json!({"allowRestrictedAttachments":true});
    publish(service, admin, node);
    let mut rows = vec![];
    for applicant in &applicants {
        let mut draft = fixture::request(
            service,
            admin,
            CREATE_GENERAL_REQUEST,
            Some(
                json!({"requestKey":nonce().unwrap(),"employeeId":applicant["employee"]["id"],"title":format!("范围-{}",nonce().unwrap()),"reason":"显式限制优先于分派","category":"Other"}),
            ),
        );
        if applicant["employee"]["id"] == applicants[1]["employee"]["id"] {
            let id = draft["id"].as_i64().unwrap();
            draft=service.upload(super::oa_contract::operation("general","upload"),&[("id",id.to_string())],json!({"expectedVersion":draft["versionNumber"],"purpose":"Supporting","classification":"Restricted"}),"scope.xml",b"<?xml version=\"1.0\"?><Scope>anonymous</Scope>",admin).unwrap();
        }
        rows.push(submit(service, admin, &draft));
    }
    let blocked_id = rows[1]["id"].as_i64().unwrap();
    let before = fixture::call(service, &old_token, GET_GENERAL_REQUEST, blocked_id, None).unwrap();
    assert_eq!(
        before["approvalPlan"]["steps"][0]["approverPersonId"],
        identity["employee"]["id"]
    );
    let attachment = before["attachments"][0]["id"].as_i64().unwrap();
    let path = [
        ("id", blocked_id.to_string()),
        ("attachmentId", attachment.to_string()),
    ];
    assert!(
        service
            .download_file(
                super::oa_contract::operation("general", "download"),
                &path,
                &old_token
            )
            .is_ok()
    );
    let old_notice = read(service, &old_token, LIST_NOTIFICATIONS, &[])["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|n| n["requestId"] == blocked_id)
        .unwrap()["id"]
        .as_i64()
        .unwrap();
    let restriction = fixture::request(
        service,
        admin,
        SAVE_ACCESS_RESTRICTION,
        Some(
            json!({"principalKind":"Account","principalId":reviewer_id.to_string(),"resourceKey":"office.general","action":"view","mode":"Departments","members":[departments[0]["id"].as_i64().unwrap().to_string()],"reason":"限定为甲部门","expectedVersion":0}),
        ),
    );
    assert_eq!(
        fixture::call(service, &old_token, GET_CURRENT_USER, 0, None)
            .err()
            .unwrap()
            .status,
        Some(401)
    );
    let login = fixture::request(
        service,
        "",
        LOGIN,
        Some(json!({"username":identity["username"],"password":"Workflow-Review-2026"})),
    );
    let token = login["accessToken"].as_str().unwrap();
    let list = read(
        service,
        token,
        super::oa_contract::operation("general", "list"),
        &[("mineOnly", "false".into())],
    );
    assert_eq!(list["totalCount"], 1);
    assert_eq!(list["items"][0]["id"], rows[0]["id"]);
    assert_eq!(
        fixture::call(service, token, GET_GENERAL_REQUEST, blocked_id, None)
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    assert_eq!(fixture::call(service,token,APPROVE_GENERAL_REQUEST,blocked_id,Some(json!({"expectedVersion":rows[1]["versionNumber"],"commandKey":nonce().unwrap(),"taskKey":rows[1]["approvalPlan"]["steps"][0]["taskKey"],"note":"不允许绕过明确范围"}))).err().unwrap().status,Some(403));
    assert_eq!(
        service
            .download_file(
                super::oa_contract::operation("general", "download"),
                &path,
                token
            )
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    assert_eq!(
        fixture::call(service, token, READ_NOTIFICATION, old_notice, None)
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    let notices = read(service, token, LIST_NOTIFICATIONS, &[]);
    assert!(
        !notices["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|n| n["requestId"] == blocked_id)
    );
    let tasks = read(
        service,
        admin,
        GET_UNIT_WORKFLOW_TASKS,
        &[("assigneeId", reviewer_id.to_string())],
    );
    assert_eq!(
        tasks["items"]
            .as_array()
            .unwrap()
            .iter()
            .find(|t| t["requestId"] == blocked_id)
            .unwrap()["identityAvailable"],
        false
    );
    assert!(
        tasks["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|t| t.get("_scope").is_none() && t.get("_assigneePersonId").is_none())
    );
    assert_eq!(sign(service, token, &rows[0])["status"], "Approved");
    fixture::request(
        service,
        admin,
        REASSIGN_UNIT_WORKFLOW_TASK,
        Some(
            json!({"requestId":blocked_id,"requestKind":"oa-general","expectedVersion":rows[1]["versionNumber"],"fromUserId":reviewer_id,"toUserId":replacement_id,"toContextId":"","taskKey":rows[1]["approvalPlan"]["steps"][0]["taskKey"],"note":"向合格人员移交超出范围的事项"}),
        ),
    );
    let transferred =
        fixture::call(service, &replacement, GET_GENERAL_REQUEST, blocked_id, None).unwrap();
    assert_ne!(
        transferred["approvalPlan"]["steps"][0]["approverPersonId"],
        identity["employee"]["id"]
    );
    assert_eq!(
        sign(service, &replacement, &transferred)["status"],
        "Approved"
    );
    fixture::request(
        service,
        admin,
        SAVE_ACCESS_RESTRICTION,
        Some(
            json!({"principalKind":"Account","principalId":reviewer_id.to_string(),"resourceKey":"office.general","action":"view","mode":"Unrestricted","members":[],"reason":"回归已结束","expectedVersion":restriction["versionNumber"]}),
        ),
    );
}

fn same_department_appointments(service: &NativeService, admin: &str) {
    let (first_id, first) = fixture::reviewer(service, admin);
    let (second_id, second) = fixture::reviewer(service, admin);
    let first_user = fixture::request(service, &first, GET_CURRENT_USER, None);
    let second_user = fixture::request(service, &second, GET_CURRENT_USER, None);
    let first_person = fixture::call(
        service,
        admin,
        GET_PERSONNEL,
        first_user["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    let second_person = fixture::call(
        service,
        admin,
        GET_PERSONNEL,
        second_user["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    let username = format!("appointment-identity-{}", nonce().unwrap());
    let grants = json!([{"resourceKey":"office.general","action":"view","dataScope":"own"},{"resourceKey":"office.general","action":"create","dataScope":"own"},{"resourceKey":"office.general","action":"edit","dataScope":"own"}]);
    let user=fixture::request(service,admin,CREATE_USER_ACCOUNT,Some(json!({"username":username,"fullName":"同部门多岗人员","role":"OfficeEmployee","companyScope":"DEFAULT","departmentId":"GENERAL","permissionGrants":grants,"isActive":true,"resetPassword":"Appointment-Identity-2026"})))["user"].clone();
    let person = fixture::request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":"GENERAL","jobTitle":"第一岗位","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":"同部门多岗人员"},"organization":{"managerEmployeeId":first_person["employee"]["id"],"managerAssignmentId":first_person["organization"]["primaryAssignmentId"],"additionalAssignments":[{"companyCode":"DEFAULT","departmentId":"GENERAL","jobTitle":"第二岗位","startsOn":"2026-01-01","endsOn":"","managerEmployeeId":second_person["employee"]["id"],"managerAssignmentId":second_person["organization"]["primaryAssignmentId"],"functionalManagerEmployeeIds":[]} ]}}),
        ),
    );
    let linked=fixture::call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":user["id"],"expectedAccountVersion":user["versionNumber"]}))).unwrap();
    let appointment = linked["organization"]["additionalAssignments"][0]["id"].clone();
    let role = fixture::request(
        service,
        admin,
        SAVE_UNIT_ACCESS,
        Some(
            json!({"userId":user["id"],"companyCode":"DEFAULT","name":"第二岗位业务身份","mode":"Business","appointmentId":appointment,"permissionGrants":grants,"management":{},"disabledModules":[],"oaAssignedOnly":false,"includeDescendants":false,"startsAt":"","endsAt":"","isActive":true,"expectedVersion":0}),
        ),
    );
    let settings = publish(
        service,
        admin,
        fixture::node("line", &[], "LineManager", json!([])),
    );
    let login = fixture::request(
        service,
        "",
        LOGIN,
        Some(json!({"username":username,"password":"Appointment-Identity-2026"})),
    );
    let session = fixture::request(
        service,
        login["accessToken"].as_str().unwrap(),
        SWITCH_UNIT_CONTEXT,
        Some(json!({"contextId":format!("unit:{}:DEFAULT",role["id"])})),
    );
    assert_eq!(session["user"]["employee"]["jobTitle"], "第二岗位");
    let token = session["accessToken"].as_str().unwrap();
    let draft = fixture::request(
        service,
        token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"title":"第二岗位申请","reason":"使用明确任职对应的上级","category":"Other"}),
        ),
    );
    assert_eq!(draft["applicantAppointmentId"], appointment);
    assert_eq!(draft["applicantJobTitle"], "第二岗位");
    let pending = submit(service, token, &draft);
    assert_eq!(
        pending["approvalPlan"]["steps"][0]["approverUserId"],
        second_id
    );
    let id = pending["id"].as_i64().unwrap();
    let snapshot: Value = serde_json::from_slice(
        &service
            .dispatch(
                super::oa_contract::operation("general", "submission"),
                &[
                    ("id", id.to_string()),
                    (
                        "submissionId",
                        pending["submissionId"].as_i64().unwrap().to_string(),
                    ),
                ],
                &[],
                None,
                token,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(snapshot["appointment"]["id"], appointment);
    assert_eq!(snapshot["applicantJobTitle"], "第二岗位");
    let definition = settings["rules"]
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["key"] == "general")
        .unwrap()
        .clone();
    let simulation = fixture::request(
        service,
        admin,
        SIMULATE_OA_DEFINITION,
        Some(
            json!({"definition":definition,"employeeId":linked["employee"]["id"],"appointmentId":appointment,"amount":"0","currency":"CNY","duration":"1","category":"Other"}),
        ),
    );
    assert_eq!(
        simulation["approvalPlan"]["steps"][0]["approverUserId"],
        second_id
    );
    assert_eq!(sign(service, &second, &pending)["status"], "Approved");
    let main = fixture::request(
        service,
        token,
        SWITCH_UNIT_CONTEXT,
        Some(json!({"contextId":""})),
    );
    let token = main["accessToken"].as_str().unwrap();
    assert_eq!(main["user"]["employee"]["jobTitle"], "第一岗位");
    let draft = fixture::request(
        service,
        token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"title":"主任职申请","reason":"主任职上级保持独立","category":"Other"}),
        ),
    );
    let pending = submit(service, token, &draft);
    assert_eq!(
        pending["approvalPlan"]["steps"][0]["approverUserId"],
        first_id
    );
    assert_eq!(sign(service, &first, &pending)["status"], "Approved");
}
