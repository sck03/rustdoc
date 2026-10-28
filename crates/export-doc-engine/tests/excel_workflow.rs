#![cfg(feature = "excel")]
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{
    contracts,
    generated_api::*,
    invoice::InvoiceDraft,
    paths::{RuntimePaths, nonce},
};
use native_fixture::ApiClient;
use serde_json::{Value, json};
#[path = "support/jobs.rs"]
pub mod jobs;
use std::{fs, path::PathBuf, sync::atomic::AtomicBool};

#[test]
fn excel_preview_export_backup_and_restore_keep_one_authoritative_database() {
    let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let root = workspace
        .join(".codex-runtime/excel-workflow-tests")
        .join(nonce().unwrap());
    fs::create_dir_all(&root).unwrap();
    let paths = RuntimePaths::server(&workspace, &root.join("Data")).unwrap();
    let client = ApiClient::native(paths)
        .unwrap()
        .login("admin".into(), "".into())
        .unwrap()
        .0;
    let invoice = client
        .save_invoice(
            &InvoiceDraft::demo("2026-09-16", "EXCEL-SERVICE-001")
                .build()
                .unwrap(),
        )
        .unwrap();
    let start: BackgroundJobSnapshot = client
        .json(
            START_INVOICE_BOOKING_SHEET_DOWNLOAD_JOB,
            &[("invoiceId", invoice.id.to_string())],
            &[],
            None,
        )
        .unwrap();
    let job = jobs::wait_for_completion(&client, start, &AtomicBool::new(false)).unwrap();
    let parameters = [("jobId", job.job_id.clone())];
    let bytes = client
        .bytes(DOWNLOAD_JOB_RESULT, &parameters, &[], None, jobs::PDF_LIMIT)
        .unwrap();
    let preview = client
        .upload(
            PREVIEW_UPLOADED_EXCEL_IMPORT,
            &[],
            json!({}),
            "导入.xlsx",
            &bytes,
        )
        .unwrap();
    export_doc_contracts::validation::response(PREVIEW_UPLOADED_EXCEL_IMPORT.id, &preview).unwrap();
    assert_eq!(preview["success"], true);
    assert_eq!(preview["invoice"]["invoiceNo"], invoice.invoice_no);
    let invoices: Value = client.json(LIST_INVOICES, &[], &[], None).unwrap();
    assert_eq!(invoices["totalCount"], 1, "preview must not insert records");
    let booking_path = root.join("发票托单.xlsx");
    for id in [Value::Null, json!(0), json!(-1)] {
        let error = client
            .json::<Value>(
                START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
                &[],
                &[],
                Some(json!({"invoiceId": id, "destinationPath": booking_path})),
            )
            .unwrap_err();
        assert_eq!(error.status, Some(400));
        assert!(!booking_path.exists());
    }
    let saved: BackgroundJobSnapshot = client
        .json(
            START_INVOICE_BOOKING_SHEET_SAVE_TO_PATH_JOB,
            &[],
            &[],
            Some(json!({"invoiceId": invoice.id, "destinationPath": booking_path})),
        )
        .unwrap();
    jobs::wait_for_completion(&client, saved, &AtomicBool::new(false)).unwrap();
    let saved_preview = client
        .upload(
            PREVIEW_UPLOADED_EXCEL_IMPORT,
            &[],
            json!({}),
            "发票托单.xlsx",
            &fs::read(&booking_path).unwrap(),
        )
        .unwrap();
    assert_eq!(saved_preview["invoice"], preview["invoice"]);
    let after_preview: Value = client.json(LIST_INVOICES, &[], &[], None).unwrap();
    assert_eq!(after_preview["totalCount"], 1);
    assert_eq!(
        client
            .upload(
                PREVIEW_UPLOADED_EXCEL_IMPORT,
                &[],
                json!({}),
                "../escape.xlsx",
                &bytes
            )
            .unwrap_err()
            .status,
        Some(400)
    );

    let destination = root.join("选择的模板.xlsx");
    let start: BackgroundJobSnapshot = client
        .json(
            START_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
            &[],
            &[],
            Some(json!({"destinationPath":destination})),
        )
        .unwrap();
    jobs::wait_for_completion(&client, start, &AtomicBool::new(false)).unwrap();
    assert!(fs::read(&destination).unwrap().starts_with(b"PK"));
    let same_path = client
        .json::<Value>(
            START_BOOKING_SHEET_CONVERT_SAVE_TO_PATH_JOB,
            &[],
            &[],
            Some(json!({"sourcePath":destination,"destinationPath":destination})),
        )
        .unwrap_err();
    assert_eq!(same_path.status, Some(400));

    let _: Value = client.json(CREATE_DATABASE_BACKUP, &[], &[], None).unwrap();
    let backups: Value = client.json(LIST_DATABASE_BACKUPS, &[], &[], None).unwrap();
    let name = backups["backups"][0]["fileName"].as_str().unwrap();
    let _: Value = client.json(DELETE_JOB, &parameters, &[], None).unwrap();
    let body = contracts::overlay(
        contracts::object(RESTORE_DATABASE_BACKUP.id, true),
        &json!({"backupFileName":name,"confirmationText":"RESTORE"}),
    );
    let _: Value = client
        .json(RESTORE_DATABASE_BACKUP, &[], &[], Some(body))
        .unwrap();
    let authenticated = client.login("admin".into(), "".into()).unwrap().0;
    assert_eq!(
        authenticated
            .bytes(DOWNLOAD_JOB_RESULT, &parameters, &[], None, jobs::PDF_LIMIT)
            .unwrap(),
        bytes
    );
    drop(authenticated);
    drop(client);
    fs::remove_dir_all(root).unwrap();
}
