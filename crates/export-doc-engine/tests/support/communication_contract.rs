use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};

fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    query: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let parameters = if id > 0 {
        vec![("id", id.to_string())]
    } else {
        vec![]
    };
    let body = body.map(|v| contracts::overlay(contracts::object(op.id, true), &v));
    let value: Value =
        serde_json::from_slice(&service.dispatch(op, &parameters, query, body, token)?).unwrap();
    export_doc_contracts::validation::response(op.id, &value)
        .unwrap_or_else(|e| panic!("{}: {e}", op.id));
    Ok(value)
}
fn action(service: &NativeService, token: &str, op: Operation, row: &Value) -> Value {
    call(
        service,
        token,
        op,
        row["id"].as_i64().unwrap(),
        &[],
        Some(json!({"expectedVersion":row["versionNumber"],"note":"验收操作原因"})),
    )
    .unwrap()
}
fn count(service: &NativeService, token: &str) -> i64 {
    call(service, token, GET_NOTIFICATION_UNREAD_COUNT, 0, &[], None).unwrap()["unreadCount"]
        .as_i64()
        .unwrap()
}

/// Runs against both SQLite and PostgreSQL using only the public service boundary.
pub fn exercise(service: &NativeService, admin: &str) -> i64 {
    let suffix = nonce().unwrap()[..8].to_owned();
    let create = |op, body| call(service, admin, op, 0, &[], Some(body)).unwrap();
    let company = format!("COMMS-{suffix}");
    let department = format!("COMMS-D-{suffix}");
    create(
        CREATE_ORGANIZATION_COMPANY,
        json!({"code":company,"name":"隔离公司","isActive":true}),
    );
    create(
        CREATE_ORGANIZATION_DEPARTMENT,
        json!({"code":department,"companyCode":company,"name":"隔离部门","isActive":true}),
    );
    let login = |username: &str| {
        call(
            service,
            "",
            LOGIN,
            0,
            &[],
            Some(json!({"username":username,"password":"Communication-2026"})),
        )
        .unwrap()["accessToken"]
            .as_str()
            .unwrap()
            .to_owned()
    };
    let mut users = vec![];
    let mut tokens = vec![];
    for (name, role, c, d) in [
        ("reader", "OfficeEmployee", "DEFAULT", "GENERAL"),
        ("manager", "OfficeManager", "DEFAULT", "GENERAL"),
        ("other", "Admin", company.as_str(), department.as_str()),
    ] {
        let username = format!("{name}-{suffix}");
        users.push(create(CREATE_USER_ACCOUNT,json!({"username":username,"fullName":name,"role":role,"companyScope":c,"departmentId":d,"isActive":true,"resetPassword":"Communication-2026"}))["user"].clone());
        tokens.push(login(&username));
    }
    let reader = &tokens[0];
    let manager = &tokens[1];
    let other = &tokens[2];
    let now = chrono::Utc::now();
    let body = json!({"requestKey":nonce().unwrap(),"title":"制度更新与放假安排","body":"第一行\n第二行 <script>保持纯文字</script>","audienceDepartment":"GENERAL","isPinned":true,"startsAt":(now-chrono::Duration::hours(1)).to_rfc3339(),"expiresAt":(now+chrono::Duration::days(30)).to_rfc3339()});
    let mut row = create(CREATE_ANNOUNCEMENT, body.clone());
    let id = row["id"].as_i64().unwrap();
    assert_eq!(create(CREATE_ANNOUNCEMENT, body.clone())["id"], id);
    let mut injected = body.clone();
    injected["title"] = json!("重试不同内容");
    assert_eq!(
        call(service, admin, CREATE_ANNOUNCEMENT, 0, &[], Some(injected))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        call(service, reader, GET_ANNOUNCEMENT, id, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        call(service, reader, MANAGE_ANNOUNCEMENTS, 0, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let bytes = b"%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n";
    let mut disposable = body.clone();
    disposable["requestKey"] = json!(nonce().unwrap());
    let disposable = create(CREATE_ANNOUNCEMENT, disposable);
    let disposable_id = disposable["id"].as_i64().unwrap();
    let disposable = service
        .upload(
            UPLOAD_ANNOUNCEMENT_ATTACHMENT,
            &[("id", disposable_id.to_string())],
            json!({"expectedVersion":disposable["versionNumber"]}),
            "draft.pdf",
            bytes,
            admin,
        )
        .unwrap();
    for (token, version, note, status) in [
        (reader, disposable["versionNumber"].clone(), "误录", 403),
        (other, disposable["versionNumber"].clone(), "误录", 403),
        (manager, json!(1), "误录", 409),
        (manager, disposable["versionNumber"].clone(), "", 400),
    ] {
        assert_eq!(
            call(
                service,
                token,
                DELETE_ANNOUNCEMENT,
                disposable_id,
                &[],
                Some(json!({"expectedVersion":version,"note":note}))
            )
            .unwrap_err()
            .status,
            Some(status)
        );
    }
    assert_eq!(
        service
            .download_file(
                DOWNLOAD_ANNOUNCEMENT_ATTACHMENT,
                &[
                    ("id", disposable_id.to_string()),
                    (
                        "attachmentId",
                        disposable["attachments"][0]["id"].to_string()
                    )
                ],
                admin
            )
            .unwrap()
            .content,
        bytes
    );
    let disposable = action(service, manager, ARCHIVE_ANNOUNCEMENT, &disposable);
    assert_eq!(
        action(service, manager, DELETE_ANNOUNCEMENT, &disposable)["success"],
        true
    );
    assert_eq!(
        call(service, admin, GET_ANNOUNCEMENT, disposable_id, &[], None)
            .unwrap_err()
            .status,
        Some(404)
    );
    row = service
        .upload(
            UPLOAD_ANNOUNCEMENT_ATTACHMENT,
            &[("id", id.to_string())],
            json!({"expectedVersion":row["versionNumber"]}),
            "notice.pdf",
            bytes,
            admin,
        )
        .unwrap();
    let file = row["attachments"][0]["id"].as_i64().unwrap();
    let path = [("id", id.to_string()), ("attachmentId", file.to_string())];
    row = action(service, admin, PUBLISH_ANNOUNCEMENT, &row);
    assert_eq!(row["publishVersion"], 1);
    assert_eq!(
        call(
            service,
            admin,
            DELETE_ANNOUNCEMENT,
            id,
            &[],
            Some(json!({"expectedVersion":row["versionNumber"],"note":"不可删除已发布"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        service
            .download_file(DOWNLOAD_ANNOUNCEMENT_ATTACHMENT, &path, reader)
            .unwrap()
            .content,
        bytes
    );
    assert_eq!(
        service
            .download_file(DOWNLOAD_ANNOUNCEMENT_ATTACHMENT, &path, other)
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    assert_eq!(
        call(service, other, GET_ANNOUNCEMENT, id, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        call(service, other, LIST_ANNOUNCEMENTS, 0, &[], None).unwrap()["totalCount"],
        0
    );
    assert_eq!(
        call(service, reader, LIST_ANNOUNCEMENT_RECEIPTS, id, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let read = || {
        call(
            service,
            reader,
            CONFIRM_ANNOUNCEMENT_READ,
            id,
            &[],
            Some(json!({"publishVersion":1})),
        )
        .unwrap()
    };
    assert!(!read()["readAt"].as_str().unwrap().is_empty());
    read();
    assert_eq!(
        call(service, admin, LIST_ANNOUNCEMENT_RECEIPTS, id, &[], None).unwrap()["totalCount"],
        1
    );
    assert_eq!(
        call(
            service,
            reader,
            LIST_ANNOUNCEMENTS,
            0,
            &[("unreadOnly", "true".into())],
            None
        )
        .unwrap()["totalCount"],
        0
    );
    assert_eq!(
        service
            .upload(
                UPLOAD_ANNOUNCEMENT_ATTACHMENT,
                &[("id", id.to_string())],
                json!({"expectedVersion":row["versionNumber"]}),
                "notice.pdf",
                bytes,
                admin
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let old = row.clone();
    row = action(service, admin, WITHDRAW_ANNOUNCEMENT, &row);
    assert_eq!(
        call(
            service,
            admin,
            DELETE_ANNOUNCEMENT,
            id,
            &[],
            Some(json!({"expectedVersion":row["versionNumber"],"note":"不可删除已撤回"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        call(service, reader, GET_ANNOUNCEMENT, id, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let mut edit = body.clone();
    edit["expectedVersion"] = old["versionNumber"].clone();
    edit["body"] = json!("修订后的制度");
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_ANNOUNCEMENT,
            id,
            &[],
            Some(edit.clone())
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    edit["expectedVersion"] = row["versionNumber"].clone();
    row = call(service, admin, UPDATE_ANNOUNCEMENT, id, &[], Some(edit)).unwrap();
    row = action(service, admin, PUBLISH_ANNOUNCEMENT, &row);
    assert_eq!(row["publishVersion"], 2);
    assert_eq!(
        call(service, reader, GET_ANNOUNCEMENT, id, &[], None).unwrap()["readAt"],
        ""
    );
    assert_eq!(
        call(
            service,
            reader,
            CONFIRM_ANNOUNCEMENT_READ,
            id,
            &[],
            Some(json!({"publishVersion":1}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    call(
        service,
        reader,
        CONFIRM_ANNOUNCEMENT_READ,
        id,
        &[],
        Some(json!({"publishVersion":2})),
    )
    .unwrap();
    assert_eq!(
        call(service, admin, LIST_ANNOUNCEMENT_RECEIPTS, id, &[], None).unwrap()["totalCount"],
        2
    );
    // Scheduled and expired announcements are excluded before paging/counting.
    let mut future = body.clone();
    future["requestKey"] = json!(nonce().unwrap());
    future["startsAt"] = json!((now + chrono::Duration::days(1)).to_rfc3339());
    let future = action(
        service,
        admin,
        PUBLISH_ANNOUNCEMENT,
        &create(CREATE_ANNOUNCEMENT, future),
    );
    assert_eq!(
        call(
            service,
            reader,
            GET_ANNOUNCEMENT,
            future["id"].as_i64().unwrap(),
            &[],
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let mut expired = body.clone();
    expired["requestKey"] = json!(nonce().unwrap());
    expired["startsAt"] = json!((now - chrono::Duration::days(2)).to_rfc3339());
    expired["expiresAt"] = json!((now - chrono::Duration::days(1)).to_rfc3339());
    let expired = create(CREATE_ANNOUNCEMENT, expired);
    assert_eq!(
        call(
            service,
            admin,
            PUBLISH_ANNOUNCEMENT,
            expired["id"].as_i64().unwrap(),
            &[],
            Some(json!({"expectedVersion":expired["versionNumber"],"note":""}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let page = call(
        service,
        reader,
        LIST_ANNOUNCEMENTS,
        0,
        &[("pageSize", "1".into())],
        None,
    )
    .unwrap();
    assert_eq!(page["totalCount"], 1);
    assert_eq!(page["items"][0]["id"], id);
    // A team applicant has a real employee link; SQLite exercises the same rules.
    let employee = create(
        CREATE_PERSONNEL,
        json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("COMMS-{suffix}"),"departmentId":"GENERAL","jobTitle":"业务","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":"reader"}}),
    );
    let register = call(service, admin, GET_CURRENT_USER, 0, &[], None).unwrap()["capabilities"]["usesOfficeRegister"]
        == true;
    if !register {
        call(service,admin,LINK_PERSONNEL_ACCOUNT,employee["employee"]["id"].as_i64().unwrap(),&[],Some(json!({"expectedVersion":employee["versionNumber"],"userId":users[0]["id"],"expectedAccountVersion":users[0]["versionNumber"]}))).unwrap();
    }
    let reader = login(users[0]["username"].as_str().unwrap());
    let request=call(service,&reader,CREATE_GENERAL_REQUEST,0,&[],Some(json!({"requestKey":nonce().unwrap(),"employeeId":if register { employee["employee"]["id"].clone() } else { Value::Null },"title":"通知事务验收","reason":"设备维修","category":"IT"}))).unwrap();
    let request = action(service, &reader, SUBMIT_GENERAL_REQUEST, &request);
    assert_eq!(count(service, manager), 1);
    assert_eq!(count(service, &reader), 0);
    assert_eq!(count(service, other), 0);
    let request = action(service, manager, REJECT_GENERAL_REQUEST, &request);
    assert_eq!(count(service, &reader), 1);
    let request = action(service, &reader, SUBMIT_GENERAL_REQUEST, &request);
    let stale = request.clone();
    let request = action(service, manager, APPROVE_GENERAL_REQUEST, &request);
    assert_eq!(
        call(
            service,
            manager,
            APPROVE_GENERAL_REQUEST,
            stale["id"].as_i64().unwrap(),
            &[],
            Some(json!({"expectedVersion":stale["versionNumber"],"note":"重复审批"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(count(service, &reader), 2);
    action(service, manager, COMPLETE_GENERAL_REQUEST, &request);
    assert_eq!(count(service, &reader), 3);
    let inbox = call(
        service,
        &reader,
        LIST_NOTIFICATIONS,
        0,
        &[("pageSize", "1".into())],
        None,
    )
    .unwrap();
    assert_eq!(inbox["totalCount"], 3);
    assert_eq!(inbox["items"].as_array().unwrap().len(), 1);
    let notification = inbox["items"][0]["id"].as_i64().unwrap();
    assert_eq!(
        call(service, manager, READ_NOTIFICATION, notification, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    call(service, &reader, READ_NOTIFICATION, notification, &[], None).unwrap();
    call(service, &reader, READ_NOTIFICATION, notification, &[], None).unwrap();
    assert_eq!(count(service, &reader), 2);
    call(service, &reader, READ_ALL_NOTIFICATIONS, 0, &[], None).unwrap();
    assert_eq!(count(service, &reader), 0);
    assert_eq!(
        call(
            service,
            &reader,
            GET_GENERAL_REQUEST,
            request["id"].as_i64().unwrap(),
            &[],
            None
        )
        .unwrap()["status"],
        "Completed"
    );
    // Revoking business view hides existing notices and rejects a known old ID.
    let template = create(
        CREATE_PERMISSION_TEMPLATE,
        json!({"code":format!("NOTICE-{suffix}"),"name":"仅公告通知","isActive":true,"grants":[{"resourceKey":"office.announcements","action":"view","dataScope":"company"},{"resourceKey":"office.notifications","action":"view","dataScope":"company"}]}),
    );
    let accounts = call(service, admin, LIST_USERS, 0, &[], None).unwrap();
    let mut account = accounts["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["id"] == users[0]["id"])
        .unwrap()
        .clone();
    account["expectedVersion"] = account["versionNumber"].clone();
    account["permissionTemplateId"] = template["id"].clone();
    call(
        service,
        admin,
        UPDATE_USER_ACCOUNT,
        users[0]["id"].as_i64().unwrap(),
        &[],
        Some(account),
    )
    .unwrap();
    assert_eq!(
        call(service, &reader, LIST_NOTIFICATIONS, 0, &[], None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let revoked = login(users[0]["username"].as_str().unwrap());
    assert_eq!(
        call(service, &revoked, LIST_NOTIFICATIONS, 0, &[], None).unwrap()["totalCount"],
        0
    );
    assert_eq!(
        call(
            service,
            &revoked,
            READ_NOTIFICATION,
            notification,
            &[],
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    row = action(service, admin, ARCHIVE_ANNOUNCEMENT, &row);
    assert_eq!(row["status"], "Archived");
    assert_eq!(
        call(
            service,
            admin,
            DELETE_ANNOUNCEMENT,
            id,
            &[],
            Some(json!({"expectedVersion":row["versionNumber"],"note":"不可删除发布历史"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        call(service, &revoked, GET_ANNOUNCEMENT, id, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    id
}
