use super::*;
use serde_json::{Value, json};
#[test]
fn buffered_downloads_reserve_before_reading_and_release_after_delivery() {
    let root = crate::engine::test_support::ScratchDirectory::new();
    let service = root.service();
    let login: Value = serde_json::from_slice(
        &service
            .dispatch(
                crate::generated_api::LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    let token = login["accessToken"].as_str().unwrap();
    let held = service
        .jobs
        .reserve_download(128 * 1024 * 1024, true)
        .unwrap();
    assert_eq!(
        service
            .prepare_file_download(DOWNLOAD_REPORT_TEMPLATE_PACKAGE, &[], token)
            .err()
            .unwrap()
            .status,
        Some(429)
    );
    drop(held);
    let download = service
        .prepare_file_download(DOWNLOAD_REPORT_TEMPLATE_PACKAGE, &[], token)
        .unwrap();
    assert!(
        service
            .jobs
            .reserve_download(128 * 1024 * 1024, true)
            .is_err(),
        "response owns its allocated bytes"
    );
    drop(download);
    assert!(
        service
            .jobs
            .reserve_download(128 * 1024 * 1024, true)
            .is_ok()
    );
}
