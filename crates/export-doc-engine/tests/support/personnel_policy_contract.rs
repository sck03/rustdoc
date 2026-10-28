//! The same direct/approval lifecycle contract runs against SQLite and PostgreSQL.
use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};

struct Session<'a>(&'a NativeService, String);
impl Session<'_> {
    fn request(&self, op: Operation, id: i64, body: Option<Value>) -> Result<Value, ApiError> {
        let parameters = if id > 0 {
            vec![("id", id.to_string())]
        } else {
            vec![]
        };
        let body = body.map(|v| contracts::overlay(contracts::object(op.id, true), &v));
        let value =
            serde_json::from_slice(&self.0.dispatch(op, &parameters, &[], body, &self.1)?).unwrap();
        export_doc_contracts::validation::response(op.id, &value).unwrap();
        Ok(value)
    }
    fn login(&self, username: String, password: String) -> Result<Self, ApiError> {
        let result = self.request(
            LOGIN,
            0,
            Some(json!({"username":username,"password":password})),
        )?;
        Ok(Self(self.0, result["accessToken"].as_str().unwrap().into()))
    }
}

fn action(
    client: &Session<'_>,
    op: Operation,
    row: &Value,
) -> Result<Value, export_doc_engine::api::ApiError> {
    client.request(
        op,
        row["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":row["versionNumber"],"note":"人员制度验收"})),
    )
}
fn proposal(client: &Session<'_>, person: &Value, change: &str, department: &str) -> Value {
    client.request(CREATE_PERSONNEL_CHANGE_REQUEST, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"任职变动验收","reason":"按制度办理","personnelChange":{"action":change,"expectedPersonnelVersion":person["versionNumber"],"effectiveDate":"2026-10-01","departmentId":department,"jobTitle":"业务主管","onProbation":true}}))).unwrap()
}
pub fn exercise(service: &NativeService, token: &str) {
    let root = Session(service, token.into());
    let key = &nonce().unwrap()[..8];
    let company = format!("POLICY-{key}");
    let department = format!("HR-{key}");
    let new_department = format!("JOB-{key}");
    root.request(
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"人员制度验收公司","isActive":true})),
    )
    .unwrap();
    for code in [&department, &new_department] {
        root.request(
            CREATE_ORGANIZATION_DEPARTMENT,
            0,
            Some(json!({"code":code,"name":code,"companyCode":company,"isActive":true})),
        )
        .unwrap();
    }
    let mut accounts = vec![];
    let mut sessions = vec![];
    for (name, role) in [
        ("admin", "Admin"),
        ("hr", "PersonnelManager"),
        ("review", "PersonnelManager"),
    ] {
        let username = format!("policy-{name}-{key}");
        let account = root.request(CREATE_USER_ACCOUNT, 0, Some(json!({"username":username,"fullName":name,"role":role,"companyScope":company,"departmentId":department,"isActive":true,"resetPassword":"Personnel-Policy-2026"}))).unwrap()["user"].clone();
        accounts.push(account);
        sessions.push(
            root.login(username, "Personnel-Policy-2026".into())
                .unwrap(),
        );
    }
    let admin = &sessions[0];
    let hr = &sessions[1];
    let reviewer = &sessions[2];
    let registration = json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("DIRECT-{key}"),"departmentId":department,"jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":true,"profile":{"fullName":"直接办理"}});
    let mut direct = hr
        .request(CREATE_PERSONNEL, 0, Some(registration.clone()))
        .unwrap();
    let direct_id = direct["employee"]["id"].as_i64().unwrap();
    assert_eq!(
        hr.request(GET_PERSONNEL_OPTIONS, 0, None).unwrap()["approvalRequiredActions"],
        json!([])
    );
    let mut settings = admin.request(GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    settings["expectedVersion"] = settings["versionNumber"].clone();
    settings["personnelApprovalActions"] =
        json!(["Hire", "Confirm", "Transfer", "Depart", "Rehire"]);
    // This contract tests the opt-in personnel policy with one explicit reviewer.
    for rule in settings["rules"].as_array_mut().unwrap() {
        rule["mode"] = json!("Single");
        rule["stages"] = json!([]);
    }
    admin
        .request(SAVE_OA_APPROVAL_SETTINGS, 0, Some(settings))
        .unwrap();
    let transition = json!({"expectedVersion":direct["versionNumber"],"effectiveDate":"2026-09-02","note":"管理员直接办理"});
    assert_eq!(
        hr.request(CONFIRM_PERSONNEL, direct_id, Some(transition.clone()))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        hr.request(CREATE_PERSONNEL, 0, Some(registration))
            .unwrap_err()
            .status,
        Some(409)
    );
    // A global administrator can maintain another company's personnel directly.
    direct = root
        .request(CONFIRM_PERSONNEL, direct_id, Some(transition))
        .unwrap();
    let history = hr.request(GET_PERSONNEL_HISTORY, direct_id, None).unwrap();
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|event| event["action"] == "Confirm")
    );
    let options: Value = serde_json::from_slice(
        &service
            .dispatch(
                GET_PERSONNEL_OPTIONS,
                &[],
                &[("employeeId", direct_id.to_string())],
                None,
                &root.1,
            )
            .unwrap(),
    )
    .unwrap();
    assert!(
        options["departments"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["code"] == department)
    );
    assert!(
        options["departments"]
            .as_array()
            .unwrap()
            .iter()
            .all(|d| d["code"] == department || d["code"] == new_department)
    );
    assert_eq!(direct["employee"]["status"], "Active");
    assert_eq!(direct["approvalRequiredActions"], json!([]));
    assert_eq!(direct["canCorrectRegistration"], true);
    let correction = json!({"expectedVersion":direct["versionNumber"],"profile":direct["profile"],"employmentType":"FullTime","correctionNote":"核对合同后修正工号","registration":{"employeeNumber":format!("CORRECT-{key}"),"departmentId":department,"jobTitle":"业务员","hireDate":"2026-09-01","onProbation":false}});
    direct = admin
        .request(UPDATE_PERSONNEL, direct_id, Some(correction))
        .unwrap();
    assert_eq!(
        direct["employee"]["employeeNumber"],
        format!("CORRECT-{key}")
    );
    let body = json!({"requestKey":nonce().unwrap(),"title":"入职审批","reason":"新员工入职","personnelChange":{"action":"Hire","registration":{"requestKey":"ignored-nested-key","employeeNumber":format!("APPROVAL-{key}"),"departmentId":department,"jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":true,"probationEndsOn":"2026-12-01","contractEndsOn":"2027-12-01","profile":{"fullName":"审批人员"}}}});
    let draft = hr
        .request(CREATE_PERSONNEL_CHANGE_REQUEST, 0, Some(body.clone()))
        .unwrap();
    assert_eq!(
        hr.request(LIST_PERSONNEL, 0, None).unwrap()["totalCount"],
        1
    );
    let pending = action(hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    assert_eq!(
        action(hr, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(403)
    );
    let rejected = action(reviewer, REJECT_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    assert_eq!(
        hr.request(LIST_PERSONNEL, 0, None).unwrap()["totalCount"],
        1
    );
    let pending = action(hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &rejected).unwrap();
    let approved = action(reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    assert_eq!(approved["status"], "Completed");
    assert_eq!(
        hr.request(CREATE_PERSONNEL_CHANGE_REQUEST, 0, Some(body))
            .unwrap()["id"],
        approved["id"]
    );
    assert_eq!(
        action(reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &approved)
            .unwrap_err()
            .status,
        Some(409)
    );
    let id = approved["employeeId"].as_i64().unwrap();
    let mut person = admin.request(GET_PERSONNEL, id, None).unwrap();
    person = admin.request(LINK_PERSONNEL_ACCOUNT, id, Some(json!({"expectedVersion":person["versionNumber"],"userId":accounts[2]["id"],"expectedAccountVersion":accounts[2]["versionNumber"]}))).unwrap();
    let employee = root
        .login(
            accounts[2]["username"].as_str().unwrap().into(),
            "Personnel-Policy-2026".into(),
        )
        .unwrap();
    let draft = proposal(hr, &person, "Confirm", &department);
    let pending = action(hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    assert_eq!(
        employee
            .request(
                GET_PERSONNEL_CHANGE_REQUEST,
                pending["id"].as_i64().unwrap(),
                None
            )
            .unwrap()["canReview"],
        false
    );
    assert_eq!(
        action(&employee, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(403)
    );
    let queue: Value = serde_json::from_slice(
        &service
            .dispatch(
                LIST_PERSONNEL_CHANGE_REQUEST,
                &[],
                &[
                    ("approvalsOnly", "true".into()),
                    ("mineOnly", "false".into()),
                ],
                None,
                &employee.1,
            )
            .unwrap(),
    )
    .unwrap();
    assert!(
        !queue["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|row| row["id"] == pending["id"])
    );
    action(admin, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    person = admin.request(GET_PERSONNEL, id, None).unwrap();
    assert_eq!(person["employee"]["status"], "Active");
    let draft = proposal(hr, &person, "Transfer", &new_department);
    let pending = action(hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    person = admin.request(UPDATE_PERSONNEL, id, Some(json!({"expectedVersion":person["versionNumber"],"profile":person["profile"],"employmentType":"FullTime","probationEndsOn":"2026-12-01","contractEndsOn":"2027-12-01"}))).unwrap();
    assert_eq!(
        action(admin, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        hr.request(
            GET_PERSONNEL_CHANGE_REQUEST,
            pending["id"].as_i64().unwrap(),
            None
        )
        .unwrap()["versionNumber"],
        pending["versionNumber"]
    );
    let mut draft = action(hr, WITHDRAW_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    draft["expectedVersion"] = draft["versionNumber"].clone();
    draft["personnelChange"]["expectedPersonnelVersion"] = person["versionNumber"].clone();
    let draft = hr
        .request(
            UPDATE_PERSONNEL_CHANGE_REQUEST,
            draft["id"].as_i64().unwrap(),
            Some(draft),
        )
        .unwrap();
    let pending = action(hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    let mut blocker = json!({"requestKey":nonce().unwrap(),"title":"交接未完成","reason":"验证最终审批重新检查","category":"Other"});
    if service.provider().unwrap() == "SQLite" {
        blocker["employeeId"] = json!(id);
    }
    let blocker = employee
        .request(CREATE_GENERAL_REQUEST, 0, Some(blocker))
        .unwrap();
    assert_eq!(
        action(admin, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        admin.request(GET_PERSONNEL, id, None).unwrap()["employee"]["departmentId"],
        department
    );
    assert_eq!(
        hr.request(
            GET_PERSONNEL_CHANGE_REQUEST,
            pending["id"].as_i64().unwrap(),
            None
        )
        .unwrap()["status"],
        "Pending"
    );
    action(&employee, CANCEL_GENERAL_REQUEST, &blocker).unwrap();
    action(admin, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    person = admin.request(GET_PERSONNEL, id, None).unwrap();
    assert_eq!(person["account"]["departmentId"], new_department);
    assert_eq!(
        root.login(
            accounts[2]["username"].as_str().unwrap().into(),
            "Personnel-Policy-2026".into()
        )
        .unwrap()
        .request(GET_CURRENT_USER, 0, None)
        .unwrap()["role"],
        "PersonnelManager"
    );
    assert_eq!(
        employee
            .request(GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    for change in ["Depart", "Rehire"] {
        let draft = proposal(hr, &person, change, &new_department);
        let pending = action(hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
        action(admin, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
        person = admin.request(GET_PERSONNEL, id, None).unwrap();
        assert_eq!(person["account"]["isActive"], false);
    }
    assert_eq!(person["employee"]["status"], "Probation");
    assert!(person["probationEndsOn"].is_null() && person["contractEndsOn"].is_null());
}
