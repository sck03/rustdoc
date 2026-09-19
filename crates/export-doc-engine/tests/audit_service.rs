#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::generated_api::*;
use serde_json::{Value, json};

#[test]
fn audit_filters_export_and_cleanup_keep_business_data_and_redact_private_values() {
    let fixture = native_fixture::Fixture::new();
    let customer = fixture.create(
        CREATE_CUSTOMER,
        json!({"customerNameEN":"CONFIDENTIAL-CUSTOMER","customerNameCN":"保密客户资料"}),
    );
    let query = &[
        ("entityName", "Customer".into()),
        ("action", "Added".into()),
    ];
    let page: Value = fixture
        .client()
        .json(LIST_AUDIT_LOGS, &[], query, None)
        .unwrap();
    assert_eq!(page["totalCount"], 1);
    export_doc_contracts::validation::response(LIST_AUDIT_LOGS.id, &page).unwrap();
    assert!(!page.to_string().contains("CONFIDENTIAL-CUSTOMER"));
    assert!(!page.to_string().contains("保密客户资料"));
    assert!(
        page["items"][0]["newValues"]
            .as_str()
            .unwrap()
            .contains("[TEXT length=")
    );
    assert_eq!(page["items"][0]["entityId"], customer["id"].to_string());
    let bad: Result<Value, _> = fixture.client().json(
        LIST_AUDIT_LOGS,
        &[],
        &[("startTime", "2026-09-17 09:00".into())],
        None,
    );
    assert_eq!(bad.unwrap_err().status, Some(400));
    for request in [
        json!({"confirmed":true}),
        json!({"entityName":"Customer","confirmed":false}),
    ] {
        assert_eq!(
            fixture
                .client()
                .json::<Value>(DELETE_AUDIT_LOGS_BY_CRITERIA, &[], &[], Some(request))
                .unwrap_err()
                .status,
            Some(400)
        );
    }
    #[cfg(feature = "excel")]
    {
        let bytes = fixture
            .client()
            .bytes(
                DOWNLOAD_AUDIT_LOGS,
                &[],
                &[],
                Some(json!({"entityName":"Customer"})),
                16 * 1024 * 1024,
            )
            .unwrap();
        assert!(bytes.starts_with(b"PK"));
        let output = fixture.root.join("审计.xlsx");
        let response = fixture.request(
            SAVE_AUDIT_LOGS_TO_PATH,
            None,
            Some(json!({"entityName":"Customer","destinationPath":output})),
        );
        assert_eq!(response["affectedCount"], 1);
        assert!(std::fs::read(output).unwrap().starts_with(b"PK"));
    }
    let removed = fixture.request(
        DELETE_AUDIT_LOGS_BY_CRITERIA,
        None,
        Some(json!({"entityName":"Customer","confirmed":true})),
    );
    assert_eq!(removed["affectedCount"], 1);
    let page: Value = fixture
        .client()
        .json(LIST_AUDIT_LOGS, &[], query, None)
        .unwrap();
    assert_eq!(page["totalCount"], 0);
    let existing = fixture.request(GET_CUSTOMER, customer["id"].as_i64(), None);
    assert_eq!(existing["customerNameEN"], "CONFIDENTIAL-CUSTOMER");
    let cleanup = fixture.request(
        CLEANUP_AUDIT_LOGS,
        None,
        Some(json!({"daysToKeep":180,"confirmed":true})),
    );
    assert_eq!(cleanup["affectedCount"], 0);
}
