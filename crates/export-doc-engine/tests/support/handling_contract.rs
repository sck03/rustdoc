//! Service handover and narrow custodian access run on both supported databases.
use super::oa_contract::{call, operation};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};
#[path = "handling_resources.rs"]
mod resources;

fn list(service: &NativeService, token: &str, op: Operation, query: &[(&str, String)]) -> Value {
    let result =
        serde_json::from_slice(&service.dispatch(op, &[], query, None, token).unwrap()).unwrap();
    export_doc_contracts::validation::response(op.id, &result).unwrap();
    result
}
fn act(service: &NativeService, token: &str, action: &str, row: &Value) -> Value {
    call(
        service,
        token,
        operation("general", action),
        row["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":row["versionNumber"],"note":"实物办理验收"})),
    )
    .unwrap()
}
pub fn exercise(service: &NativeService, root: &str) {
    let suffix = &nonce().unwrap()[..8];
    let company = format!("HAND-{suffix}");
    call(
        service,
        root,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"分工闭环验收","isActive":true})),
    )
    .unwrap();
    let department = format!("DEPT-{suffix}");
    call(
        service,
        root,
        CREATE_ORGANIZATION_DEPARTMENT,
        0,
        Some(json!({"code":department,"name":"综合部","companyCode":company,"isActive":true})),
    )
    .unwrap();
    let mut users = vec![];
    for (name, role) in [
        ("admin", "Admin"),
        ("applicant", "OfficeEmployee"),
        ("seal", "OfficeEmployee"),
        ("supplies", "OfficeEmployee"),
        ("leader", "OfficeManager"),
    ] {
        let mut body = json!({"username":format!("{name}-{suffix}"),"fullName":name,"role":role,"companyScope":company,"departmentId":department,"isActive":true,"resetPassword":"Handling-2026-Test"});
        if ["seal", "supplies"].contains(&name) {
            body["permissionGrants"] = json!([
                {"resourceKey":"office.general","action":"view","dataScope":"own"},
                {"resourceKey":"office.general","action":"complete","dataScope":"own"},
                {"resourceKey":"office.supplies","action":"view","dataScope":"own"},
                {"resourceKey":"office.supplies","action":"issue","dataScope":"own"},
                {"resourceKey":"office.supplies","action":"return","dataScope":"own"},
                {"resourceKey":"office.supplies","action":"restock","dataScope":"own"},
                {"resourceKey":"office.rooms","action":"view","dataScope":"own"},
                {"resourceKey":"office.rooms","action":"issue","dataScope":"own"},
                {"resourceKey":"office.rooms","action":"return","dataScope":"own"},
                {"resourceKey":"office.notifications","action":"view","dataScope":"own"}
            ]);
        }
        users
            .push(call(service, root, CREATE_USER_ACCOUNT, 0, Some(body)).unwrap()["user"].clone());
    }
    let login = |user: &Value| {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":user["username"],"password":"Handling-2026-Test"})),
        )
        .unwrap()["accessToken"]
            .as_str()
            .unwrap()
            .to_owned()
    };
    let admin = login(&users[0]);
    let person = call(service,&admin,CREATE_PERSONNEL,0,Some(json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("EMP-{suffix}"),"departmentId":department,"jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":"applicant"}}))).unwrap();
    let person_id = person["employee"]["id"].as_i64().unwrap();
    call(service,&admin,LINK_PERSONNEL_ACCOUNT,person_id,Some(json!({"expectedVersion":person["versionNumber"],"userId":users[1]["id"],"expectedAccountVersion":users[1]["versionNumber"]}))).unwrap();
    let applicant = login(&users[1]);
    let seal = login(&users[2]);
    let supplies = login(&users[3]);
    let leader = login(&users[4]);
    let local = call(service, &admin, GET_CURRENT_USER, 0, None).unwrap()["capabilities"]["usesOfficeRegister"]
        == true;
    let mut policy = call(service, &admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    policy["handlingServices"] = json!([
        {"key":"seal","name":"公章","category":"Seal","handlerUserIds":[users[2]["id"]],"isActive":true},
        {"key":"certificate","name":"在职证明","category":"Certificate","handlerUserIds":[users[3]["id"]],"isActive":true},
        {"key":"daily","name":"日常用品","category":"Supply","handlerUserIds":[users[2]["id"]],"isActive":true},
        {"key":"garment","name":"服装辅料","category":"Supply","handlerUserIds":[users[3]["id"]],"isActive":true}
    ]);
    let save_policy = |mut body: Value| {
        body["expectedVersion"] = body["versionNumber"].clone();
        call(service, &admin, SAVE_OA_APPROVAL_SETTINGS, 0, Some(body)).unwrap()
    };
    policy = save_policy(policy);
    let body = |category: &str, key: &str| json!({"requestKey":nonce().unwrap(),"employeeId":if local {Some(person_id)} else {None},"title":format!("{category}分工测试"),"reason":"审批后专岗办理","category":category,"handlingKey":key});
    let draft = call(
        service,
        &applicant,
        operation("general", "create"),
        0,
        Some(body("Seal", "seal")),
    )
    .unwrap();
    let id = draft["id"].as_i64().unwrap();
    assert_eq!(
        call(service, &seal, operation("general", "get"), id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let missing = call(
        service,
        &applicant,
        operation("general", "create"),
        0,
        Some(body("Seal", "")),
    )
    .unwrap();
    assert_eq!(
        call(
            service,
            &applicant,
            operation("general", "submit"),
            missing["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":missing["versionNumber"]}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    act(service, &applicant, "cancel", &missing);
    assert_eq!(
        call(
            service,
            &applicant,
            operation("general", "create"),
            0,
            Some(body("Certificate", "seal"))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let pending = act(service, &applicant, "submit", &draft);
    assert_eq!(
        call(service, &seal, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        0
    );
    let approved = act(service, &leader, "approve", &pending);
    assert_eq!(
        call(service, &seal, operation("general", "get"), id, None).unwrap()["canHandle"],
        true
    );
    assert_eq!(
        call(service, &supplies, operation("general", "get"), id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let queue = list(
        service,
        &seal,
        operation("general", "list"),
        &[
            ("mineOnly", "false".into()),
            ("handlingOnly", "true".into()),
            ("pageSize", "1".into()),
        ],
    );
    assert_eq!(queue["totalCount"], 1);
    assert_eq!(queue["items"][0]["id"], id);
    assert_eq!(
        call(service, &seal, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        1
    );
    assert_eq!(
        call(
            service,
            &leader,
            operation("general", "complete"),
            id,
            Some(json!({"expectedVersion":approved["versionNumber"],"note":"越过分工"}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let proof = service.upload(
        operation("general", "upload"),
        &[("id", id.to_string())],
        json!({"expectedVersion":approved["versionNumber"]}),
        "late.pdf",
        b"%PDF-1.7\n%%EOF",
        &applicant,
    );
    assert!(proof.is_err(), "approved attachments remain frozen");
    policy["handlingServices"][0]["handlerUserIds"] = json!([users[3]["id"]]);
    policy = save_policy(policy);
    assert_eq!(
        call(service, &seal, operation("general", "get"), id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        call(service, &seal, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        0,
        "old notifications cannot bypass a handover"
    );
    assert_eq!(
        call(service, &supplies, GET_NOTIFICATION_UNREAD_COUNT, 0, None).unwrap()["unreadCount"],
        1
    );
    assert_eq!(
        act(service, &supplies, "complete", &approved)["status"],
        "Completed"
    );
    let certificate = call(
        service,
        &applicant,
        operation("general", "create"),
        0,
        Some(body("Certificate", "certificate")),
    )
    .unwrap();
    let certificate = act(service, &applicant, "submit", &certificate);
    let certificate = act(service, &leader, "approve", &certificate);
    assert_eq!(
        act(service, &supplies, "complete", &certificate)["status"],
        "Completed"
    );
    for (key, name, handler, other) in [
        ("daily", "抽纸", &seal, &supplies),
        ("garment", "借用样品色卡", &supplies, &seal),
    ] {
        let returnable = key == "garment";
        let supply = call(service,&admin,CREATE_OFFICE_SUPPLY,0,Some(json!({"name":name,"unit":"件","minimumStock":0,"isActive":true,"isReturnable":returnable,"handlingKey":key}))).unwrap();
        let supply_id = supply["id"].as_i64().unwrap();
        let catalog = list(service, &applicant, LIST_OFFICE_SUPPLIES, &[]);
        assert!(
            catalog["items"]
                .as_array()
                .unwrap()
                .iter()
                .any(|r| r["id"] == supply_id),
            "employees can select shared inventory without reading other employees' requests"
        );
        let stock_body = json!({"operationId":nonce().unwrap(),"expectedVersion":supply["versionNumber"],"quantity":10,"note":"验收入库"});
        assert_eq!(
            call(
                service,
                other,
                RESTOCK_OFFICE_SUPPLY,
                supply_id,
                Some(stock_body.clone())
            )
            .unwrap_err()
            .status,
            Some(403)
        );
        call(
            service,
            handler,
            RESTOCK_OFFICE_SUPPLY,
            supply_id,
            Some(stock_body),
        )
        .unwrap();
        let today =
            call(service, &admin, GET_CURRENT_USER, 0, None).unwrap()["businessDate"].clone();
        let mut request = call(service,&applicant,CREATE_OFFICE_SUPPLY_REQUEST,0,Some(json!({"requestKey":nonce().unwrap(),"employeeId":if local {Some(person_id)} else {None},"officeSupplyId":supply_id,"quantity":2,"purpose":"领用与归还","returnDueDate":if returnable {today} else {Value::Null}}))).unwrap();
        let rid = request["id"].as_i64().unwrap();
        if !local {
            request = call(
                service,
                &leader,
                APPROVE_OFFICE_SUPPLY_REQUEST,
                rid,
                Some(json!({"expectedVersion":request["versionNumber"]})),
            )
            .unwrap();
        }
        let issue = json!({"expectedVersion":request["versionNumber"],"note":"当面交付"});
        assert_eq!(
            call(
                service,
                other,
                ISSUE_OFFICE_SUPPLY,
                rid,
                Some(issue.clone())
            )
            .unwrap_err()
            .status,
            Some(403)
        );
        request = call(service, handler, ISSUE_OFFICE_SUPPLY, rid, Some(issue)).unwrap();
        let query = [
            ("handlingOnly", "true".into()),
            ("status", "Issued".into()),
            ("mineOnly", "false".into()),
        ];
        if !returnable {
            assert_eq!(
                list(service, handler, LIST_OFFICE_SUPPLY_REQUESTS, &query)["totalCount"],
                0,
                "consumed supplies are not pending returns"
            );
            assert_eq!(
                call(service, &admin, GET_PERSONNEL_CLEARANCE, person_id, None).unwrap()["supplyCount"],
                0,
                "consumed supplies never block departure"
            );
            assert_eq!(
                call(
                    service,
                    handler,
                    RETURN_OFFICE_SUPPLY,
                    rid,
                    Some(json!({"expectedVersion":request["versionNumber"],"quantity":1}))
                )
                .unwrap_err()
                .status,
                Some(409)
            );
            continue;
        }
        assert_eq!(
            list(service, handler, LIST_OFFICE_SUPPLY_REQUESTS, &query)["totalCount"],
            1
        );
        for count in 1..=2 {
            request = call(service,handler,RETURN_OFFICE_SUPPLY,rid,Some(json!({"expectedVersion":request["versionNumber"],"quantity":1,"note":"实物验收"}))).unwrap();
            assert_eq!(request["returnedQuantity"], count);
        }
        assert_eq!(request["status"], "Returned");
        assert_eq!(
            list(
                service,
                handler,
                LIST_OFFICE_SUPPLY_REQUESTS,
                &[("handlingOnly", "true".into())]
            )["totalCount"],
            0,
            "completed returns must leave the unfiltered handling queue"
        );
        assert_eq!(
            list(service, handler, LIST_OFFICE_SUPPLY_REQUESTS, &query)["totalCount"],
            0
        );
    }
    resources::exercise(
        service,
        [&admin, &applicant, &seal, &supplies, &leader],
        &users,
        person_id,
        &mut policy,
        local,
    );
    // Referenced categories cannot disappear or silently change meaning.
    policy["handlingServices"] = json!([]);
    policy["expectedVersion"] = policy["versionNumber"].clone();
    assert_eq!(
        call(service, &admin, SAVE_OA_APPROVAL_SETTINGS, 0, Some(policy))
            .unwrap_err()
            .status,
        Some(409)
    );
}
