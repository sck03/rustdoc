use super::*;
use crate::engine::test_support::{ScratchDirectory, TestService, administrator};
use export_doc_storage::AuditWrite;
use std::{
    fs,
    io::Read,
    time::{Duration, SystemTime},
};

#[test]
fn support_package_contains_only_diagnostic_summaries() {
    let workspace = ScratchDirectory::new();
    let service = TestService::open(workspace.paths());
    service
        .store
        .transaction(|tx| {
            tx.set_settings(
                "protected-credentials",
                1,
                &json!({"/email/password":"never-include-this-secret"}),
            )?;
            Ok(())
        })
        .unwrap();
    let saved = handle(&service, &administrator(), SAVE_SUPPORT_PACKAGE_TO_RUNTIME).unwrap();
    let output = download(&service, &administrator()).unwrap();
    assert!(std::path::Path::new(saved["fullPath"].as_str().unwrap()).is_file());
    let mut archive = zip::ZipArchive::new(std::io::Cursor::new(output.content)).unwrap();
    assert_eq!(archive.len(), 5);
    for index in 0..archive.len() {
        let mut file = archive.by_index(index).unwrap();
        assert!(file.name().unwrap().starts_with("diagnostics/"));
        let mut text = String::new();
        file.read_to_string(&mut text).unwrap();
        assert!(!text.contains("never-include-this-secret"));
    }
    let mut database = String::new();
    archive
        .by_name("diagnostics/database.json")
        .unwrap()
        .read_to_string(&mut database)
        .unwrap();
    let statistics: Value = serde_json::from_str(&database).unwrap();
    assert!(statistics["totalEvents"].is_number());
    assert!(statistics.get("scannedEvents").is_none());
}

#[test]
fn text_log_cleanup_preserves_audit_history() {
    let workspace = ScratchDirectory::new();
    let paths = workspace.paths();
    let service = TestService::open(paths.clone());
    service
        .store
        .transaction(|tx| {
            tx.append_audit(&AuditWrite {
                kind: "test",
                record_id: 0,
                version: 1,
                action: "create",
                actor_id: 1,
                occurred_at: "2020-01-01T00:00:00Z",
                note: "",
            })?;
            tx.set_settings(
                "settings",
                1,
                &json!({"system":{"logRetentionDays":30,"logRetainedFileCount":14}}),
            )?;
            Ok(())
        })
        .unwrap();
    let before = service
        .store
        .read(|tx| Ok(tx.audit_statistics()?.total_events))
        .unwrap();
    let old_log = paths.log_root.join("2020-01-01.log");
    fs::write(&old_log, b"old log").unwrap();
    fs::File::options()
        .write(true)
        .open(&old_log)
        .unwrap()
        .set_times(
            fs::FileTimes::new()
                .set_modified(SystemTime::UNIX_EPOCH + Duration::from_secs(1_577_836_800)),
        )
        .unwrap();
    let unrelated = paths.log_root.join("keep.txt");
    fs::write(&unrelated, b"not a text log").unwrap();
    let response = handle(&service, &administrator(), CLEANUP_SYSTEM_LOGS).unwrap();
    assert_eq!(response["deletedTextLogsByAge"], 1);
    assert!(response.get("deletedAuditLogs").is_none());
    assert!(!old_log.exists());
    assert!(unrelated.exists());
    let again = handle(&service, &administrator(), CLEANUP_SYSTEM_LOGS).unwrap();
    assert_eq!(again["deletedTextLogs"], 0);
    assert_eq!(
        service
            .store
            .read(|tx| Ok(tx.audit_statistics()?.total_events))
            .unwrap(),
        before
    );
}
