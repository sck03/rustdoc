//! Resource files, assigned inboxes and archived PDFs share the two-store contract.
use super::{NativeService, Value, call, json, operation, workflow};
use export_doc_engine::{generated_api::*, paths::nonce};
use std::time::{Duration, Instant};

fn list(service: &NativeService, token: &str, op: Operation, query: &[(&str, String)]) -> Value {
    serde_json::from_slice(&service.dispatch(op, &[], query, None, token).unwrap()).unwrap()
}
fn snapshot(service: &NativeService, token: &str, kind: &str, id: i64, submission: i64) -> Value {
    serde_json::from_slice(
        &service
            .dispatch(
                operation(kind, "submission"),
                &[
                    ("id", id.to_string()),
                    ("submissionId", submission.to_string()),
                ],
                &[],
                None,
                token,
            )
            .unwrap(),
    )
    .unwrap()
}
pub(super) fn exercise(
    service: &NativeService,
    admin: &str,
    applicant: &str,
    reviewer: &str,
    users: &[Value],
    company: &str,
    person: &Value,
) {
    let mut settings = call(service, admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    for rule in settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .filter(|r| matches!(r["kind"].as_str(), Some("room" | "supply")))
    {
        rule["stages"] = json!([workflow::node(
            "review",
            &[],
            "Named",
            json!([users[2]["id"]])
        )]);
    }
    settings["expectedVersion"] = settings["versionNumber"].clone();
    settings = call(service, admin, SAVE_OA_APPROVAL_SETTINGS, 0, Some(settings)).unwrap();
    for key in ["room", "supply"] {
        settings = call(
            service,
            admin,
            PUBLISH_OA_DEFINITION,
            0,
            Some(json!({"definitionKey":key,"expectedVersion":settings["versionNumber"]})),
        )
        .unwrap();
    }
    let room=call(service,admin,CREATE_MEETING_ROOM,0,Some(json!({"name":"资源档案验收会议室","capacity":10,"maximumBookingHours":8,"advanceBookingDays":90,"requiresKey":true,"isActive":true}))).unwrap();
    let supply=call(service,admin,CREATE_OFFICE_SUPPLY,0,Some(json!({"name":"资源档案验收物品","unit":"件","isReturnable":false,"minimumStock":0,"isActive":true}))).unwrap();
    call(service,admin,RESTOCK_OFFICE_SUPPLY,supply["id"].as_i64().unwrap(),Some(json!({"operationId":nonce().unwrap(),"expectedVersion":supply["versionNumber"],"quantity":10,"note":"准备受控测试库存"}))).unwrap();
    let start = chrono::Utc::now() + chrono::Duration::minutes(30);
    let room_body = json!({"requestKey":nonce().unwrap(),"meetingRoomId":room["id"],"title":"冻结会议资料","attendeeCount":2,"startsAt":start.to_rfc3339(),"endsAt":(start+chrono::Duration::hours(1)).to_rfc3339(),"submitImmediately":false});
    let supply_body = json!({"requestKey":nonce().unwrap(),"officeSupplyId":supply["id"],"quantity":2,"purpose":"冻结领用说明","returnDueDate":null,"submitImmediately":false});
    let mut rows = vec![
        call(
            service,
            applicant,
            CREATE_MEETING_BOOKING,
            0,
            Some(room_body.clone()),
        )
        .unwrap(),
        call(
            service,
            applicant,
            CREATE_OFFICE_SUPPLY_REQUEST,
            0,
            Some(supply_body),
        )
        .unwrap(),
    ];
    assert!(rows.iter().all(|r| r["status"] == "Draft"));
    let queue = [
        ("approvalsOnly", "true".into()),
        ("mineOnly", "false".into()),
        ("status", "Pending".into()),
    ];
    assert_eq!(
        list(service, reviewer, LIST_MEETING_BOOKINGS, &queue)["totalCount"],
        0
    );
    let mut immediate = room_body;
    immediate["requestKey"] = json!(nonce().unwrap());
    immediate
        .as_object_mut()
        .unwrap()
        .remove("submitImmediately");
    let immediate = call(
        service,
        applicant,
        CREATE_MEETING_BOOKING,
        0,
        Some(immediate),
    )
    .unwrap();
    assert_eq!(
        immediate["status"], "Pending",
        "existing create semantics stay explicit and submitted"
    );
    assert_eq!(
        call(
            service,
            applicant,
            SUBMIT_MEETING_BOOKING,
            rows[0]["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":rows[0]["versionNumber"],"note":"时段复核"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    call(
        service,
        applicant,
        CANCEL_MEETING_BOOKING,
        immediate["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":immediate["versionNumber"],"note":"释放测试预约"})),
    )
    .unwrap();
    let original =
        b"<?xml version=\"1.0\"?><Resource><Purpose>anonymous review</Purpose></Resource>";
    for (index, kind, submit, approve, cancel, get) in [
        (
            0,
            "room",
            SUBMIT_MEETING_BOOKING,
            APPROVE_MEETING_BOOKING,
            CANCEL_MEETING_BOOKING,
            GET_MEETING_BOOKING,
        ),
        (
            1,
            "supply",
            SUBMIT_OFFICE_SUPPLY_REQUEST,
            APPROVE_OFFICE_SUPPLY_REQUEST,
            CANCEL_OFFICE_SUPPLY_REQUEST,
            GET_OFFICE_SUPPLY_REQUEST,
        ),
    ] {
        let id = rows[index]["id"].as_i64().unwrap();
        let parameters = [("id", id.to_string())];
        rows[index] = service
            .upload(
                operation(kind, "upload"),
                &parameters,
                json!({"expectedVersion":rows[index]["versionNumber"],"purpose":"Supporting"}),
                "anonymous-resource.xml",
                original,
                applicant,
            )
            .unwrap();
        let attachment = rows[index]["attachments"][0]["id"].as_i64().unwrap();
        let path = [
            ("id", id.to_string()),
            ("attachmentId", attachment.to_string()),
        ];
        assert_eq!(
            service
                .download_file(operation(kind, "download"), &path, applicant)
                .unwrap()
                .content,
            original
        );
        assert_eq!(
            call(service, reviewer, get, id, None).unwrap_err().status,
            Some(403),
            "an unsubmitted resource draft stays private"
        );
        rows[index] = call(
            service,
            applicant,
            submit,
            id,
            Some(json!({"expectedVersion":rows[index]["versionNumber"],"note":"冻结附件并提交"})),
        )
        .unwrap();
        assert_eq!(rows[index]["status"], "Pending");
        if index == 0 {
            let current = super::member(
                service,
                admin,
                company,
                person["employee"]["id"].as_i64().unwrap(),
            );
            let mut changed = current["appointments"].clone();
            changed[0]["jobTitle"] = json!("未交接不能调岗");
            assert_eq!(
                super::save_member(service, admin, company, &current, changed)
                    .unwrap_err()
                    .status,
                Some(409),
                "public appointment maintenance shares personnel clearance requirements"
            );
        }
        let inbox = list(
            service,
            reviewer,
            if index == 0 {
                LIST_MEETING_BOOKINGS
            } else {
                LIST_OFFICE_SUPPLY_REQUESTS
            },
            &queue,
        );
        assert_eq!(inbox["totalCount"], 1);
        assert_eq!(inbox["items"][0]["id"], id);
        let hub = call(service, reviewer, GET_OA_APPROVAL_HUB, 0, None).unwrap();
        let entry = hub["entries"]
            .as_array()
            .unwrap()
            .iter()
            .find(|e| {
                e["kind"] == if index == 0 { "rooms" } else { "supplies" }
                    && e["status"] == "Pending"
            })
            .unwrap();
        assert_eq!(entry["page"]["totalCount"], 1);
        assert_eq!(entry["page"]["items"][0]["id"], id);
        assert!(entry["page"]["items"][0].get("attachments").is_none());
        assert_eq!(
            call(service, reviewer, get, id, None).unwrap()["companyScope"],
            company
        );
        assert_eq!(
            service
                .upload(
                    operation(kind, "upload"),
                    &parameters,
                    json!({"expectedVersion":rows[index]["versionNumber"],"purpose":"Supporting"}),
                    "late.xml",
                    original,
                    applicant
                )
                .unwrap_err()
                .status,
            Some(409)
        );
        let submission = rows[index]["submissionId"].as_i64().unwrap();
        let submitted = snapshot(service, applicant, kind, id, submission);
        assert_eq!(submitted["request"]["attachments"][0]["id"], attachment);
        assert_eq!(submitted["request"]["kind"], kind);
        rows[index] = call(
            service,
            reviewer,
            approve,
            id,
            Some(json!({"expectedVersion":rows[index]["versionNumber"],"note":"资源附件核对通过"})),
        )
        .unwrap();
        let deadline = Instant::now() + Duration::from_secs(60);
        loop {
            let state = snapshot(service, applicant, kind, id, submission);
            if state["documentStatus"] == "Ready" {
                break;
            }
            assert_ne!(
                state["documentStatus"], "Failed",
                "{}",
                state["documentError"]
            );
            assert!(Instant::now() < deadline);
            std::thread::sleep(Duration::from_millis(50));
        }
        let doc = [
            ("id", id.to_string()),
            ("submissionId", submission.to_string()),
        ];
        let pdf = service
            .download_file(operation(kind, "document-download"), &doc, applicant)
            .unwrap()
            .content;
        assert!(pdf.starts_with(b"%PDF-"));
        assert_eq!(
            pdf,
            service
                .download_file(operation(kind, "document-download"), &doc, applicant)
                .unwrap()
                .content
        );
        let bundle = service
            .download_file(operation(kind, "bundle-download"), &doc, applicant)
            .unwrap()
            .content;
        let mut zip = zip::ZipArchive::new(std::io::Cursor::new(bundle)).unwrap();
        let mut retained = false;
        for i in 0..zip.len() {
            use std::io::Read;
            let mut data = vec![];
            zip.by_index(i).unwrap().read_to_end(&mut data).unwrap();
            retained |= data == original;
        }
        assert!(retained, "the archive retains the submitted original bytes");
        rows[index]=call(service,applicant,cancel,id,Some(json!({"expectedVersion":rows[index]["versionNumber"],"note":"取消已批准的测试资源"}))).unwrap();
        assert_eq!(
            snapshot(service, applicant, kind, id, submission)["documentStatus"],
            "Voided"
        );
        assert!(
            service
                .download_file(operation(kind, "document-download"), &doc, applicant)
                .is_err()
        );
        assert_eq!(
            service
                .download_file(operation(kind, "download"), &path, applicant)
                .unwrap()
                .content,
            original
        );
    }
    // A business context resolves the actual secondary appointment rather than
    // rejecting the employee because the main appointment belongs elsewhere.
    let sub = format!("RESOURCE-{}", &nonce().unwrap()[..8]);
    call(
        service,
        admin,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":sub,"name":"资源兼任单位","isActive":true})),
    )
    .unwrap();
    call(
        service,
        admin,
        CREATE_ORGANIZATION_DEPARTMENT,
        0,
        Some(json!({"code":"RESOURCE","name":"资源使用部","companyCode":sub,"isActive":true})),
    )
    .unwrap();
    let live = call(
        service,
        admin,
        GET_PERSONNEL,
        person["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    let mut organization = live["organization"].clone();
    organization["additionalAssignments"].as_array_mut().unwrap().push(json!({"companyCode":sub,"departmentId":"RESOURCE","jobTitle":"跨单位兼任","startsOn":"","endsOn":"","managerEmployeeId":null,"functionalManagerEmployeeIds":[]}));
    let day = call(service, admin, GET_CURRENT_USER, 0, None).unwrap()["businessDate"].clone();
    let live=workflow::personnel_change(service, admin, "Transfer",live["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":live["versionNumber"],"effectiveDate":day,"departmentId":live["employee"]["departmentId"],"jobTitle":live["employee"]["jobTitle"],"organization":organization,"note":"建立资源单位兼任"}))).unwrap();
    let appointment = live["organization"]["additionalAssignments"]
        .as_array()
        .unwrap()
        .iter()
        .find(|a| a["companyCode"] == sub)
        .unwrap()["id"]
        .clone();
    let grants = json!(
        ["view", "create", "cancel"]
            .map(|action| json!({"resourceKey":"office.rooms","action":action,"dataScope":"own"}))
    );
    let role=call(service,admin,SAVE_UNIT_ACCESS,0,Some(json!({"userId":users[3]["id"],"companyCode":sub,"name":"资源业务兼任","mode":"Business","appointmentId":appointment,"permissionGrants":grants,"disabledModules":[],"oaAssignedOnly":false,"management":{"organization":false,"appointments":false,"workflows":false,"roles":false},"includeDescendants":false,"startsAt":"","endsAt":"","isActive":true,"expectedVersion":0}))).unwrap();
    let username = format!("resource-sub-{}", nonce().unwrap());
    call(service,admin,CREATE_USER_ACCOUNT,0,Some(json!({"username":username,"fullName":"资源单位测试管理员","role":"Admin","companyScope":sub,"departmentId":"RESOURCE","isActive":true,"resetPassword":"Resource-Contract-2026"}))).unwrap();
    let sub_admin = call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":username,"password":"Resource-Contract-2026"})),
    )
    .unwrap();
    let room=call(service,sub_admin["accessToken"].as_str().unwrap(),CREATE_MEETING_ROOM,0,Some(json!({"name":"兼任申请会议室","capacity":5,"maximumBookingHours":8,"advanceBookingDays":90,"isActive":true,"requiresKey":false}))).unwrap();
    let fresh = call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":users[3]["username"],"password":"Appointment-Contract-2026"})),
    )
    .unwrap();
    let selected = call(
        service,
        fresh["accessToken"].as_str().unwrap(),
        SWITCH_UNIT_CONTEXT,
        0,
        Some(json!({"contextId":format!("unit:{}:{sub}",role["id"])})),
    )
    .unwrap();
    let token = selected["accessToken"].as_str().unwrap();
    let draft=call(service,token,CREATE_MEETING_BOOKING,0,Some(json!({"requestKey":nonce().unwrap(),"meetingRoomId":room["id"],"title":"按兼任身份申请","attendeeCount":1,"startsAt":start.to_rfc3339(),"endsAt":(start+chrono::Duration::hours(1)).to_rfc3339(),"submitImmediately":false}))).unwrap();
    assert_eq!(draft["companyScope"], sub);
    assert_eq!(draft["departmentId"], "RESOURCE");
    call(
        service,
        token,
        CANCEL_MEETING_BOOKING,
        draft["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":draft["versionNumber"],"note":"关闭兼任测试草稿"})),
    )
    .unwrap();
}
