//! Correction and managed descendants must agree across SQLite and PostgreSQL.
use super::oa_contract::call;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, admin: &str) {
    let suffix = nonce().unwrap();
    let directory = call(service, admin, GET_CURRENT_USER, 0, None).unwrap();
    let company = directory["companyScope"].as_str().unwrap();
    let codes: Vec<_> = ["ROOT", "CHILD", "LEAF", "SIBLING"]
        .iter()
        .map(|name| format!("{name}-{}", &suffix[..8]))
        .collect();
    let mut departments = vec![];
    for (i, code) in codes.iter().enumerate() {
        departments.push(call(service, admin, CREATE_ORGANIZATION_DEPARTMENT, 0, Some(json!({"code":code,"name":format!("部门{i}"),"companyCode":company,"parentCode":if i == 1 || i == 2 {Some(&codes[i-1])} else {None},"isActive":true}))).unwrap());
    }
    let create = |name: &str, department: &str| {
        call(service, admin, CREATE_PERSONNEL, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("{name}-{}", &suffix[..8]),"departmentId":department,"jobTitle":"普通员工","hireDate":"2026-09-01","onProbation":false,"employmentType":"FullTime","profile":{"fullName":name}}))).unwrap()
    };
    let manager = create("上级负责人", &codes[3]);
    let mut worker = create("下级员工", &codes[2]);
    let username = format!("scope-{}", &suffix[..8]);
    let account = call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":username,"fullName":"上级负责人","role":"OfficeEmployee","companyScope":company,"departmentId":codes[3],"isActive":true,"resetPassword":"Scope-Correction-2026","permissionGrants":[{"resourceKey":"office.people","action":"view","dataScope":"department"},{"resourceKey":"office.people","action":"view-details","dataScope":"department"}]}))).unwrap()["user"].clone();
    call(service, admin, LINK_PERSONNEL_ACCOUNT, manager["employee"]["id"].as_i64().unwrap(), Some(json!({"expectedVersion":manager["versionNumber"],"userId":account["id"],"expectedAccountVersion":account["versionNumber"]}))).unwrap();
    let login = || {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":username,"password":"Scope-Correction-2026"})),
        )
        .unwrap()
    };
    let session = login();
    let token = session["accessToken"].as_str().unwrap();
    let id = worker["employee"]["id"].as_i64().unwrap();
    assert_eq!(
        call(service, token, GET_PERSONNEL, id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let save_department = |row: &Value, manager: Value, parent: Value| -> Value {
        let mut body = row.clone();
        body["expectedVersion"] = row["versionNumber"].clone();
        body["managerEmployeeId"] = manager;
        body["parentCode"] = parent;
        serde_json::from_slice(
            &service
                .dispatch(
                    UPDATE_ORGANIZATION_DEPARTMENT,
                    &[("code", row["code"].as_str().unwrap().into())],
                    &[],
                    Some(body),
                    admin,
                )
                .unwrap(),
        )
        .unwrap()
    };
    departments[0] = save_department(
        &departments[0],
        manager["employee"]["id"].clone(),
        Value::Null,
    );
    assert_eq!(
        call(service, token, GET_PERSONNEL, id, None).unwrap()["employee"]["id"],
        id
    );
    let refreshed = login();
    for code in &codes {
        assert!(
            refreshed["user"]["effectiveDepartmentIds"]
                .as_array()
                .unwrap()
                .contains(&json!(code))
        );
    }
    let page: Value = serde_json::from_slice(
        &service
            .dispatch(
                LIST_PERSONNEL,
                &[],
                &[("keyword", "下级员工".into()), ("pageSize", "1".into())],
                None,
                token,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(page["totalCount"], 1);
    assert_eq!(page["items"][0]["id"], id);
    // Merely belonging to the sibling department does not grant its descendants.
    departments[1] = save_department(&departments[1], Value::Null, json!(codes[3]));
    assert_eq!(
        call(service, token, GET_PERSONNEL, id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    departments[1] = save_department(&departments[1], Value::Null, json!(codes[0]));
    assert_eq!(
        call(service, token, GET_PERSONNEL, id, None).unwrap()["employee"]["id"],
        id
    );
    let correction = |person: &Value, organization: Value| json!({"expectedVersion":person["versionNumber"],"profile":person["profile"],"employmentType":person["employmentType"],"correctionNote":"核对原登记","registration":{"employeeNumber":person["employee"]["employeeNumber"],"departmentId":person["employee"]["departmentId"],"jobTitle":person["employee"]["jobTitle"],"hireDate":person["hireDate"],"onProbation":false,"organization":organization}});
    let organization = json!({"managerEmployeeId":manager["employee"]["id"],"functionalManagerEmployeeIds":[],"additionalAssignments":[{"departmentId":codes[0],"jobTitle":"兼职协调员","functionalManagerEmployeeIds":[]}]});
    let body = correction(&worker, organization.clone());
    assert_eq!(
        call(service, token, UPDATE_PERSONNEL, id, Some(body.clone()))
            .unwrap_err()
            .status,
        Some(403)
    );
    let mut bad = body.clone();
    bad["correctionNote"] = json!("");
    assert_eq!(
        call(service, admin, UPDATE_PERSONNEL, id, Some(bad))
            .unwrap_err()
            .status,
        Some(400)
    );
    for invalid in [
        json!({"managerEmployeeId":id}),
        json!({"additionalAssignments":[{"departmentId":codes[2],"jobTitle":"重复主职"}]}),
        json!({"additionalAssignments":[{"departmentId":"missing","jobTitle":"不存在"}]}),
    ] {
        assert_eq!(
            call(
                service,
                admin,
                UPDATE_PERSONNEL,
                id,
                Some(correction(&worker, invalid))
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    assert_eq!(
        call(service, admin, GET_PERSONNEL, id, None).unwrap()["versionNumber"],
        worker["versionNumber"]
    );
    worker = call(service, admin, UPDATE_PERSONNEL, id, Some(body.clone())).unwrap();
    assert_eq!(
        worker["organization"]["additionalAssignments"][0]["departmentName"],
        "部门0"
    );
    assert_eq!(
        worker["organization"]["managerEmployeeId"],
        manager["employee"]["id"]
    );
    assert_eq!(
        call(service, admin, UPDATE_PERSONNEL, id, Some(body))
            .unwrap_err()
            .status,
        Some(409)
    );
    // A normal profile update must preserve the organization, including when omitted.
    let mut profile = correction(&worker, json!({}));
    profile["registration"] = Value::Null;
    worker = call(service, admin, UPDATE_PERSONNEL, id, Some(profile)).unwrap();
    assert_eq!(
        worker["organization"]["additionalAssignments"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
    worker = call(
        service,
        admin,
        UPDATE_PERSONNEL,
        id,
        Some(correction(&worker, json!({}))),
    )
    .unwrap();
    assert!(
        worker["organization"]["additionalAssignments"]
            .as_array()
            .unwrap()
            .is_empty()
    );
    save_department(&departments[0], Value::Null, Value::Null);
    assert_eq!(
        call(service, token, GET_PERSONNEL, id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
}
