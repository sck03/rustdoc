//! Initial registration is direct; every formal change uses the same published workflow.
#[path = "approval_fixture.rs"]
mod workflow;
use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};

struct Session<'a>(&'a NativeService, String);
impl Session<'_> {
    fn request(&self, op: Operation, id: i64, body: Option<Value>) -> Result<Value, ApiError> {
        workflow::call(
            self.0,
            &self.1,
            op,
            id,
            workflow::decision_body(self.0, &self.1, op, id, body),
        )
    }
    fn login(&self, username: &str) -> Self {
        let login = self
            .request(
                LOGIN,
                0,
                Some(json!({"username":username,"password":"Personnel-Policy-2026"})),
            )
            .unwrap();
        Self(self.0, login["accessToken"].as_str().unwrap().into())
    }
}
fn action(client: &Session<'_>, op: Operation, row: &Value) -> Result<Value, ApiError> {
    client.request(
        op,
        row["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":row["versionNumber"],"note":"人员制度验收"})),
    )
}
fn proposal(client: &Session<'_>, person: &Value, change: &str, department: &str) -> Value {
    client.request(CREATE_PERSONNEL_CHANGE_REQUEST, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"任职变动验收","reason":"按发布制度办理","personnelChange":{"action":change,"expectedPersonnelVersion":person["versionNumber"],"effectiveDate":if change=="Rehire" {"2026-10-02"} else {"2026-10-01"},"departmentId":department,"jobTitle":"业务主管","onProbation":true}}))).unwrap()
}
pub fn exercise(service: &NativeService, token: &str) {
    let root = Session(service, token.into());
    let key = &nonce().unwrap()[..8];
    let company = format!("POLICY-{key}");
    let department = format!("HR-{key}");
    let next_department = format!("JOB-{key}");
    root.request(
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"人员制度验收公司","isActive":true})),
    )
    .unwrap();
    for code in [&department, &next_department] {
        root.request(
            CREATE_ORGANIZATION_DEPARTMENT,
            0,
            Some(json!({"code":code,"name":code,"companyCode":company,"isActive":true})),
        )
        .unwrap();
    }
    let mut users = vec![];
    for (name, role) in [
        ("admin", "Admin"),
        ("hr", "PersonnelManager"),
        ("employee", "OfficeEmployee"),
    ] {
        users.push(root.request(CREATE_USER_ACCOUNT, 0, Some(json!({"username":format!("policy-{name}-{key}"),"fullName":name,"role":role,"companyScope":company,"departmentId":department,"isActive":true,"resetPassword":"Personnel-Policy-2026"}))).unwrap()["user"].clone());
    }
    let admin = root.login(users[0]["username"].as_str().unwrap());
    let registration = |number: &str, name: &str| json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("{number}-{key}"),"departmentId":department,"jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":true,"profile":{"fullName":name}});
    let initial = admin
        .request(
            CREATE_PERSONNEL,
            0,
            Some(registration("INITIAL", "首次建档")),
        )
        .unwrap();
    assert_eq!(initial["canCorrectRegistration"], true);
    let id = initial["employee"]["id"].as_i64().unwrap();
    let corrected = admin.request(UPDATE_PERSONNEL, id, Some(json!({"expectedVersion":initial["versionNumber"],"profile":initial["profile"],"employmentType":"FullTime","correctionNote":"首次建档纠错","registration":{"employeeNumber":format!("CORRECT-{key}"),"departmentId":department,"jobTitle":"业务助理","hireDate":"2026-09-01","onProbation":true}}))).unwrap();
    assert_eq!(corrected["employee"]["jobTitle"], "业务助理");
    for (i, person) in [
        (
            1,
            admin
                .request(CREATE_PERSONNEL, 0, Some(registration("HR", "人事专员")))
                .unwrap(),
        ),
        (2, corrected),
    ] {
        admin.request(LINK_PERSONNEL_ACCOUNT, person["employee"]["id"].as_i64().unwrap(), Some(json!({"expectedVersion":person["versionNumber"],"userId":users[i]["id"],"expectedAccountVersion":users[i]["versionNumber"]}))).unwrap();
    }
    let hr = root.login(users[1]["username"].as_str().unwrap());
    let employee = root.login(users[2]["username"].as_str().unwrap());
    let (review_id, review_token) = workflow::reviewer(service, &admin.1);
    let reviewer = Session(service, review_token);
    workflow::configure(service, &admin.1, review_id);
    let required = json!(["Confirm", "Transfer", "Depart", "Rehire"]);
    assert_eq!(
        hr.request(GET_PERSONNEL_OPTIONS, 0, None).unwrap()["approvalRequiredActions"],
        required
    );
    assert_eq!(
        admin.request(GET_PERSONNEL_OPTIONS, 0, None).unwrap()["approvalRequiredActions"],
        required
    );
    assert!(
        admin
            .request(GET_OA_APPROVAL_SETTINGS, 0, None)
            .unwrap()
            .get("personnelApprovalActions")
            .is_none()
    );
    let mut person = hr.request(GET_PERSONNEL, id, None).unwrap();
    assert_eq!(person["canCorrectRegistration"], false);
    let correction = json!({"expectedVersion":person["versionNumber"],"profile":person["profile"],"employmentType":"FullTime","correctionNote":"禁止绕过正式流程","registration":{"employeeNumber":format!("ESCAPE-{key}"),"departmentId":next_department,"jobTitle":"未经审批","hireDate":"2026-09-01","onProbation":false}});
    assert_eq!(
        admin
            .request(UPDATE_PERSONNEL, id, Some(correction))
            .unwrap_err()
            .status,
        Some(409)
    );

    let draft = proposal(&hr, &person, "Confirm", &department);
    let pending = action(&hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    assert_eq!(
        action(&admin, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        action(&employee, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(403)
    );
    let returned = action(
        &reviewer,
        RETURN_FOR_CORRECTION_PERSONNEL_CHANGE_REQUEST,
        &pending,
    )
    .unwrap();
    assert_eq!(
        hr.request(GET_PERSONNEL, id, None).unwrap()["employee"]["status"],
        "Probation"
    );
    let pending = action(&hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &returned).unwrap();
    let completed = action(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    assert_eq!(completed["status"], "Completed");
    person = hr.request(GET_PERSONNEL, id, None).unwrap();
    assert_eq!(person["employee"]["status"], "Active");

    let draft = proposal(&hr, &person, "Transfer", &next_department);
    let pending = action(&hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    person = hr.request(UPDATE_PERSONNEL,id,Some(json!({"expectedVersion":person["versionNumber"],"profile":person["profile"],"employmentType":"FullTime","contractEndsOn":"2027-12-01"}))).unwrap();
    assert_eq!(
        action(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
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
    let mut withdrawn = action(&hr, WITHDRAW_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    withdrawn["expectedVersion"] = withdrawn["versionNumber"].clone();
    withdrawn["personnelChange"]["expectedPersonnelVersion"] = person["versionNumber"].clone();
    let revised = hr
        .request(
            UPDATE_PERSONNEL_CHANGE_REQUEST,
            withdrawn["id"].as_i64().unwrap(),
            Some(contracts::dto(
                contracts::schema("OaRequestSave"),
                withdrawn,
            )),
        )
        .unwrap();
    let pending = action(&hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &revised).unwrap();
    let blocker = employee.request(CREATE_GENERAL_REQUEST,0,Some(json!({"requestKey":nonce().unwrap(),"title":"交接未完成","reason":"最终批准必须重新核对","category":"Other"}))).unwrap();
    assert_eq!(
        action(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending)
            .unwrap_err()
            .status,
        Some(409)
    );
    action(&employee, CANCEL_GENERAL_REQUEST, &blocker).unwrap();
    action(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    assert_eq!(
        employee
            .request(GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    person = hr.request(GET_PERSONNEL, id, None).unwrap();
    assert_eq!(person["employee"]["departmentId"], next_department);
    assert_eq!(person["account"]["departmentId"], next_department);

    let previous_primary = person["organization"]["primaryAssignmentId"].clone();
    for change in ["Depart", "Rehire"] {
        let draft = proposal(&hr, &person, change, &next_department);
        let pending = action(&hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
        action(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
        person = hr.request(GET_PERSONNEL, id, None).unwrap();
        assert_eq!(
            person["account"]["isActive"], false,
            "rehire never restores the old account"
        );
        let accounts = admin.request(LIST_USERS, 0, None).unwrap();
        let account = accounts["users"]
            .as_array()
            .unwrap()
            .iter()
            .find(|u| u["id"] == users[2]["id"])
            .unwrap();
        assert_eq!(
            account["permissionGrants"],
            json!([]),
            "departure revokes grants permanently"
        );
        assert!(account["permissionTemplateId"].is_null());
    }
    assert_eq!(person["employee"]["status"], "Probation");
    assert_eq!(
        person["organization"]["primaryStartsOn"],
        person["hireDate"]
    );
    assert_ne!(
        person["organization"]["primaryAssignmentId"],
        previous_primary
    );
    assert!(
        person["organization"]["additionalAssignments"]
            .as_array()
            .unwrap()
            .iter()
            .any(|a| a["id"] == previous_primary && a["endsOn"] == "2026-10-01")
    );

    let body = json!({"requestKey":nonce().unwrap(),"title":"正式入职","reason":"独立业务审批后建档","personnelChange":{"action":"Hire","registration":registration("APPROVED","正式入职人员")}});
    let before = hr.request(LIST_PERSONNEL, 0, None).unwrap()["totalCount"]
        .as_i64()
        .unwrap();
    let draft = hr
        .request(CREATE_PERSONNEL_CHANGE_REQUEST, 0, Some(body.clone()))
        .unwrap();
    assert_eq!(
        hr.request(LIST_PERSONNEL, 0, None).unwrap()["totalCount"],
        before
    );
    let pending = action(&hr, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
    let completed = action(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
    assert_eq!(completed["status"], "Completed");
    assert_eq!(
        hr.request(CREATE_PERSONNEL_CHANGE_REQUEST, 0, Some(body))
            .unwrap()["id"],
        completed["id"]
    );
    assert_eq!(
        hr.request(LIST_PERSONNEL, 0, None).unwrap()["totalCount"],
        before + 1
    );
    let hired = hr
        .request(
            GET_PERSONNEL,
            completed["employeeId"].as_i64().unwrap(),
            None,
        )
        .unwrap();
    assert_eq!(
        hired["organization"]["primaryAssignmentId"],
        format!("hire-{}-primary", completed["id"])
    );
}
