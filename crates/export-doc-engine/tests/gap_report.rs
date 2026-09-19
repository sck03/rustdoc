use export_doc_engine::generated_api::*;
#[path = "support/native_fixture.rs"]
mod native_fixture;
use native_fixture::Fixture;

#[test]
fn gap_report() {
    let fixture = Fixture::new();
    let client = fixture.client();
    let mut unsupported = vec![];
    for operation in ALL_OPERATIONS {
        if !client.supports(*operation) {
            unsupported.push(operation.id.to_string());
        }
    }
    println!(
        "UNSUPPORTED({}): {}",
        unsupported.len(),
        unsupported.join(", ")
    );
    assert!(
        !client.supports(GET_DISASTER_RECOVERY_STATUS)
            || client.supports(CREATE_DISASTER_RECOVERY_PACKAGE)
    );
    assert!(
        !client.supports(GET_DISASTER_RECOVERY_STATUS)
            || client.supports(RESTORE_DISASTER_RECOVERY_PACKAGE)
    );
    assert!(client.supports(CLONE_USER_REPORT_TEMPLATE));
}
