#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
#[path = "support/office_contract.rs"]
mod office_contract;

#[test]
fn sqlite_office_directory_schedule_handover_and_stock_follow_the_original_contract() {
    let fixture = native_fixture::Fixture::new();
    let person = fixture.employee();
    use export_doc_engine::generated_api::*;
    use serde_json::json;
    let account=fixture.create(CREATE_USER_ACCOUNT,json!({"username":"resource-applicant","fullName":"资源申请人","role":"OfficeEmployee","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Resource-2026-Test"}));
    fixture.request(LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64(),Some(json!({"expectedVersion":person["versionNumber"],"userId":account["user"]["id"],"expectedAccountVersion":account["user"]["versionNumber"]})));
    let applicant = fixture
        .client()
        .login("resource-applicant".into(), "Resource-2026-Test".into())
        .unwrap()
        .0;
    let applicant_call = |op, id: Option<i64>, query: &[(&str, String)], body| {
        applicant.json(
            op,
            &id.map(|id| vec![("id", id.to_string())])
                .unwrap_or_default(),
            query,
            body,
        )
    };
    let call = |operation, id: Option<i64>, query: &[(&str, String)], body| {
        fixture.client().json(
            operation,
            &id.map(|id| vec![("id", id.to_string())])
                .unwrap_or_default(),
            query,
            body,
        )
    };
    office_contract::exercise(&call, &applicant_call);
}

#[test]
fn administrator_cannot_register_other_company_resources_or_people() {
    use export_doc_engine::{contracts, generated_api::*, paths::nonce};
    use serde_json::{Value, json};
    let fixture = native_fixture::Fixture::new();
    let person = fixture.employee();
    fixture.create(
        CREATE_ORGANIZATION_COMPANY,
        json!({"code":"OTHER","name":"另一公司","isActive":true}),
    );
    fixture.create(
        CREATE_ORGANIZATION_DEPARTMENT,
        json!({"code":"OTHER-HR","name":"行政部","companyCode":"OTHER","isActive":true}),
    );
    fixture.create(CREATE_USER_ACCOUNT,json!({"username":"other-admin","fullName":"另一公司管理员","role":"Admin","departmentId":"OTHER-HR","companyScope":"OTHER","isActive":true,"resetPassword":"Office-Tenant-2026"}));
    let other = fixture
        .client()
        .clone()
        .login("other-admin".into(), "Office-Tenant-2026".into())
        .unwrap()
        .0;
    let room:Value=other.json(CREATE_MEETING_ROOM,&[],&[],Some(contracts::overlay(contracts::object(CREATE_MEETING_ROOM.id,true),&json!({"name":"另一公司会议室","capacity":10,"maximumBookingHours":8,"advanceBookingDays":90,"isActive":true})))).unwrap();
    let start = chrono::Utc::now() + chrono::Duration::hours(1);
    let body = json!({"requestKey":nonce().unwrap(),"meetingRoomId":room["id"],"employeeId":person["employee"]["id"],"title":"跨公司登记应被拒绝","attendeeCount":2,"startsAt":start.to_rfc3339(),"endsAt":(start+chrono::Duration::hours(1)).to_rfc3339()});
    assert_eq!(
        fixture
            .client()
            .json::<Value>(CREATE_MEETING_BOOKING, &[], &[], Some(body.clone()))
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        other
            .json::<Value>(CREATE_MEETING_BOOKING, &[], &[], Some(body))
            .unwrap_err()
            .status,
        Some(403)
    );
    let visible: Value = fixture
        .client()
        .json(LIST_MEETING_ROOMS, &[], &[], None)
        .unwrap();
    assert_eq!(visible["totalCount"], 0);
}
