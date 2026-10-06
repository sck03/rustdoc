//! Shared SQLite/PostgreSQL checks for multiple handlers and resource lifecycle.
use super::{call, list, operation};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub(super) fn exercise(
    service: &NativeService,
    tokens: [&str; 5],
    users: &[Value],
    employee: i64,
    policy: &mut Value,
    local: bool,
) {
    let [admin, applicant, first, second, leader] = tokens;
    let save = |policy: &mut Value| {
        policy["expectedVersion"] = policy["versionNumber"].clone();
        *policy = call(
            service,
            admin,
            SAVE_OA_APPROVAL_SETTINGS,
            0,
            Some(policy.clone()),
        )
        .unwrap();
    };
    policy["handlingServices"].as_array_mut().unwrap().push(json!({"key":"meeting","name":"会议接待","category":"Room","handlerUserIds":[users[2]["id"],users[3]["id"]],"isActive":true}));
    save(policy);
    assert_eq!(
        call(service, applicant, LIST_ROOM_HANDLING_SERVICES, 0, None).unwrap()["items"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
    assert!(
        call(service, applicant, LIST_GENERAL_HANDLING_SERVICES, 0, None).unwrap()["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|r| r["category"] != "Room")
    );
    let room = call(service, admin, CREATE_MEETING_ROOM, 0, Some(json!({"name":"双人办理会议室","capacity":10,"maximumBookingHours":8,"advanceBookingDays":90,"requiresKey":true,"isActive":true,"handlingKey":"meeting"}))).unwrap();
    let room_id = room["id"].as_i64().unwrap();
    assert!(
        list(service, applicant, LIST_MEETING_ROOMS, &[])["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|r| r["id"] == room_id)
    );
    let start = chrono::Utc::now() + chrono::Duration::minutes(10);
    let body = json!({"requestKey":nonce().unwrap(),"employeeId":if local {Some(employee)} else {None},"meetingRoomId":room_id,"title":"办理闭环","attendeeCount":2,"startsAt":start.to_rfc3339(),"endsAt":(start+chrono::Duration::hours(1)).to_rfc3339()});
    let mut booking = call(
        service,
        applicant,
        CREATE_MEETING_BOOKING,
        0,
        Some(body.clone()),
    )
    .unwrap();
    let id = booking["id"].as_i64().unwrap();
    let queue = |token: &str, handling: bool| {
        list(
            service,
            token,
            LIST_MEETING_BOOKINGS,
            &[
                ("handlingOnly", handling.to_string()),
                ("mineOnly", "false".into()),
                ("requestId", id.to_string()),
                ("pageSize", "1".into()),
            ],
        )
    };
    let notice = |token| {
        list(
            service,
            token,
            LIST_NOTIFICATIONS,
            &[("pageSize", "100".into())],
        )["items"]
            .as_array()
            .unwrap()
            .iter()
            .filter(|r| r["requestId"] == id && r["requestKind"] == "bookings")
            .cloned()
            .collect::<Vec<_>>()
    };
    if !local {
        assert_eq!(
            queue(first, false)["totalCount"],
            0,
            "custodians cannot read pending meeting content"
        );
        assert!(notice(first).is_empty());
        assert!(notice(leader).iter().any(|n| n["action"] == "submit"));
        booking = call(
            service,
            leader,
            APPROVE_MEETING_BOOKING,
            id,
            Some(json!({"expectedVersion":booking["versionNumber"]})),
        )
        .unwrap();
    }
    for token in [first, second] {
        assert_eq!(queue(token, true)["totalCount"], 1);
        assert_eq!(queue(token, true)["items"][0]["canHandle"], true);
        assert!(notice(token).iter().any(|n| n["action"] == "approve"));
        let work = list(
            service,
            token,
            GET_WORKLIST,
            &[("source", "meeting-collection".into())],
        );
        assert_eq!(work["page"]["totalCount"], 1);
    }
    let update = |active, key| {
        let mut value = room.clone();
        value["expectedVersion"] = room["versionNumber"].clone();
        value["isActive"] = json!(active);
        value["handlingKey"] = json!(key);
        value
    };
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_MEETING_ROOM,
            room_id,
            Some(update(false, "meeting"))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_MEETING_ROOM,
            room_id,
            Some(update(true, ""))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let issue = json!({"expectedVersion":booking["versionNumber"],"note":"第一位办理人交付钥匙"});
    assert_eq!(
        call(
            service,
            leader,
            ISSUE_MEETING_ROOM_KEY,
            id,
            Some(issue.clone())
        )
        .unwrap_err()
        .status,
        Some(403),
        "broad permissions do not replace named handlers"
    );
    booking = call(
        service,
        first,
        ISSUE_MEETING_ROOM_KEY,
        id,
        Some(issue.clone()),
    )
    .unwrap();
    assert_eq!(
        call(service, second, ISSUE_MEETING_ROOM_KEY, id, Some(issue))
            .unwrap_err()
            .status,
        Some(409),
        "two handlers cannot repeat one handover"
    );
    let old_notice = notice(first)[0]["id"].as_i64().unwrap();
    let room_service = policy["handlingServices"]
        .as_array_mut()
        .unwrap()
        .last_mut()
        .unwrap();
    room_service["handlerUserIds"] = json!([users[3]["id"]]);
    room_service["name"] = json!("会议接待新名称");
    room_service["isActive"] = json!(false);
    save(policy);
    assert_eq!(queue(first, false)["totalCount"], 0);
    assert!(notice(first).is_empty());
    assert_eq!(
        call(service, first, READ_NOTIFICATION, old_notice, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        call(service, first, GET_MEETING_BOOKING_HISTORY, id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let current = &queue(second, true)["items"][0];
    assert_eq!(
        current["handlingName"], "会议接待",
        "request snapshot stays unchanged during handover"
    );
    assert_eq!(current["handlerNames"], "supplies");
    assert!(notice(second).iter().any(|n| n["action"] == "reassign"));
    let mut fresh = body.clone();
    fresh["requestKey"] = json!(nonce().unwrap());
    fresh["startsAt"] = json!((start + chrono::Duration::hours(2)).to_rfc3339());
    fresh["endsAt"] = json!((start + chrono::Duration::hours(3)).to_rfc3339());
    assert_eq!(
        call(service, applicant, CREATE_MEETING_BOOKING, 0, Some(fresh))
            .unwrap_err()
            .status,
        Some(400),
        "disabled service blocks new requests"
    );
    let returned =
        json!({"expectedVersion":booking["versionNumber"],"note":"第二位办理人核收钥匙"});
    assert_eq!(
        call(
            service,
            first,
            RETURN_MEETING_ROOM_KEY,
            id,
            Some(returned.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    booking = call(
        service,
        second,
        RETURN_MEETING_ROOM_KEY,
        id,
        Some(returned.clone()),
    )
    .unwrap();
    assert_eq!(booking["status"], "Completed");
    assert_eq!(
        call(service, second, RETURN_MEETING_ROOM_KEY, id, Some(returned))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(queue(second, true)["totalCount"], 0);
    let history = call(service, second, GET_MEETING_BOOKING_HISTORY, id, None).unwrap();
    for (action, actor) in [
        ("Issue", "seal"),
        ("Return", "supplies"),
        ("Reassign", "admin"),
    ] {
        assert!(
            history["items"]
                .as_array()
                .unwrap()
                .iter()
                .any(|e| e["action"] == action && e["actorName"] == actor)
        );
    }
    assert_eq!(
        call(service, admin, GET_PERSONNEL_CLEARANCE, employee, None).unwrap()["meetingCount"],
        0
    );
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_MEETING_ROOM,
            room_id,
            Some(update(false, "meeting"))
        )
        .unwrap()["isActive"],
        false,
        "an inactive service must not block archiving its finished resource"
    );
    // Two custodians share a supply group, but inventory is deducted only once.
    policy["handlingServices"][2]["handlerUserIds"] = json!([users[2]["id"], users[3]["id"]]);
    save(policy);
    let supply = call(service, admin, CREATE_OFFICE_SUPPLY, 0, Some(json!({"name":"共同保管耗材","unit":"件","minimumStock":0,"isActive":true,"isReturnable":false,"handlingKey":"daily"}))).unwrap();
    let supply_id = supply["id"].as_i64().unwrap();
    call(service, first, RESTOCK_OFFICE_SUPPLY, supply_id, Some(json!({"operationId":nonce().unwrap(),"expectedVersion":supply["versionNumber"],"quantity":5,"note":"共同管理入库"}))).unwrap();
    let mut supply_request = call(service, applicant, CREATE_OFFICE_SUPPLY_REQUEST, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeId":if local {Some(employee)} else {None},"officeSupplyId":supply_id,"quantity":2,"purpose":"双人办理库存只扣一次","returnDueDate":null}))).unwrap();
    let request_id = supply_request["id"].as_i64().unwrap();
    if !local {
        supply_request = call(
            service,
            leader,
            APPROVE_OFFICE_SUPPLY_REQUEST,
            request_id,
            Some(json!({"expectedVersion":supply_request["versionNumber"]})),
        )
        .unwrap();
    }
    for token in [first, second] {
        let assigned = list(
            service,
            token,
            LIST_OFFICE_SUPPLY_REQUESTS,
            &[
                ("requestId", request_id.to_string()),
                ("handlingOnly", "true".into()),
            ],
        );
        assert_eq!(assigned["totalCount"], 1);
    }
    let issue =
        json!({"expectedVersion":supply_request["versionNumber"],"note":"第二位办理人发放"});
    call(
        service,
        second,
        ISSUE_OFFICE_SUPPLY,
        request_id,
        Some(issue.clone()),
    )
    .unwrap();
    assert_eq!(
        call(service, first, ISSUE_OFFICE_SUPPLY, request_id, Some(issue))
            .unwrap_err()
            .status,
        Some(409)
    );
    let inventory = list(service, admin, LIST_OFFICE_SUPPLIES, &[]);
    let inventory = inventory["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["id"] == supply_id)
        .unwrap();
    assert_eq!(inventory["stockQuantity"], 3);
    assert_eq!(inventory["reservedQuantity"], 0);
    // General services also use any-one handling; a second completion is rejected.
    policy["handlingServices"][0]["handlerUserIds"] = json!([users[2]["id"], users[3]["id"]]);
    save(policy);
    let draft = call(service, applicant, operation("general", "create"), 0, Some(json!({"requestKey":nonce().unwrap(),"employeeId":if local {Some(employee)} else {None},"title":"多位用印办理","reason":"任意一位完成","category":"Seal","handlingKey":"seal"}))).unwrap();
    let pending = super::act(service, applicant, "submit", &draft);
    let approved = super::act(service, leader, "approve", &pending);
    for token in [first, second] {
        assert_eq!(
            call(
                service,
                token,
                operation("general", "get"),
                approved["id"].as_i64().unwrap(),
                None
            )
            .unwrap()["canHandle"],
            true
        );
    }
    super::act(service, second, "complete", &approved);
    assert_eq!(
        call(
            service,
            first,
            operation("general", "complete"),
            approved["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":approved["versionNumber"],"note":"重复用印"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
}
