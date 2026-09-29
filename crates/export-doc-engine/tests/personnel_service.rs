#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
#[path = "support/personnel_contract.rs"]
mod personnel_contract;
use export_doc_engine::{engine::NativeService, generated_api::LOGIN};
use serde_json::{Value, json};

#[test]
fn personnel_registration_files_and_private_fields_follow_the_contract() {
    let mut fixture = native_fixture::Fixture::new();
    fixture.client.take();
    let service = NativeService::open(fixture.paths.clone()).unwrap();
    let login: Value = serde_json::from_slice(
        &service
            .dispatch(
                LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    personnel_contract::exercise(&service, login["accessToken"].as_str().unwrap());
    service.close().unwrap();
}
