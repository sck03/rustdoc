#![cfg(feature = "postgres")]
#[path = "support/account_scope.rs"]
mod account_scope;
#[path = "support/business_contract.rs"]
mod business_contract;
#[path = "support/office_contract.rs"]
mod office_contract;
#[path = "support/tools_contract.rs"]
mod tools_contract;
use export_doc_engine::{
    contracts,
    engine::NativeService,
    generated_api::*,
    invoice::InvoiceDraft,
    paths::{RuntimePaths, nonce},
};
use export_doc_storage::Connection;
use serde_json::{Value, json};
use std::{path::PathBuf, sync::Arc};

fn call(
    service: &NativeService,
    token: &str,
    operation: Operation,
    id: i64,
    body: Option<Value>,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let parameters = if id == 0 {
        vec![]
    } else {
        vec![("id", id.to_string())]
    };
    let bytes = service.dispatch(operation, &parameters, &[], body, token)?;
    Ok(serde_json::from_slice(&bytes).unwrap())
}

#[test]
#[ignore = "requires an isolated PostgreSQL 18 database"]
fn team_bootstrap_permissions_personnel_and_approval_share_the_rust_services() {
    let maintenance = std::env::var("EXPORTDOC_TEST_ENGINE_MAINTENANCE").unwrap();
    let app = std::env::var("EXPORTDOC_TEST_ENGINE_APP").unwrap();
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime")
        .join("team-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let paths = RuntimePaths::server(&root, &root.join("Data")).unwrap();
    let bootstrap = nonce().unwrap();
    let service =
        NativeService::open_postgres(paths.clone(), &app, bootstrap.clone(), Default::default())
            .unwrap();
    let login = json!({"username":"admin","password":"Native-Admin-2026"});
    assert_eq!(
        service
            .dispatch_with_bootstrap(LOGIN, &[], &[], Some(login.clone()), "", "invalid")
            .unwrap_err()
            .status,
        Some(401)
    );
    let admin: Value = serde_json::from_slice(
        &service
            .dispatch_with_bootstrap(LOGIN, &[], &[], Some(login), "", &bootstrap)
            .unwrap(),
    )
    .unwrap();
    assert_eq!(admin["user"]["capabilities"]["usesOfficeRegister"], false);
    let token = admin["accessToken"].as_str().unwrap();
    business_contract::exercise(&|operation, parameters, query, body| {
        let bytes = service.dispatch(operation, parameters, query, body, token)?;
        Ok(serde_json::from_slice(&bytes).unwrap())
    });
    tools_contract::exercise(&|operation, parameters, query, body| {
        let bytes = service.dispatch(operation, parameters, query, body, token)?;
        Ok(serde_json::from_slice(&bytes).unwrap())
    });
    let suffix = nonce().unwrap();
    let create = |operation: Operation, body: Value| {
        call(
            &service,
            token,
            operation,
            0,
            Some(contracts::overlay(
                contracts::object(operation.id, true),
                &body,
            )),
        )
        .unwrap()
    };
    let account = create(CREATE_USER_ACCOUNT, json!({"username":format!("employee-{}",&suffix[..8]),"fullName":"团队员工","role":"OfficeManager","departmentId":"GENERAL","companyScope":"DEFAULT","isActive":true,"resetPassword":"Native-Employee-2026"}))["user"].clone();
    let employee = create(
        CREATE_PERSONNEL,
        json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("E-{}",&suffix[..8]),"departmentId":"GENERAL","jobTitle":"行政","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":"团队员工"}}),
    );
    call(&service, token, LINK_PERSONNEL_ACCOUNT, employee["employee"]["id"].as_i64().unwrap(), Some(json!({"expectedVersion":employee["versionNumber"],"userId":account["id"],"expectedAccountVersion":account["versionNumber"]}))).unwrap();
    let staff = call(
        &service,
        "",
        LOGIN,
        0,
        Some(json!({"username":account["username"],"password":"Native-Employee-2026"})),
    )
    .unwrap();
    let staff_token = staff["accessToken"].as_str().unwrap();
    let room = create(
        CREATE_MEETING_ROOM,
        json!({"name":format!("会议室-{}",&suffix[..8]),"location":"一楼","capacity":12,"maximumBookingHours":8,"advanceBookingDays":90,"requiresKey":true,"isActive":true}),
    );
    let room_id = room["id"].as_i64().unwrap();
    let start = chrono::Utc::now() + chrono::Duration::days(1);
    let end = start + chrono::Duration::hours(1);
    let body = json!({"requestKey":nonce().unwrap(),"meetingRoomId":room_id,"title":"原生团队验收","attendeeCount":3,"startsAt":start.to_rfc3339(),"endsAt":end.to_rfc3339()});
    let mut delegated = body.clone();
    delegated["employeeId"] = employee["employee"]["id"].clone();
    assert_eq!(
        call(
            &service,
            staff_token,
            CREATE_MEETING_BOOKING,
            0,
            Some(delegated)
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let booking = call(&service, staff_token, CREATE_MEETING_BOOKING, 0, Some(body)).unwrap();
    assert_eq!(booking["status"], "Pending");
    let booking_id = booking["id"].as_i64().unwrap();
    let decision = json!({"expectedVersion":booking["versionNumber"],"note":"同意"});
    assert_eq!(
        call(
            &service,
            staff_token,
            APPROVE_MEETING_BOOKING,
            booking_id,
            Some(decision.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let approved = call(
        &service,
        token,
        APPROVE_MEETING_BOOKING,
        booking_id,
        Some(decision),
    )
    .unwrap();
    assert_eq!(approved["status"], "Approved");
    let edited = call(&service, staff_token, UPDATE_MEETING_BOOKING, booking_id, Some(json!({"expectedVersion":approved["versionNumber"],"meetingRoomId":room_id,"title":"修改后重新审批","attendeeCount":4,"startsAt":start.to_rfc3339(),"endsAt":end.to_rfc3339()}))).unwrap();
    assert_eq!(edited["status"], "Pending");

    let invoice = InvoiceDraft::demo("2026-09-16", &format!("PG-{}", &suffix[..8]))
        .build()
        .unwrap();
    let saved = call(&service, token, CREATE_INVOICE, 0, Some(json!(invoice))).unwrap();
    assert_eq!(
        saved["invoice"]["items"][0]["quantity"],
        json!(invoice.items[0].quantity)
    );
    assert_eq!(
        call(
            &service,
            staff_token,
            GET_INVOICE,
            saved["invoice"]["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let renewed = call(&service, token, RENEW_SESSION, 0, None).unwrap();
    assert_ne!(renewed["accessToken"], token);
    assert_eq!(
        call(&service, token, GET_CURRENT_USER, 0, None)
            .unwrap_err()
            .status,
        Some(401)
    );
    #[cfg(feature = "excel")]
    let persisted_job = {
        let template_root = root.join("Resources/ExcelTemplates");
        std::fs::create_dir_all(&template_root).unwrap();
        std::fs::write(
            template_root.join("invoice-import-template.xlsx"),
            include_bytes!("../../../Resources/ExcelTemplates/invoice-import-template.xlsx"),
        )
        .unwrap();
        let token = renewed["accessToken"].as_str().unwrap();
        let job = call(&service, token, START_EXCEL_TEMPLATE_DOWNLOAD_JOB, 0, None).unwrap();
        let id = job["jobId"].as_str().unwrap().to_owned();
        let parameters = [("jobId", id.clone())];
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(30);
        loop {
            let job: Value = serde_json::from_slice(
                &service
                    .dispatch(GET_JOB, &parameters, &[], None, token)
                    .unwrap(),
            )
            .unwrap();
            if job["status"] == "Succeeded" {
                break;
            }
            assert_ne!(job["status"], "Failed", "{job}");
            assert!(std::time::Instant::now() < deadline);
            std::thread::sleep(std::time::Duration::from_millis(20));
        }
        assert_eq!(
            service
                .dispatch(GET_JOB, &parameters, &[], None, staff_token)
                .unwrap_err()
                .status,
            Some(403)
        );
        let bytes = service
            .dispatch(DOWNLOAD_JOB_RESULT, &parameters, &[], None, token)
            .unwrap();
        assert!(bytes.starts_with(b"PK"));
        (id, bytes)
    };
    let admin_token = renewed["accessToken"].as_str().unwrap();
    let request = |token: &str, operation, id: Option<i64>, query: &[(&str, String)], body| {
        let parameters = id
            .map(|id| vec![("id", id.to_string())])
            .unwrap_or_default();
        service
            .dispatch(operation, &parameters, query, body, token)
            .map(|bytes| serde_json::from_slice(&bytes).unwrap())
    };
    office_contract::exercise(
        &|op, id, query, body| request(admin_token, op, id, query, body),
        &|op, id, query, body| request(staff_token, op, id, query, body),
        None,
    );
    account_scope::exercise(&service, admin_token);
    service.close().unwrap();
    drop(Arc::clone(&service));
    drop(service);
    #[cfg(feature = "excel")]
    {
        let reopened =
            NativeService::open_postgres(paths, &app, bootstrap, Default::default()).unwrap();
        let login = call(
            &reopened,
            "",
            LOGIN,
            0,
            Some(json!({"username":"admin","password":"Native-Admin-2026"})),
        )
        .unwrap();
        let token = login["accessToken"].as_str().unwrap();
        let parameters = [("jobId", persisted_job.0)];
        assert_eq!(
            reopened
                .dispatch(DOWNLOAD_JOB_RESULT, &parameters, &[], None, token)
                .unwrap(),
            persisted_job.1
        );
        reopened.close().unwrap();
        drop(reopened);
    }
    std::fs::remove_dir_all(root).unwrap();
}
