//! Public permission/identity contract shared by the SQLite and PostgreSQL service tests.
use super::oa_contract::{call, operation, workflow};
use super::organization_workflow_contract::policy;
use export_doc_engine::{contracts, engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn read(service: &NativeService, token: &str, op: Operation, query: &[(&str, String)]) -> Value {
    let value: Value = serde_json::from_slice(
        &service
            .dispatch(op, &[], query, None, token)
            .unwrap_or_else(|e| panic!("{} read failed: {e:?}", op.id)),
    )
    .unwrap();
    export_doc_contracts::validation::response(op.id, &value).unwrap();
    value
}
fn login(service: &NativeService, user: &Value) -> Value {
    call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":user["username"],"password":"Permission-Identity-2026"})),
    )
    .unwrap()
}
fn token(session: &Value) -> &str {
    session["accessToken"].as_str().unwrap()
}

pub fn exercise(service: &NativeService, root: &str) {
    let suffix = &nonce().unwrap()[..8];
    let unit = format!("PERM-{suffix}");
    let other_unit = format!("PERM-OTHER-{suffix}");
    for code in [&unit, &other_unit] {
        call(
            service,
            root,
            CREATE_ORGANIZATION_COMPANY,
            0,
            Some(json!({"code":code,"name":code,"isActive":true})),
        )
        .unwrap();
        for department in ["A", "B"] {
            call(
                service,
                root,
                CREATE_ORGANIZATION_DEPARTMENT,
                0,
                Some(
                    json!({"code":department,"name":department,"companyCode":code,"isActive":true}),
                ),
            )
            .unwrap();
        }
    }
    let mut administrators = vec![];
    for (index, company) in [&unit, &other_unit].into_iter().enumerate() {
        let user = call(service, root, CREATE_USER_ACCOUNT, 0, Some(json!({"username":format!("perm-admin-{index}-{suffix}"),"fullName":"隔离单位管理员","role":"Admin","companyScope":company,"departmentId":"A","isActive":true,"resetPassword":"Permission-Identity-2026"}))).unwrap()["user"].clone();
        administrators.push(login(service, &user));
    }
    let admin = token(&administrators[0]);
    let other_admin = token(&administrators[1]);
    let mut users = vec![];
    for index in 0..1 {
        users.push(call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":format!("perm-worker-{index}-{suffix}"),"fullName":format!("权限成员{index}"),"role":"OfficeEmployee","companyScope":unit,"departmentId":"A","isActive":true,"resetPassword":"Permission-Identity-2026"}))).unwrap()["user"].clone());
    }
    let number = format!("PERM-P-{suffix}");
    let person = call(service, admin, CREATE_PERSONNEL, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":number,"departmentId":"A","jobTitle":"普通员工","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":"权限成员0"}}))).unwrap();
    call(service, admin, LINK_PERSONNEL_ACCOUNT, person["employee"]["id"].as_i64().unwrap(), Some(json!({"expectedVersion":person["versionNumber"],"userId":users[0]["id"],"expectedAccountVersion":users[0]["versionNumber"]}))).unwrap();
    let members = read(
        service,
        admin,
        GET_UNIT_MEMBERS,
        &[("companyCode", unit.clone()), ("keyword", number)],
    );
    let appointment = members["items"][0]["appointments"]
        .as_array()
        .unwrap()
        .iter()
        .find(|a| a["isPrimary"] == true)
        .unwrap();
    let employee = contracts::contract()["permissions"]["roles"]
        .as_array()
        .unwrap()
        .iter()
        .find(|role| role["code"] == "OfficeEmployee")
        .unwrap();
    let scheme = call(service, admin, CREATE_PERMISSION_TEMPLATE, 0, Some(json!({"code":format!("perm-employee-{suffix}"),"name":"直接复用普通员工预置","grants":employee["grants"],"disabledModules":[],"oaAssignedOnly":false,"isActive":true,"expectedVersion":0}))).unwrap();
    let scheme = workflow::publish_scheme(service, admin, scheme);
    let role = call(service, admin, SAVE_UNIT_ACCESS, 0, Some(json!({"userId":users[0]["id"],"companyCode":unit,"name":"普通员工单位身份","mode":"Business","appointmentId":appointment["id"],"permissionTemplateId":scheme["id"],"permissionSchemeVersion":scheme["publishedVersion"],"permissionGrants":null,"management":{},"disabledModules":[],"oaAssignedOnly":false,"includeDescendants":false,"startsAt":"","endsAt":"","isActive":true,"expectedVersion":0}))).unwrap();
    let login_worker = login(service, &users[0]);
    let worker = call(
        service,
        token(&login_worker),
        SWITCH_UNIT_CONTEXT,
        0,
        Some(json!({"contextId":format!("unit:{}:{unit}",role["id"])})),
    )
    .unwrap();
    let announcement = |publisher: &str, title: &str, department: &str| {
        let now = chrono::Utc::now();
        let draft = call(service, publisher, CREATE_ANNOUNCEMENT, 0, Some(json!({"requestKey":nonce().unwrap(),"title":title,"body":"隔离公开公告","audienceDepartment":department,"isPinned":false,"startsAt":(now-chrono::Duration::hours(1)).to_rfc3339(),"expiresAt":(now+chrono::Duration::days(30)).to_rfc3339()}))).unwrap();
        call(
            service,
            publisher,
            PUBLISH_ANNOUNCEMENT,
            draft["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":draft["versionNumber"],"note":"发布验收"})),
        )
        .unwrap()
    };
    let visible = announcement(admin, "本单位全员公告", "");
    let audience_hidden = announcement(admin, "另一部门公告", "B");
    let unit_hidden = announcement(other_admin, "另一单位公告", "");
    let announcements = read(service, token(&worker), LIST_ANNOUNCEMENTS, &[]);
    let ids: Vec<_> = announcements["items"]
        .as_array()
        .unwrap()
        .iter()
        .map(|row| row["id"].clone())
        .collect();
    assert!(ids.contains(&visible["id"]));
    assert!(!ids.contains(&audience_hidden["id"]) && !ids.contains(&unit_hidden["id"]));
    let (reviewer, reviewer_token) = workflow::reviewer(service, admin);
    policy(
        service,
        admin,
        "general",
        workflow::rule(
            "general",
            "",
            json!([workflow::node("review", &[], "Named", json!([reviewer]))]),
        ),
    );
    let request = call(service, token(&worker), operation("general", "create"), 0, Some(json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"单位身份通知验收","reason":"仅向实际收件人发送审批动态","category":"IT"}))).unwrap();
    let request = call(
        service,
        token(&worker),
        operation("general", "submit"),
        request["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":request["versionNumber"]})),
    )
    .unwrap();
    call(
        service,
        &reviewer_token,
        operation("general", "reject"),
        request["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":request["versionNumber"],"note":"通知验收拒绝"})),
    )
    .unwrap();
    let notices = read(service, token(&worker), LIST_NOTIFICATIONS, &[]);
    let other_notices = read(service, &reviewer_token, LIST_NOTIFICATIONS, &[]);
    assert!(notices["totalCount"].as_i64().unwrap() > 0);
    let other_notice = &other_notices["items"][0];
    assert!(
        notices["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|row| row["id"] != other_notice["id"])
    );
    assert!(matches!(
        call(
            service,
            token(&worker),
            READ_NOTIFICATION,
            other_notice["id"].as_i64().unwrap(),
            Some(json!({}))
        )
        .unwrap_err()
        .status,
        Some(403 | 404)
    ));
    for management in [json!({"workflows":true}), json!({"appointments":true})] {
        let managed = call(service, admin, SAVE_UNIT_ACCESS, 0, Some(json!({"userId":users[0]["id"],"companyCode":unit,"name":format!("仅管理{management}"),"mode":"Administration","appointmentId":"","permissionGrants":[],"management":management,"disabledModules":[],"oaAssignedOnly":false,"includeDescendants":false,"startsAt":"","endsAt":"","isActive":true,"expectedVersion":0}))).unwrap();
        let session = login(service, &users[0]);
        let session = call(
            service,
            token(&session),
            SWITCH_UNIT_CONTEXT,
            0,
            Some(json!({"contextId":format!("unit:{}:{unit}",managed["id"])})),
        )
        .unwrap();
        assert!(
            read(
                service,
                token(&session),
                LIST_ORGANIZATION_MANAGERS,
                &[("companyCode", unit.clone())]
            )["items"]
                .is_array()
        );
        assert_eq!(
            call(
                service,
                token(&session),
                CREATE_ORGANIZATION_DEPARTMENT,
                0,
                Some(
                    json!({"code":"FORBIDDEN","name":"禁止扩权","companyCode":unit,"isActive":true})
                )
            )
            .unwrap_err()
            .status,
            Some(403)
        );
        assert_eq!(
            call(
                service,
                token(&session),
                GET_PERSONNEL,
                person["employee"]["id"].as_i64().unwrap(),
                None
            )
            .unwrap_err()
            .status,
            Some(403)
        );
    }
}
