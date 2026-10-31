#![cfg(feature = "postgres")]
#[path = "support/account_permissions.rs"]
mod account_permissions;
#[path = "support/account_scope.rs"]
mod account_scope;
#[path = "support/appointment_contract.rs"]
mod appointment_contract;
#[path = "support/approval_contract.rs"]
mod approval_contract;
#[path = "support/business_contract.rs"]
mod business_contract;
#[path = "support/communication_contract.rs"]
mod communication_contract;
#[path = "support/handling_contract.rs"]
mod handling_contract;
#[path = "support/identity_contract.rs"]
mod identity_contract;
#[path = "support/invoice_clone_contract.rs"]
mod invoice_clone_contract;
#[path = "support/material_contract.rs"]
mod material_contract;
#[path = "support/oa_archive_contract.rs"]
mod oa_archive_contract;
#[path = "support/oa_contract.rs"]
mod oa_contract;
#[path = "support/oa_integration_contract.rs"]
mod oa_integration_contract;
#[path = "support/office_contract.rs"]
mod office_contract;
#[path = "support/organization_workflow_contract.rs"]
mod organization_workflow_contract;
#[path = "support/permission_identity_contract.rs"]
mod permission_identity_contract;
#[path = "support/personnel_contract.rs"]
mod personnel_contract;
#[path = "support/personnel_policy_contract.rs"]
mod personnel_policy_contract;
#[path = "support/personnel_scope_contract.rs"]
mod personnel_scope_contract;
#[path = "support/template_contract.rs"]
mod template_contract;
#[path = "support/tools_contract.rs"]
mod tools_contract;
#[path = "support/unit_contract.rs"]
mod unit_contract;
#[path = "support/approval_fixture.rs"]
mod workflow;
#[path = "support/workflow_operations_contract.rs"]
mod workflow_operations_contract;
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
    let body = workflow::decision_body(service, token, operation, id, body);
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
    let mut paths = RuntimePaths::server(&root, &root.join("Data")).unwrap();
    paths.font_path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../Resources/Fonts/OpenSource/NotoSansCJKsc-Regular.otf")
        .canonicalize()
        .unwrap();
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
    assert!(
        admin["user"]["capabilities"]
            .get("usesOfficeRegister")
            .is_none()
    );
    let token = admin["accessToken"].as_str().unwrap();
    material_contract::exercise(&service, token);
    workflow_operations_contract::exercise(&service, token);
    permission_identity_contract::exercise(&service, token);
    invoice_clone_contract::exercise(&|operation, parameters, query, body| {
        let bytes = service.dispatch(operation, parameters, query, body, token)?;
        Ok(serde_json::from_slice(&bytes).unwrap())
    });
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
    let body = json!({"requestKey":nonce().unwrap(),"employeeId":employee["employee"]["id"],"meetingRoomId":room_id,"title":"原生团队验收","attendeeCount":3,"startsAt":start.to_rfc3339(),"endsAt":end.to_rfc3339()});
    assert_eq!(
        call(
            &service,
            staff_token,
            CREATE_MEETING_BOOKING,
            0,
            Some(body.clone())
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let (reviewer_id, reviewer_token) = workflow::reviewer(&service, token);
    workflow::configure(&service, token, reviewer_id);
    let mut delegated = body.clone();
    delegated["employeeId"] = json!(i64::MAX);
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
        Some(403)
    );
    let booking = call(&service, staff_token, CREATE_MEETING_BOOKING, 0, Some(body)).unwrap();
    assert_eq!(booking["status"], "Pending");
    let booking_id = booking["id"].as_i64().unwrap();
    let decision = json!({"expectedVersion":booking["versionNumber"],"taskKey":booking["approvalPlan"]["steps"][0]["taskKey"],"note":"同意"});
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
        &reviewer_token,
        APPROVE_MEETING_BOOKING,
        booking_id,
        Some(decision),
    )
    .unwrap();
    assert_eq!(approved["status"], "Approved");
    assert_eq!(call(&service, staff_token, UPDATE_MEETING_BOOKING, booking_id, Some(json!({"expectedVersion":approved["versionNumber"],"meetingRoomId":room_id,"title":"修改后重新审批","attendeeCount":4,"startsAt":start.to_rfc3339(),"endsAt":end.to_rfc3339()}))).unwrap_err().status, Some(409));
    let cancelled = call(
        &service,
        staff_token,
        CANCEL_MEETING_BOOKING,
        booking_id,
        Some(json!({"expectedVersion":approved["versionNumber"],"note":"修改内容，重新申请"})),
    )
    .unwrap();
    assert_eq!(cancelled["status"], "Cancelled");
    let resubmitted = call(&service, staff_token, CREATE_MEETING_BOOKING, 0, Some(json!({"requestKey":nonce().unwrap(),"meetingRoomId":room_id,"title":"修改后重新审批","attendeeCount":4,"startsAt":start.to_rfc3339(),"endsAt":end.to_rfc3339()}))).unwrap();
    assert_eq!(resubmitted["status"], "Pending");
    assert_ne!(resubmitted["id"], booking["id"]);

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
        let body = workflow::decision_body(&service, token, operation, id.unwrap_or(0), body);
        service
            .dispatch(operation, &parameters, query, body, token)
            .map(|bytes| serde_json::from_slice(&bytes).unwrap())
    };
    office_contract::exercise(
        &|op, id, query, body| request(admin_token, op, id, query, body),
        &|op, id, query, body| request(staff_token, op, id, query, body),
        &|op, id, query, body| request(&reviewer_token, op, id, query, body),
    );
    oa_contract::exercise(&service, admin_token, staff_token, None);
    oa_archive_contract::exercise(&service, admin_token, staff_token, None);
    approval_contract::exercise(&service, admin_token);
    organization_workflow_contract::exercise(&service, admin_token);
    oa_integration_contract::exercise(&service, admin_token);
    organization_workflow_contract::exercise_nodes(&service, admin_token);
    personnel_scope_contract::exercise(&service, admin_token);
    handling_contract::exercise(&service, admin_token);
    communication_contract::exercise(&service, admin_token);
    account_scope::exercise(&service, admin_token);
    template_contract::exercise(&service, admin_token);
    account_permissions::exercise(&service, admin_token);
    personnel_contract::exercise(&service, admin_token);
    identity_contract::exercise(&service, admin_token);
    personnel_policy_contract::exercise(&service, admin_token);
    appointment_contract::exercise(&service, admin_token);
    unit_contract::exercise(&service, admin_token);
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
