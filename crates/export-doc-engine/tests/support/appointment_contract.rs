//! Appointment policy and references run through the public service on both stores.
use super::oa_contract::{call, operation, workflow};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};
#[path = "resource_contract.rs"]
mod resources;

fn directory(service: &NativeService, token: &str, company: &str) -> Value {
    serde_json::from_slice(
        &service
            .dispatch(
                GET_UNIT_MEMBERS,
                &[],
                &[("companyCode", company.into()), ("pageSize", "100".into())],
                None,
                token,
            )
            .unwrap(),
    )
    .unwrap()
}
fn member(service: &NativeService, token: &str, company: &str, id: i64) -> Value {
    directory(service, token, company)["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|m| m["personId"] == id)
        .unwrap()
        .clone()
}
fn save_member(
    service: &NativeService,
    token: &str,
    company: &str,
    person: &Value,
    appointments: Value,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let current = call(
        service,
        token,
        GET_PERSONNEL,
        person["personId"].as_i64().unwrap(),
        None,
    )?;
    let primary = appointments
        .as_array()
        .unwrap()
        .iter()
        .find(|a| a["isPrimary"] == true)
        .unwrap();
    let mut organization = current["organization"].clone();
    let mut additional = organization["additionalAssignments"]
        .as_array()
        .unwrap()
        .iter()
        .filter(|a| a["companyCode"] != company)
        .cloned()
        .collect::<Vec<_>>();
    additional.extend(
        appointments
            .as_array()
            .unwrap()
            .iter()
            .filter(|a| a["isPrimary"] != true)
            .cloned(),
    );
    organization["additionalAssignments"] = json!(additional);
    for key in [
        "managerEmployeeId",
        "managerAssignmentId",
        "functionalManagerEmployeeIds",
        "functionalManagerAssignmentIds",
    ] {
        organization[key] = primary[key].clone();
    }
    let body = json!({"expectedVersion":person["versionNumber"],"effectiveDate":"2026-10-01","departmentId":primary["departmentId"],"jobTitle":primary["jobTitle"],"organization":organization,"note":"公开任职制度与生命周期验收"});
    workflow::personnel_change(
        service,
        token,
        "Transfer",
        person["personId"].as_i64().unwrap(),
        Some(body),
    )?;
    Ok(member(
        service,
        token,
        company,
        person["personId"].as_i64().unwrap(),
    ))
}
pub fn exercise(service: &NativeService, root: &str) {
    let suffix = &nonce().unwrap()[..8];
    let company = format!("APPOINTMENT-{suffix}");
    let department = format!("AP-A-{suffix}");
    let additional = format!("AP-B-{suffix}");
    let unused = format!("AP-C-{suffix}");
    call(
        service,
        root,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"任职制度验收单位","isActive":true})),
    )
    .unwrap();
    for code in [&department, &additional, &unused] {
        call(
            service,
            root,
            CREATE_ORGANIZATION_DEPARTMENT,
            0,
            Some(json!({"code":code,"name":code,"companyCode":company,"isActive":true})),
        )
        .unwrap();
    }
    let mut users = vec![];
    for i in 0..4 {
        let grants = if i == 2 {
            json!(["office.rooms","office.supplies"].into_iter().flat_map(|resource|["view","approve"].map(move |action|json!({"resourceKey":resource,"action":action,"dataScope":"own"}))).collect::<Vec<_>>())
        } else if i == 3 {
            json!(["office.rooms","office.supplies"].into_iter().flat_map(|resource|["view","create","cancel"].map(move |action|json!({"resourceKey":resource,"action":action,"dataScope":"own"}))).collect::<Vec<_>>())
        } else {
            json!([])
        };
        users.push(call(service,root,CREATE_USER_ACCOUNT,0,Some(json!({"username":format!("appointment-{suffix}-{i}"),"fullName":format!("匿名任职验收{i}"),"role":if i==0 {"Admin"} else {"OfficeEmployee"},"companyScope":company,"departmentId":department,"permissionGrants":grants,"isActive":true,"resetPassword":"Appointment-Contract-2026"}))).unwrap()["user"].clone());
    }
    let login = |i: usize| {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":users[i]["username"],"password":"Appointment-Contract-2026"})),
        )
        .unwrap()
    };
    let admin_login = login(0);
    let admin = admin_login["accessToken"].as_str().unwrap();
    let role=call(service,admin,SAVE_UNIT_ACCESS,0,Some(json!({"userId":users[1]["id"],"companyCode":company,"name":"仅公开任职维护","mode":"Administration","appointmentId":"","permissionGrants":[],"disabledModules":[],"oaAssignedOnly":false,"management":{"organization":false,"appointments":true,"workflows":false,"roles":false},"includeDescendants":false,"startsAt":"","endsAt":"","isActive":true,"expectedVersion":0}))).unwrap();
    let manager_login = login(1);
    let management = call(
        service,
        manager_login["accessToken"].as_str().unwrap(),
        SWITCH_UNIT_CONTEXT,
        0,
        Some(json!({"contextId":format!("unit:{}:{company}",role["id"])})),
    )
    .unwrap();
    let manager = management["accessToken"].as_str().unwrap();
    let mut people = vec![];
    for i in [2, 3] {
        let person=call(service,admin,CREATE_PERSONNEL,0,Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("AP-{suffix}-{i}"),"departmentId":department,"jobTitle":"长期主职","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":users[i]["fullName"]},"organization":{"additionalAssignments":if i==3 {json!([{"companyCode":company,"departmentId":additional,"jobTitle":"长期兼任","startsOn":"","endsOn":"","managerEmployeeId":null,"functionalManagerEmployeeIds":[]}])}else{json!([])}}}))).unwrap();
        people.push(call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":users[i]["id"],"expectedAccountVersion":users[i]["versionNumber"]}))).unwrap());
    }
    let id = people[1]["employee"]["id"].as_i64().unwrap();
    assert!(
        export_doc_contracts::openapi::document()["paths"]["/api/organization/unit-members"]
            .get("post")
            .is_none()
    );
    let current = member(service, manager, &company, id);
    assert!(current.get("profile").is_none());
    let mut proposed = current["appointments"].clone();
    proposed[0]["jobTitle"] = json!("不可绕过审批的岗位变更");
    assert_eq!(
        save_member(service, manager, &company, &current, proposed.clone())
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        member(service, admin, &company, id)["appointments"],
        current["appointments"]
    );
    let corrected = save_member(service, admin, &company, &current, proposed).unwrap();
    assert_eq!(
        corrected["appointments"][0]["jobTitle"],
        "不可绕过审批的岗位变更"
    );
    let current = member(service, manager, &company, id);
    let mut proposed = current["appointments"].clone();
    proposed[0]["jobTitle"] = json!("同一制度下的直接维护");
    let current = save_member(service, admin, &company, &current, proposed).unwrap();
    let appointment = current["appointments"][1]["id"].clone();
    let mut role=call(service,admin,SAVE_UNIT_ACCESS,0,Some(json!({"userId":users[3]["id"],"companyCode":company,"name":"兼任业务身份","mode":"Business","appointmentId":appointment,"permissionGrants":[{"resourceKey":"office.general","action":"view","dataScope":"own"}],"disabledModules":[],"oaAssignedOnly":false,"management":{"organization":false,"appointments":false,"workflows":false,"roles":false},"includeDescendants":false,"startsAt":"","endsAt":"","isActive":true,"expectedVersion":0}))).unwrap();
    role["expectedVersion"] = role["versionNumber"].clone();
    role["isActive"] = json!(false);
    call(service, admin, SAVE_UNIT_ACCESS, 0, Some(role)).unwrap();
    let current = member(service, manager, &company, id);
    assert_eq!(current["appointments"][1]["canRemove"], false);
    assert!(
        current["appointments"][1]["removalReason"]
            .as_str()
            .unwrap()
            .contains("已停用")
    );
    let mut proposed = current["appointments"].clone();
    proposed.as_array_mut().unwrap().remove(1);
    let blocked = save_member(service, admin, &company, &current, proposed).unwrap_err();
    assert_eq!(blocked.status, Some(409));
    assert!(blocked.message.contains("单位授权"));
    let mut proposed = current["appointments"].clone();
    let today: chrono::NaiveDate = admin_login["user"]["businessDate"]
        .as_str()
        .unwrap()
        .parse()
        .unwrap();
    proposed[1]["endsOn"] = json!(today.pred_opt().unwrap().to_string());
    let ended = save_member(service, admin, &company, &current, proposed).unwrap();
    assert_eq!(ended["appointments"][1]["id"], appointment);
    assert_eq!(ended["appointments"][1]["isEffective"], false);
    let mut proposed = ended["appointments"].clone();
    proposed.as_array_mut().unwrap().push(json!({"companyCode":company,"departmentId":unused,"jobTitle":"误建待核对","startsOn":"","endsOn":"","isPrimary":false,"managerEmployeeId":null,"functionalManagerEmployeeIds":[]}));
    let added = save_member(service, admin, &company, &ended, proposed).unwrap();
    let added_appointment = added["appointments"]
        .as_array()
        .unwrap()
        .iter()
        .find(|a| a["jobTitle"] == "误建待核对")
        .unwrap();
    assert_eq!(added_appointment["canRemove"], true);
    let mut proposed = added["appointments"].clone();
    proposed
        .as_array_mut()
        .unwrap()
        .retain(|a| a["id"] != added_appointment["id"]);
    let saved = save_member(service, admin, &company, &added, proposed).unwrap();
    assert_eq!(saved["appointments"].as_array().unwrap().len(), 2);
    let mut proposed = saved["appointments"].clone();
    proposed.as_array_mut().unwrap().push(json!({"companyCode":company,"departmentId":additional,"jobTitle":"长期兼任","startsOn":today.to_string(),"endsOn":"","isPrimary":false,"managerEmployeeId":null,"functionalManagerEmployeeIds":[]}));
    let renewed = save_member(service, admin, &company, &saved, proposed).unwrap();
    let new_period = renewed["appointments"]
        .as_array()
        .unwrap()
        .iter()
        .find(|a| a["departmentId"] == additional && a["isEffective"] == true)
        .unwrap();
    assert_ne!(
        new_period["id"], appointment,
        "new appointment periods cannot inherit an ended identity"
    );
    assert!(
        renewed["appointments"]
            .as_array()
            .unwrap()
            .iter()
            .any(|a| a["id"] == appointment && a["isEffective"] == false)
    );
    let applicant = login(3);
    let reviewer = login(2);
    resources::exercise(
        service,
        admin,
        applicant["accessToken"].as_str().unwrap(),
        reviewer["accessToken"].as_str().unwrap(),
        &users,
        &company,
        &people[1],
    );
}
