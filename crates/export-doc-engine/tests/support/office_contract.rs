use export_doc_engine::{api::ApiError, contracts, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub type Request<'a> = dyn Fn(Operation, Option<i64>, &[(&str, String)], Option<Value>) -> Result<Value, ApiError>
    + 'a;

fn save(request: &Request<'_>, operation: Operation, id: Option<i64>, value: Value) -> Value {
    let response = request(
        operation,
        id,
        &[],
        Some(contracts::overlay(
            contracts::object(operation.id, true),
            &value,
        )),
    )
    .unwrap_or_else(|cause| panic!("{}: {cause}", operation.id));
    export_doc_contracts::validation::response(operation.id, &response).unwrap();
    response
}
fn page(
    request: &Request<'_>,
    operation: Operation,
    id: Option<i64>,
    query: &[(&str, String)],
) -> Value {
    let response = request(operation, id, query, None).unwrap();
    export_doc_contracts::validation::response(operation.id, &response).unwrap();
    response
}

/// The same query, concurrency, history and stock behavior is exercised on both providers.
pub fn exercise(admin: &Request<'_>, applicant: &Request<'_>, employee_id: Option<i64>) {
    let name = format!("行政契约-{}", nonce().unwrap());
    let room = save(
        admin,
        CREATE_MEETING_ROOM,
        None,
        json!({"name":name,"location":"三层东侧","capacity":12,"maximumBookingHours":8,"advanceBookingDays":90,"requiresKey":true,"isActive":true}),
    );
    let room_id = room["id"].as_i64().unwrap();
    let start = chrono::Utc::now() + chrono::Duration::minutes(20);
    let end = start + chrono::Duration::hours(1);
    let request = json!({"requestKey":nonce().unwrap(),"meetingRoomId":room_id,"title":"业务交接会议","attendeeCount":4,"startsAt":start.to_rfc3339(),"endsAt":end.to_rfc3339(),"employeeId":employee_id});
    let mut booking = save(applicant, CREATE_MEETING_BOOKING, None, request.clone());
    let id = booking["id"].as_i64();
    assert_eq!(
        save(applicant, CREATE_MEETING_BOOKING, None, request.clone())["id"],
        booking["id"]
    );
    let mut changed = request.clone();
    changed["title"] = json!("不同内容");
    assert_eq!(
        applicant(CREATE_MEETING_BOOKING, None, &[], Some(changed))
            .unwrap_err()
            .status,
        Some(409)
    );
    if employee_id.is_none() {
        assert_eq!(booking["status"], "Pending");
        assert_eq!(
            applicant(
                APPROVE_MEETING_BOOKING,
                id,
                &[],
                Some(json!({"expectedVersion":booking["versionNumber"]}))
            )
            .unwrap_err()
            .status,
            Some(403)
        );
        booking = save(
            admin,
            APPROVE_MEETING_BOOKING,
            id,
            json!({"expectedVersion":booking["versionNumber"],"note":"同意预约"}),
        );
    }
    let query = [
        ("resourceId", room_id.to_string()),
        ("requestId", id.unwrap().to_string()),
    ];
    assert_eq!(
        page(admin, LIST_MEETING_BOOKINGS, None, &query)["totalCount"],
        1
    );
    let no_match = [("requestId", i64::MAX.to_string())];
    assert_eq!(
        page(admin, LIST_MEETING_BOOKINGS, None, &no_match)["totalCount"],
        0
    );
    let mut query_empty = query.to_vec();
    query_empty.push(("from", (end + chrono::Duration::hours(1)).to_rfc3339()));
    assert_eq!(
        page(admin, LIST_MEETING_BOOKINGS, None, &query_empty)["totalCount"],
        0
    );
    let issued = save(
        admin,
        ISSUE_MEETING_ROOM_KEY,
        id,
        json!({"expectedVersion":booking["versionNumber"],"note":"当面交付钥匙"}),
    );
    let rooms = page(
        admin,
        LIST_MEETING_ROOMS,
        None,
        &[("keyword", name.clone())],
    );
    assert_eq!(rooms["items"][0]["inUse"], true);
    assert_eq!(
        admin(
            CANCEL_MEETING_BOOKING,
            id,
            &[],
            Some(json!({"expectedVersion":issued["versionNumber"],"note":"使用中不可取消"}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let completed = save(
        admin,
        RETURN_MEETING_ROOM_KEY,
        id,
        json!({"expectedVersion":issued["versionNumber"],"note":"钥匙已收回"}),
    );
    assert_eq!(completed["status"], "Completed");
    let history = page(
        admin,
        GET_MEETING_BOOKING_HISTORY,
        id,
        &[("pageSize", "2".into())],
    );
    assert_eq!(history["items"].as_array().unwrap().len(), 2);
    assert_eq!(history["items"][0]["action"], "Return");
    assert_eq!(history["items"][0]["note"], "钥匙已收回");
    assert_eq!(history["hasNextPage"], true);
    let far_start = start + chrono::Duration::days(2);
    let future = save(
        applicant,
        CREATE_MEETING_BOOKING,
        None,
        json!({"requestKey":nonce().unwrap(),"meetingRoomId":room_id,"title":"未来会议","attendeeCount":2,"startsAt":far_start.to_rfc3339(),"endsAt":(far_start+chrono::Duration::hours(1)).to_rfc3339(),"employeeId":employee_id}),
    );
    let future = if employee_id.is_none() {
        save(
            admin,
            APPROVE_MEETING_BOOKING,
            future["id"].as_i64(),
            json!({"expectedVersion":future["versionNumber"],"note":"同意"}),
        )
    } else {
        future
    };
    assert_eq!(
        admin(
            ISSUE_MEETING_ROOM_KEY,
            future["id"].as_i64(),
            &[],
            Some(json!({"expectedVersion":future["versionNumber"]}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        admin(
            CANCEL_MEETING_BOOKING,
            future["id"].as_i64(),
            &[],
            Some(json!({"expectedVersion":future["versionNumber"],"note":""}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    save(
        admin,
        CANCEL_MEETING_BOOKING,
        future["id"].as_i64(),
        json!({"expectedVersion":future["versionNumber"],"note":"会议计划调整"}),
    );

    let supply = save(
        admin,
        CREATE_OFFICE_SUPPLY,
        None,
        json!({"name":name,"unit":"台","location":"行政柜","description":"可借用设备","isReturnable":true,"isActive":true,"minimumStock":2}),
    );
    let supply_id = supply["id"].as_i64();
    let stock_request = json!({"operationId":nonce().unwrap(),"quantity":10,"expectedVersion":supply["versionNumber"],"note":"采购入库"});
    let movement = save(
        admin,
        RESTOCK_OFFICE_SUPPLY,
        supply_id,
        stock_request.clone(),
    );
    assert_eq!(
        save(
            admin,
            RESTOCK_OFFICE_SUPPLY,
            supply_id,
            stock_request.clone()
        )["id"],
        movement["id"]
    );
    let mut changed = stock_request;
    changed["quantity"] = json!(11);
    assert_eq!(
        admin(RESTOCK_OFFICE_SUPPLY, supply_id, &[], Some(changed))
            .unwrap_err()
            .status,
        Some(409)
    );
    let today = export_doc_engine::clock::BusinessClock::default()
        .now()
        .unwrap()
        .today;
    let request = json!({"requestKey":nonce().unwrap(),"officeSupplyId":supply_id,"employeeId":employee_id,"quantity":3,"purpose":"会议演示","returnDueDate":today.to_string()});
    for invalid_due in [
        Value::Null,
        json!((today + chrono::Duration::days(366)).to_string()),
    ] {
        let mut invalid_request = request.clone();
        invalid_request["returnDueDate"] = invalid_due;
        assert_eq!(
            applicant(
                CREATE_OFFICE_SUPPLY_REQUEST,
                None,
                &[],
                Some(invalid_request)
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    let mut application = save(
        applicant,
        CREATE_OFFICE_SUPPLY_REQUEST,
        None,
        request.clone(),
    );
    assert_eq!(
        save(applicant, CREATE_OFFICE_SUPPLY_REQUEST, None, request)["id"],
        application["id"]
    );
    if employee_id.is_none() {
        application = save(
            admin,
            APPROVE_OFFICE_SUPPLY_REQUEST,
            application["id"].as_i64(),
            json!({"expectedVersion":application["versionNumber"],"note":"同意借用"}),
        );
    }
    let supply = page(
        admin,
        LIST_OFFICE_SUPPLIES,
        None,
        &[("keyword", name.clone())],
    )["items"][0]
        .clone();
    assert_eq!(supply["availableQuantity"], 7);
    assert_eq!(admin(STOCKTAKE_OFFICE_SUPPLY,supply_id,&[],Some(json!({"operationId":nonce().unwrap(),"quantity":2,"expectedVersion":supply["versionNumber"],"note":"不能低于预留"}))).unwrap_err().status,Some(409));
    let mut issued = save(
        admin,
        ISSUE_OFFICE_SUPPLY,
        application["id"].as_i64(),
        json!({"expectedVersion":application["versionNumber"],"note":"当面发放"}),
    );
    issued = save(
        admin,
        RETURN_OFFICE_SUPPLY,
        issued["id"].as_i64(),
        json!({"expectedVersion":issued["versionNumber"],"quantity":1,"note":"先归还一台"}),
    );
    assert_eq!(issued["status"], "Issued");
    assert_eq!(issued["returnedQuantity"], 1);
    issued = save(
        admin,
        RETURN_OFFICE_SUPPLY,
        issued["id"].as_i64(),
        json!({"expectedVersion":issued["versionNumber"],"quantity":2,"note":"余下设备归还"}),
    );
    assert_eq!(issued["status"], "Returned");
    let history = page(
        admin,
        GET_OFFICE_SUPPLY_REQUEST_HISTORY,
        issued["id"].as_i64(),
        &[],
    );
    assert_eq!(history["items"][0]["quantity"], 2);
    assert_eq!(history["items"][0]["note"], "余下设备归还");
    let movements = page(admin, GET_OFFICE_STOCK_HISTORY, supply_id, &[]);
    assert_eq!(movements["totalCount"], 4);
    assert_eq!(movements["items"][0]["stockAfter"], 10);
    let mut supply = page(
        admin,
        LIST_OFFICE_SUPPLIES,
        None,
        &[("keyword", name.clone())],
    )["items"][0]
        .clone();
    supply["isActive"] = json!(false);
    supply["expectedVersion"] = supply["versionNumber"].clone();
    save(admin, UPDATE_OFFICE_SUPPLY, supply_id, supply);
    assert_eq!(
        page(
            admin,
            LIST_OFFICE_SUPPLIES,
            None,
            &[("keyword", name.clone())]
        )["totalCount"],
        0
    );
    assert_eq!(
        page(
            admin,
            LIST_OFFICE_SUPPLIES,
            None,
            &[
                ("keyword", name.clone()),
                ("includeInactive", "true".into())
            ]
        )["totalCount"],
        1
    );
    assert_eq!(
        page(
            admin,
            LIST_OFFICE_SUPPLIES,
            None,
            &[
                ("keyword", name),
                ("includeInactive", "true".into()),
                ("lowStockOnly", "true".into())
            ]
        )["totalCount"],
        0
    );
}
