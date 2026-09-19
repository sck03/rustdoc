#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{api::ApiClient, generated_api::*};
use serde_json::{Value, json};
use std::{
    fs,
    time::{Duration, SystemTime},
};

#[test]
fn backup_retention_restore_confirmation_and_session_revocation_use_the_original_workflow() {
    let mut fixture = native_fixture::Fixture::new();
    fixture.create(CREATE_CUSTOMER, json!({"customerNameEN":"BEFORE-BACKUP"}));
    fixture.request(CREATE_DATABASE_BACKUP, None, None);
    let list = fixture.request(LIST_DATABASE_BACKUPS, None, None);
    export_doc_contracts::validation::response(LIST_DATABASE_BACKUPS.id, &list).unwrap();
    let backup = list["backups"][0].clone();
    let filename = backup["fileName"].as_str().unwrap();
    let file = fixture.root.join("Backups").join(filename);
    assert_eq!(backup["fullPath"].as_str().unwrap(), file.to_str().unwrap());
    fs::File::options()
        .write(true)
        .open(&file)
        .unwrap()
        .set_times(
            fs::FileTimes::new().set_modified(SystemTime::now() - Duration::from_secs(60 * 86400)),
        )
        .unwrap();
    fixture.request(CREATE_DATABASE_BACKUP, None, None);
    let list = fixture.request(LIST_DATABASE_BACKUPS, None, None);
    let newest = list["backups"][0]["fileName"].as_str().unwrap().to_owned();
    let newest_path = fixture.root.join("Backups").join(&newest);
    fs::File::options()
        .write(true)
        .open(&newest_path)
        .unwrap()
        .set_times(
            fs::FileTimes::new().set_modified(SystemTime::now() - Duration::from_secs(55 * 86400)),
        )
        .unwrap();
    let cleaned = fixture.request(
        CLEANUP_DATABASE_BACKUPS,
        None,
        Some(json!({"daysToKeep":30})),
    );
    export_doc_contracts::validation::response(CLEANUP_DATABASE_BACKUPS.id, &cleaned).unwrap();
    assert_eq!(cleaned["backups"].as_array().unwrap().len(), 1);
    assert_eq!(cleaned["backups"][0]["fileName"], newest);
    assert!(!file.exists());
    fixture.create(CREATE_CUSTOMER, json!({"customerNameEN":"AFTER-BACKUP"}));
    let wrong = fixture
        .client()
        .json::<Value>(
            RESTORE_DATABASE_BACKUP,
            &[],
            &[],
            Some(json!({"backupFileName":newest,"confirmationText":"恢复"})),
        )
        .unwrap_err();
    assert_eq!(wrong.status, Some(400));
    assert!(
        fixture
            .client()
            .json::<Value>(GET_CURRENT_USER, &[], &[], None)
            .is_ok()
    );
    fixture.request(
        RESTORE_DATABASE_BACKUP,
        None,
        Some(json!({"backupFileName":newest,"confirmationText":"RESTORE"})),
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(GET_CURRENT_USER, &[], &[], None)
            .unwrap_err()
            .status,
        Some(401)
    );
    let client = fixture.client.take().unwrap();
    fixture.client = Some(client.login("admin".into(), String::new()).unwrap().0);
    let customers: Value = fixture
        .client()
        .json(LIST_CUSTOMERS, &[], &[], None)
        .unwrap();
    assert!(
        customers
            .as_array()
            .unwrap()
            .iter()
            .any(|row| row["customerNameEN"] == "BEFORE-BACKUP")
    );
    assert!(
        !customers
            .as_array()
            .unwrap()
            .iter()
            .any(|row| row["customerNameEN"] == "AFTER-BACKUP")
    );
    drop(
        ApiClient::native(fixture.paths.clone())
            .err()
            .expect("database remains single-instance"),
    );
}
