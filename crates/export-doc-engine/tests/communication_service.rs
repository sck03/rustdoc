#[path = "support/communication_contract.rs"]
mod communication_contract;
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{engine::NativeService, generated_api::*};
use serde_json::{Value, json};

#[test]
fn announcements_receipts_and_transactional_notifications_survive_restart() {
    let mut fixture = native_fixture::Fixture::new();
    fixture.client.take();
    let service = NativeService::open(fixture.paths.clone()).unwrap();
    let login = |s: &NativeService| -> String {
        let v: Value = serde_json::from_slice(
            &s.dispatch(
                LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
        )
        .unwrap();
        v["accessToken"].as_str().unwrap().into()
    };
    let id = communication_contract::exercise(&service, &login(&service));
    service.close().unwrap();
    drop(service);
    let reopened = NativeService::open(fixture.paths.clone()).unwrap();
    let token = login(&reopened);
    let row: Value = serde_json::from_slice(
        &reopened
            .dispatch(
                GET_ANNOUNCEMENT,
                &[("id", id.to_string())],
                &[],
                None,
                &token,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(row["status"], "Archived");
    assert_eq!(row["publishVersion"], 2);
    let receipts: Value = serde_json::from_slice(
        &reopened
            .dispatch(
                LIST_ANNOUNCEMENT_RECEIPTS,
                &[("id", id.to_string())],
                &[],
                None,
                &token,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(receipts["totalCount"], 2);
    reopened.close().unwrap();
}
#[test]
fn desktop_editions_do_not_expose_communications_even_to_admin() {
    for edition in ["Document", "Sales"] {
        let fixture = native_fixture::Fixture::edition(edition);
        for op in [
            LIST_ANNOUNCEMENTS,
            MANAGE_ANNOUNCEMENTS,
            LIST_NOTIFICATIONS,
            GET_NOTIFICATION_UNREAD_COUNT,
        ] {
            assert_eq!(
                fixture
                    .client()
                    .json::<Value>(op, &[], &[], None)
                    .unwrap_err()
                    .status,
                Some(403)
            );
        }
    }
}
