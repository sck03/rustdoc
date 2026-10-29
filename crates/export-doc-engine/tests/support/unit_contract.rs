//! The same unit identity and approval contract is exercised on both database adapters.
use super::oa_contract::{call, operation, workflow};
use super::organization_workflow_contract::policy;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn token(value: &Value) -> &str {
    value["accessToken"].as_str().unwrap()
}
fn grant(resource: &str, action: &str) -> Value {
    json!({"resourceKey":resource,"action":action,"dataScope":"own"})
}
fn access(
    service: &NativeService,
    admin: &str,
    user: &Value,
    company: &str,
    mode: &str,
    appointment_id: &str,
    grants: Value,
    management: Value,
) -> Value {
    call(service,admin,SAVE_UNIT_ACCESS,0,Some(json!({"userId":user["id"],"companyCode":company,"name":format!("{mode}-{company}"),"mode":mode,"appointmentId":appointment_id,"permissionGrants":grants,"management":management,"disabledModules":[],"oaAssignedOnly":mode=="Collaboration","includeDescendants":false,"startsAt":"","endsAt":if mode=="Collaboration" {"2030-12-31T23:59:59+08:00"}else{""},"isActive":true,"expectedVersion":0}))).unwrap()
}
pub fn exercise(service: &NativeService, root: &str) {
    let suffix = &nonce().unwrap()[..8];
    let hq = format!("HQ-{suffix}");
    let sub = format!("SUB-{suffix}");
    let main_department = format!("DEPT-{suffix}");
    let sub_department = main_department.clone();
    for (code, parent, dept) in [
        (&hq, None, &main_department),
        (&sub, Some(&hq), &sub_department),
    ] {
        call(service,root,CREATE_ORGANIZATION_COMPANY,0,Some(json!({"code":code,"name":code,"isActive":true,"parentCompanyCode":parent,"unitKind":"Company"}))).unwrap();
        call(
            service,
            root,
            CREATE_ORGANIZATION_DEPARTMENT,
            0,
            Some(json!({"code":dept,"companyCode":code,"name":dept,"isActive":true})),
        )
        .unwrap();
    }
    let directory = call(service, root, GET_ORGANIZATION_DIRECTORY, 0, None).unwrap();
    let sub_dept = directory["departments"]
        .as_array()
        .unwrap()
        .iter()
        .find(|d| d["companyCode"] == sub && d["code"] == sub_department)
        .unwrap();
    let renamed = call(service, root, UPDATE_ORGANIZATION_DEPARTMENT, sub_dept["id"].as_i64().unwrap(),
        Some(json!({"code":sub_department,"companyCode":sub,"name":"按稳定编号更新子公司部门","isActive":true,"expectedVersion":sub_dept["versionNumber"]}))).unwrap();
    assert_eq!(renamed["companyCode"], sub);
    let directory = call(service, root, GET_ORGANIZATION_DIRECTORY, 0, None).unwrap();
    assert!(
        directory["departments"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["companyCode"] == hq
                && d["code"] == main_department
                && d["name"] == main_department)
    );
    let hq_admin = call(
        service,
        root,
        SWITCH_UNIT_CONTEXT,
        0,
        Some(json!({"contextId":format!("company:{hq}")})),
    )
    .unwrap();
    assert_eq!(
        call(service, root, GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let admin = token(&hq_admin);
    let no_management =
        json!({"organization":false,"appointments":false,"workflows":false,"roles":false});
    let mut users = vec![];
    let mut people = vec![];
    for i in 0..4 {
        let user = call(service,admin,CREATE_USER_ACCOUNT,0,Some(json!({"username":format!("group-{suffix}-{i}"),"fullName":format!("集团人员{i}"),"role":"OfficeEmployee","companyScope":hq,"departmentId":main_department,"isActive":true,"resetPassword":"Unit-Contract-2026"}))).unwrap()["user"].clone();
        let person = call(service,admin,CREATE_PERSONNEL,0,Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("G-{suffix}-{i}"),"departmentId":main_department,"jobTitle":"总部主职","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":user["fullName"]}}))).unwrap();
        let person = call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":user["id"],"expectedAccountVersion":user["versionNumber"]}))).unwrap();
        users.push(user);
        people.push(person);
    }
    people[0] = call(service,admin,TRANSFER_PERSONNEL,people[0]["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":people[0]["versionNumber"],"effectiveDate":"2026-10-01","departmentId":main_department,"jobTitle":"总部主职","organization":{"managerEmployeeId":null,"functionalManagerEmployeeIds":[],"additionalAssignments":[{"companyCode":sub,"departmentId":sub_department,"jobTitle":"子公司兼职","startsOn":"2026-10-01","endsOn":"","managerEmployeeId":null,"functionalManagerEmployeeIds":[]}]},"note":"多单位任职"}))).unwrap();
    let mut worker_role = access(
        service,
        admin,
        &users[0],
        &sub,
        "Business",
        people[0]["organization"]["additionalAssignments"][0]["id"]
            .as_str()
            .unwrap(),
        json!([
            grant("office.general", "view"),
            grant("office.general", "create"),
            grant("office.general", "edit")
        ]),
        no_management.clone(),
    );
    let mut worker_scheme = call(service,admin,CREATE_PERMISSION_TEMPLATE,0,Some(json!({"code":format!("unit-worker-{suffix}"),"name":"单位申请人方案","description":"明确绑定当前单位","grants":worker_role["permissionGrants"],"disabledModules":[],"oaAssignedOnly":false,"isActive":true,"expectedVersion":0}))).unwrap();
    worker_role["permissionTemplateId"] = worker_scheme["id"].clone();
    worker_role["permissionGrants"] = Value::Null;
    worker_role["expectedVersion"] = worker_role["versionNumber"].clone();
    worker_role = call(service, admin, SAVE_UNIT_ACCESS, 0, Some(worker_role)).unwrap();
    let reviewer_role = access(
        service,
        admin,
        &users[1],
        &sub,
        "Collaboration",
        "",
        json!([
            grant("office.general", "view"),
            grant("office.general", "approve")
        ]),
        no_management.clone(),
    );
    let manager_role = access(
        service,
        admin,
        &users[2],
        &sub,
        "Administration",
        "",
        json!([grant("office.general", "view")]),
        json!({"organization":true,"appointments":true,"workflows":true,"roles":true}),
    );
    let mut analytics_role = access(
        service,
        admin,
        &users[3],
        &hq,
        "Administration",
        "",
        json!([]),
        json!({"organization":false,"appointments":false,"workflows":false,"roles":false,"analytics":true}),
    );
    analytics_role["includeDescendants"] = json!(true);
    analytics_role["expectedVersion"] = analytics_role["versionNumber"].clone();
    let analytics_role = call(service, admin, SAVE_UNIT_ACCESS, 0, Some(analytics_role)).unwrap();
    let directory = call(service, admin, GET_ORGANIZATION_DIRECTORY, 0, None).unwrap();
    let mut detached = directory["companies"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["code"] == sub)
        .unwrap()
        .clone();
    detached["expectedVersion"] = detached["versionNumber"].clone();
    detached["parentCompanyCode"] = Value::Null;
    let impact = call(
        service,
        admin,
        PREVIEW_ORGANIZATION_COMPANY_CHANGE,
        detached["id"].as_i64().unwrap(),
        Some(detached.clone()),
    )
    .unwrap();
    assert_eq!(impact["requiresHandover"], true);
    assert!(
        impact["impacts"]
            .as_array()
            .unwrap()
            .iter()
            .any(|i| i["roleId"] == analytics_role["id"] && i["removedUnits"][0]["code"] == sub)
    );
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_ORGANIZATION_COMPANY,
            detached["id"].as_i64().unwrap(),
            Some(detached)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let login = |index: usize| {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":users[index]["username"],"password":"Unit-Contract-2026"})),
        )
        .unwrap()
    };
    let worker = login(0);
    let manager = login(2);
    let switched = |session: &Value, role: &Value| {
        call(
            service,
            token(session),
            SWITCH_UNIT_CONTEXT,
            0,
            Some(json!({"contextId":format!("unit:{}:{sub}",role["id"])})),
        )
        .unwrap()
    };
    let worker_sub = switched(&worker, &worker_role);
    assert_eq!(worker_sub["user"]["companyScope"], sub);
    assert_eq!(worker_sub["user"]["employee"]["jobTitle"], "子公司兼职");
    assert_eq!(
        call(service, token(&worker), GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let manager_sub = switched(&manager, &manager_role);
    assert_eq!(manager_sub["user"]["capabilities"]["canManageUsers"], false);
    assert_eq!(
        call(service, token(&manager_sub), LIST_USERS, 0, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let directory = call(
        service,
        token(&manager_sub),
        GET_ORGANIZATION_DIRECTORY,
        0,
        None,
    )
    .unwrap();
    assert_eq!(directory["companies"].as_array().unwrap().len(), 1);
    assert_eq!(directory["companies"][0]["code"], sub);
    assert_eq!(directory["departments"][0]["code"], sub_department);
    let member_directory = call(service, token(&manager_sub), GET_UNIT_MEMBERS, 0, None).unwrap();
    assert_eq!(member_directory["totalCount"], 1);
    let member = &member_directory["items"][0];
    assert_eq!(member["fullName"], "集团人员0");
    assert!(member.get("profile").is_none());
    let mut member_save = json!({"companyCode":sub,"personId":member["personId"],"expectedVersion":member["versionNumber"],"appointments":member["appointments"],"note":"单位管理员维护兼职"});
    member_save["appointments"][0]["jobTitle"] = json!("子公司采购专员");
    let maintained = call(
        service,
        token(&manager_sub),
        SAVE_UNIT_MEMBER,
        0,
        Some(member_save),
    )
    .unwrap();
    assert_eq!(maintained["appointments"][0]["jobTitle"], "子公司采购专员");
    assert!(maintained.get("profile").is_none());
    assert_eq!(
        call(
            service,
            token(&manager_sub),
            GET_PERSONNEL,
            member["personId"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let worker_sub = switched(&login(0), &worker_role);
    assert_eq!(
        call(
            service,
            token(&manager_sub),
            CREATE_ORGANIZATION_COMPANY,
            0,
            Some(json!({"code":"ESCAPE","name":"拒绝越权","isActive":true}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    access(
        service,
        admin,
        &users[1],
        &hq,
        "Business",
        people[1]["organization"]["primaryAssignmentId"]
            .as_str()
            .unwrap(),
        json!([
            grant("office.general", "view"),
            grant("office.general", "approve")
        ]),
        no_management.clone(),
    );
    let reviewer = login(1);
    let directory = call(service, admin, GET_ORGANIZATION_DIRECTORY, 0, None).unwrap();
    let mut hq_row = directory["companies"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["code"] == hq)
        .unwrap()
        .clone();
    hq_row["expectedVersion"] = hq_row["versionNumber"].clone();
    hq_row["executiveEmployeeId"] = people[1]["employee"]["id"].clone();
    hq_row["executiveAppointmentId"] = people[1]["organization"]["primaryAssignmentId"].clone();
    service
        .dispatch(
            UPDATE_ORGANIZATION_COMPANY,
            &[("id", hq_row["id"].to_string())],
            &[],
            Some(hq_row.clone()),
            admin,
        )
        .unwrap();
    let mut node = workflow::node("group-review", &[], "CompanyExecutive", json!([]));
    node["organizationRoute"] = json!({"mode":"Specified","unitId":hq_row["id"]});
    node["reviewPoints"] = json!(["检查跨单位申请的业务归属"]);
    let mut rule = workflow::rule("general", "", json!([node]));
    let simulation = |rule: &Value| {
        call(service,token(&manager_sub),SIMULATE_OA_DEFINITION,0,Some(json!({"definition":rule,"employeeId":people[0]["employee"]["id"],"appointmentId":worker_role["appointmentId"],"amount":"","currency":"","duration":"","category":"Other"}))).unwrap()
    };
    let specified = simulation(&rule);
    assert_eq!(
        specified["approvalPlan"]["steps"][0]["approverUserId"],
        users[1]["id"]
    );
    rule["stages"][0]["organizationRoute"] = json!({"mode":"Ancestor","unitId":null});
    let ancestor = simulation(&rule);
    assert_eq!(
        ancestor["approvalPlan"]["steps"],
        specified["approvalPlan"]["steps"]
    );
    assert_eq!(
        call(
            service,
            token(&manager_sub),
            GET_UNIT_WORKFLOW_TASKS,
            0,
            None
        )
        .unwrap()["totalCount"],
        0
    );
    policy(service, token(&manager_sub), "general", rule);
    let draft = call(service,token(&worker_sub),operation("general","create"),0,Some(json!({"requestKey":nonce().unwrap(),"title":"子公司申请","reason":"总部授权审批","category":"Other"}))).unwrap();
    let other = call(service,token(&worker_sub),operation("general","create"),0,Some(json!({"requestKey":nonce().unwrap(),"title":"未分派草稿","reason":"不可外泄","category":"Other"}))).unwrap();
    let id = draft["id"].as_i64().unwrap();
    let mut pending = call(
        service,
        token(&worker_sub),
        operation("general", "submit"),
        id,
        Some(json!({"expectedVersion":draft["versionNumber"],"note":"提交"})),
    )
    .unwrap();
    assert_eq!(
        call(
            service,
            token(&manager_sub),
            GET_UNIT_WORKFLOW_SUMMARY,
            0,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let analyst = call(
        service,
        token(&login(3)),
        SWITCH_UNIT_CONTEXT,
        0,
        Some(json!({"contextId":format!("unit:{}:{hq}",analytics_role["id"])})),
    )
    .unwrap();
    let summary = call(service, token(&analyst), GET_UNIT_WORKFLOW_SUMMARY, 0, None).unwrap();
    assert_eq!(summary["totalCount"], 2);
    let sub_summary = summary["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["companyCode"] == sub)
        .unwrap();
    assert_eq!(sub_summary["requestCount"], 1);
    assert_eq!(sub_summary["activeTaskCount"], 1);
    assert!(sub_summary.get("title").is_none());
    assert_eq!(
        call(
            service,
            token(&analyst),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(service, token(&analyst), GET_UNIT_WORKFLOW_TASKS, 0, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        pending["approvalPlan"]["nodes"][0]["reviewPoints"],
        json!(["检查跨单位申请的业务归属"])
    );
    assert_eq!(
        call(
            service,
            token(&reviewer),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let reviewer_sub = switched(&login(1), &reviewer_role);
    let mut other_role = reviewer_role.clone();
    other_role["id"] = json!(0);
    other_role["expectedVersion"] = json!(0);
    other_role["name"] = json!("同单位另一协作身份");
    let other_role = call(service, admin, SAVE_UNIT_ACCESS, 0, Some(other_role)).unwrap();
    assert_eq!(
        call(service, token(&reviewer_sub), GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let other_reviewer = switched(&login(1), &other_role);
    assert_eq!(
        call(
            service,
            token(&other_reviewer),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(service, token(&other_reviewer), LIST_NOTIFICATIONS, 0, None).unwrap()["totalCount"],
        0
    );
    let reviewer_sub = switched(&other_reviewer, &reviewer_role);
    call(
        service,
        token(&reviewer_sub),
        operation("general", "get"),
        id,
        None,
    )
    .unwrap();
    assert_eq!(
        call(
            service,
            token(&reviewer_sub),
            operation("general", "get"),
            other["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(
            service,
            token(&reviewer_sub),
            GET_PERSONNEL,
            people[0]["employee"]["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let mut revoke = reviewer_role.clone();
    revoke["isActive"] = json!(false);
    revoke["expectedVersion"] = reviewer_role["versionNumber"].clone();
    assert_eq!(
        call(service, admin, SAVE_UNIT_ACCESS, 0, Some(revoke))
            .unwrap_err()
            .status,
        Some(409)
    );
    let moved_identity = call(service,token(&manager_sub),REASSIGN_UNIT_WORKFLOW_TASK,0,Some(json!({"requestId":id,"requestKind":"oa-general","expectedVersion":pending["versionNumber"],"taskKey":pending["approvalPlan"]["steps"][0]["taskKey"],"fromUserId":users[1]["id"],"toUserId":users[1]["id"],"toContextId":format!("unit:{}:{sub}",other_role["id"]),"note":"同一人员变更签署身份"}))).unwrap();
    assert_eq!(
        call(
            service,
            token(&reviewer_sub),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    pending["versionNumber"] = moved_identity["versionNumber"].clone();
    pending["approvalPlan"]["steps"][0]["approverContext"] =
        json!(format!("unit:{}:{sub}", other_role["id"]));
    let replacement_role = access(
        service,
        admin,
        &users[2],
        &sub,
        "Collaboration",
        "",
        json!([
            grant("office.general", "view"),
            grant("office.general", "approve")
        ]),
        no_management,
    );
    let manager_sub = switched(&login(2), &manager_role);
    let tasks = call(
        service,
        token(&manager_sub),
        GET_UNIT_WORKFLOW_TASKS,
        0,
        None,
    )
    .unwrap();
    assert_eq!(tasks["totalCount"], 1);
    assert!(tasks["items"][0].get("title").is_none());
    assert!(tasks["items"][0].get("reason").is_none());
    assert_eq!(
        tasks["items"][0]["contextId"],
        pending["approvalPlan"]["steps"][0]["approverContext"]
    );
    let moved = call(service,token(&manager_sub),REASSIGN_UNIT_WORKFLOW_TASK,0,Some(json!({"requestId":id,"requestKind":"oa-general","expectedVersion":pending["versionNumber"],"taskKey":tasks["items"][0]["taskKey"],"fromUserId":users[1]["id"],"toUserId":users[2]["id"],"note":"集团审批职责交接"}))).unwrap();
    assert_eq!(moved.as_object().unwrap().len(), 2);
    assert_eq!(
        call(
            service,
            token(&reviewer_sub),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let replacement = switched(&manager_sub, &replacement_role);
    let approved = call(
        service,
        token(&replacement),
        operation("general", "approve"),
        id,
        Some(json!({"expectedVersion":moved["versionNumber"],"note":"总部批准"})),
    )
    .unwrap();
    assert_eq!(approved["status"], "Approved");
    assert_eq!(approved["companyScope"], sub);
    assert_eq!(
        approved["approvalPlan"]["steps"][0]["actedByUserId"],
        users[2]["id"]
    );
    worker_scheme["expectedVersion"] = worker_scheme["versionNumber"].clone();
    worker_scheme["disabledModules"] = json!(["office.general"]);
    call(
        service,
        admin,
        UPDATE_PERMISSION_TEMPLATE,
        worker_scheme["id"].as_i64().unwrap(),
        Some(worker_scheme),
    )
    .unwrap();
    assert_eq!(
        call(service, token(&worker_sub), GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let restricted = switched(&login(0), &worker_role);
    assert!(
        !restricted["user"]["capabilities"]["enabledModules"]
            .as_array()
            .unwrap()
            .contains(&json!("office.general"))
    );
    assert_eq!(
        call(
            service,
            token(&restricted),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        restricted["user"]["permissionSource"]["name"],
        "单位申请人方案"
    );
    let scheme_catalog = call(service, admin, LIST_PERMISSION_TEMPLATES, 0, None).unwrap();
    let referenced = scheme_catalog["templates"]
        .as_array()
        .unwrap()
        .iter()
        .find(|t| t["id"] == worker_role["permissionTemplateId"])
        .unwrap();
    assert_eq!(referenced["unitRoleReferenceCount"], 1);
    assert_eq!(referenced["accountReferenceCount"], 0);
    let manager_sub = switched(&login(2), &manager_role);
    let schemes = call(
        service,
        token(&manager_sub),
        GET_UNIT_PERMISSION_TEMPLATES,
        0,
        None,
    )
    .unwrap();
    let mut scheme_body = json!({"unitId":schemes["unitId"],"code":"unit-read","name":"本单位只读","description":"单位管理员维护","grants":[grant("office.general","approve")],"disabledModules":[],"oaAssignedOnly":false,"isActive":true,"expectedVersion":0});
    assert_eq!(
        call(
            service,
            token(&manager_sub),
            SAVE_UNIT_PERMISSION_TEMPLATE,
            0,
            Some(scheme_body.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    scheme_body["grants"] = json!([grant("office.general", "view")]);
    let unit_scheme = call(
        service,
        token(&manager_sub),
        SAVE_UNIT_PERMISSION_TEMPLATE,
        0,
        Some(scheme_body),
    )
    .unwrap();
    assert_eq!(unit_scheme["ownerUnitId"], schemes["unitId"]);
    let mut scoped_role = worker_role.clone();
    scoped_role["id"] = json!(0);
    scoped_role["expectedVersion"] = json!(0);
    scoped_role["name"] = json!("单位只读身份");
    scoped_role["permissionTemplateId"] = unit_scheme["id"].clone();
    let scoped_role = call(
        service,
        token(&manager_sub),
        SAVE_UNIT_ACCESS,
        0,
        Some(scoped_role),
    )
    .unwrap();
    let reader = switched(&login(0), &scoped_role);
    assert_eq!(reader["user"]["permissionSource"]["name"], "本单位只读");
    call(
        service,
        token(&reader),
        operation("general", "get"),
        id,
        None,
    )
    .unwrap();
    let mut escape = scoped_role.clone();
    escape["id"] = json!(0);
    escape["expectedVersion"] = json!(0);
    escape["companyCode"] = json!(hq);
    escape["appointmentId"] = people[0]["organization"]["primaryAssignmentId"].clone();
    assert_eq!(
        call(service, admin, SAVE_UNIT_ACCESS, 0, Some(escape))
            .unwrap_err()
            .status,
        Some(403)
    );
    let mut issuer = call(service, admin, LIST_USERS, 0, None).unwrap()["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["id"] == users[2]["id"])
        .unwrap()
        .clone();
    issuer["expectedVersion"] = issuer["versionNumber"].clone();
    issuer["disabledModules"] = json!(["office.general"]);
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        users[2]["id"].as_i64().unwrap(),
        Some(issuer),
    )
    .unwrap();
    assert_eq!(
        call(service, token(&reader), GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let reader = switched(&login(0), &scoped_role);
    assert_eq!(
        call(
            service,
            token(&reader),
            operation("general", "get"),
            id,
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let person = call(
        service,
        admin,
        GET_PERSONNEL,
        people[0]["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    let before = login(0);
    let promotion = json!({"appointmentId":worker_role["appointmentId"],"expectedVersion":person["versionNumber"],"note":"子公司兼任职调整为主职"});
    assert_eq!(
        call(
            service,
            token(&before),
            PROMOTE_PERSONNEL_APPOINTMENT,
            person["employee"]["id"].as_i64().unwrap(),
            Some(promotion.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(
            service,
            admin,
            PROMOTE_PERSONNEL_APPOINTMENT,
            person["employee"]["id"].as_i64().unwrap(),
            Some(promotion)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let person = call(
        service,
        admin,
        GET_PERSONNEL,
        people[3]["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    let person = call(service,admin,TRANSFER_PERSONNEL,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"effectiveDate":"2026-10-01","departmentId":main_department,"jobTitle":"总部主职","organization":{"additionalAssignments":[{"companyCode":sub,"departmentId":sub_department,"jobTitle":"子公司主职候选","startsOn":"2026-10-01","endsOn":"","functionalManagerEmployeeIds":[]}]},"note":"登记调任目标任职"}))).unwrap();
    let target_assignment = person["organization"]["additionalAssignments"][0]["id"].clone();
    let before = login(3);
    let promotion = json!({"appointmentId":target_assignment,"expectedVersion":person["versionNumber"],"note":"子公司兼任职调整为主职"});
    let promoted = call(
        service,
        admin,
        PROMOTE_PERSONNEL_APPOINTMENT,
        person["employee"]["id"].as_i64().unwrap(),
        Some(promotion),
    )
    .unwrap();
    assert_eq!(promoted["companyScope"], sub);
    assert_eq!(
        promoted["organization"]["primaryAssignmentId"],
        target_assignment
    );
    assert!(
        promoted["organization"]["additionalAssignments"]
            .as_array()
            .unwrap()
            .iter()
            .any(|a| a["id"] == person["organization"]["primaryAssignmentId"]
                && a["companyCode"] == hq)
    );
    assert_eq!(
        call(service, token(&before), GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let after = login(3);
    assert_eq!(after["user"]["companyScope"], sub);
    assert_eq!(after["user"]["departmentId"], sub_department);
    let history = call(
        service,
        admin,
        GET_PERSONNEL_HISTORY,
        person["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|h| h["previousPrimaryAssignmentId"]
                == person["organization"]["primaryAssignmentId"]
                && h["primaryAssignmentId"] == target_assignment)
    );
}
