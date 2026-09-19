#![cfg(feature = "mail")]
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
#[path = "support/smtp.rs"]
mod smtp;
use export_doc_engine::{generated_api::*, invoice::InvoiceDraft, jobs};
use export_doc_report::Builtin;
use serde_json::{Value, json};
use std::{fs, path::PathBuf, sync::atomic::AtomicBool};

fn provision_font(fixture: &native_fixture::Fixture) {
    let manifest = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let workspace = manifest.parent().unwrap().parent().unwrap();
    fs::copy(
        workspace
            .join("Resources")
            .join("Fonts")
            .join("OpenSource")
            .join("NotoSansCJKsc-Regular.otf"),
        fixture.root.join("font.otf"),
    )
    .unwrap();
}

fn configure(fixture: &native_fixture::Fixture, port: u16) -> Value {
    let mut settings = fixture.request(GET_SETTINGS, None, None)["settings"].clone();
    settings["email"]["smtpHost"] = json!("127.0.0.1");
    settings["email"]["smtpPort"] = json!(port);
    settings["email"]["enableSsl"] = json!(false);
    settings["email"]["fromAddress"] = json!("sender@example.invalid");
    settings["email"]["userName"] = json!("sender@example.invalid");
    settings["email"]["password"] = json!("smtp-canary-credential");
    settings["email"]["recipientAllowList"] = json!("example.invalid");
    fixture.request(
        UPDATE_SETTINGS,
        None,
        Some(json!({"settings": settings, "updateSecrets": true})),
    )
}

fn package_items() -> Value {
    json!([
        {"reportType":"ExportDocument","templatePath":Builtin::Invoice.path(),"name":"商业发票","withSeal":true},
        {"reportType":"ExportDocument","templatePath":Builtin::CustomsDeclaration.path(),"name":"报关单","withSeal":false}
    ])
}

fn start_job(
    fixture: &native_fixture::Fixture,
    op: Operation,
    invoice_id: i64,
    body: Value,
) -> BackgroundJobSnapshot {
    serde_json::from_value(
        fixture
            .client()
            .json(
                op,
                &[("invoiceId", invoice_id.to_string())],
                &[],
                Some(body),
            )
            .unwrap(),
    )
    .unwrap()
}

#[test]
fn document_packages_preview_export_and_email_share_template_rendering() {
    let fixture = native_fixture::Fixture::new();
    provision_font(&fixture);
    let customer = fixture.create(
        CREATE_CUSTOMER,
        json!({"customerNameEN":"PACKAGE CUSTOMER","customerNameCN":"单据包客户","email":"package@example.invalid"}),
    );
    let customer_id = customer["id"].as_i64().unwrap();
    let mut draft = InvoiceDraft::demo("2026-09-18", "PKG-EMAIL-001")
        .build()
        .unwrap();
    draft.customer_id = customer_id;
    let saved = fixture.create(CREATE_INVOICE, json!(draft));
    let invoice_id = saved["invoice"]["id"].as_i64().unwrap();
    let parameters = [("invoiceId", invoice_id.to_string())];
    let items = package_items();

    let preview: Value = fixture
        .client()
        .json(
            PREVIEW_INVOICE_DOCUMENT_PACKAGE_HTML,
            &parameters,
            &[],
            Some(json!({"items": items})),
        )
        .unwrap();
    export_doc_contracts::validation::response(PREVIEW_INVOICE_DOCUMENT_PACKAGE_HTML.id, &preview)
        .unwrap();
    let preview_items = preview["items"].as_array().unwrap();
    assert_eq!(preview_items.len(), 2);
    assert!(!preview_items[0]["html"].as_str().unwrap().is_empty());
    assert!(!preview_items[1]["html"].as_str().unwrap().is_empty());
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                PREVIEW_INVOICE_DOCUMENT_PACKAGE_HTML,
                &parameters,
                &[],
                Some(json!({"items": []}))
            )
            .unwrap_err()
            .status,
        Some(400)
    );
    let package_pdf = fixture
        .client()
        .preview_document_package_pdf(invoice_id, &json!({"items": items.clone()}))
        .unwrap();
    assert!(package_pdf.starts_with(b"%PDF-"));

    let download = start_job(
        &fixture,
        START_INVOICE_DOCUMENT_PACKAGE_DOWNLOAD_JOB,
        invoice_id,
        json!({"items": items, "includeMergedPdf": true, "createZip": true, "destinationPath": ""}),
    );
    let finished =
        jobs::wait_for_completion(fixture.client(), download, &AtomicBool::new(false)).unwrap();
    assert_eq!(finished.status, "Succeeded");
    let archive = fixture
        .client()
        .bytes(
            DOWNLOAD_JOB_RESULT,
            &[("jobId", finished.job_id)],
            &[],
            None,
            jobs::PDF_LIMIT,
        )
        .unwrap();
    assert!(archive.starts_with(b"PK"));

    let folder = fixture.root.join("package-folder");
    fs::create_dir_all(&folder).unwrap();
    let save_folder = start_job(
        &fixture,
        START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB,
        invoice_id,
        json!({"items": items, "includeMergedPdf": false, "createZip": false, "destinationPath": folder.to_string_lossy()}),
    );
    let saved_folder =
        jobs::wait_for_completion(fixture.client(), save_folder, &AtomicBool::new(false)).unwrap();
    assert_eq!(saved_folder.status, "Succeeded");
    let mut exported = fs::read_dir(&folder).unwrap();
    let batch = exported.next().unwrap().unwrap().path();
    assert!(exported.next().is_none(), "导出目录应只包含一个批次子目录");
    let pdfs: Vec<_> = fs::read_dir(&batch).unwrap().collect();
    assert!(pdfs.len() >= 2);
    for entry in pdfs {
        assert!(
            fs::read(entry.unwrap().path())
                .unwrap()
                .starts_with(b"%PDF-")
        );
    }

    let zip_path = fixture.root.join("package-archive.zip");
    let save_zip = start_job(
        &fixture,
        START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB,
        invoice_id,
        json!({"items": items, "includeMergedPdf": true, "createZip": true, "destinationPath": zip_path.to_string_lossy()}),
    );
    let saved_zip =
        jobs::wait_for_completion(fixture.client(), save_zip, &AtomicBool::new(false)).unwrap();
    assert_eq!(saved_zip.status, "Succeeded");
    let written = fs::read(&zip_path).unwrap();
    assert!(written.starts_with(b"PK"));
    let local_headers = written.windows(4).filter(|w| w == b"PK\x03\x04").count();
    assert!(
        local_headers >= 3,
        "ZIP 应至少包含两份单据与合并 PDF，实际 {local_headers} 个条目"
    );

    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                START_INVOICE_DOCUMENT_EMAIL_JOB,
                &parameters,
                &[],
                Some(json!({"items": items, "includeMergedPdf": true, "toAddress": "", "subject": "", "body": ""}))
            )
            .unwrap_err()
            .status,
        Some(400)
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                START_INVOICE_DOCUMENT_EMAIL_JOB,
                &parameters,
                &[],
                Some(json!({"items": items, "includeMergedPdf": false, "toAddress": "not an address", "subject": "", "body": ""}))
            )
            .unwrap_err()
            .status,
        Some(400)
    );

    let peer = smtp::Smtp::start(true);
    configure(&fixture, peer.port);
    let email_job = start_job(
        &fixture,
        START_INVOICE_DOCUMENT_EMAIL_JOB,
        invoice_id,
        json!({"items": items, "includeMergedPdf": true, "toAddress": "package@example.invalid", "subject": "", "body": ""}),
    );
    let finished_email =
        jobs::wait_for_completion(fixture.client(), email_job, &AtomicBool::new(false)).unwrap();
    assert_eq!(finished_email.status, "Succeeded");
    let mime = peer
        .body
        .recv_timeout(std::time::Duration::from_secs(6))
        .unwrap();
    assert!(mime.contains("application/pdf"));
    assert!(mime.contains("Export Documents for Invoice PKG-EMAIL-001"));

    let deliveries = fixture.request(LIST_EMAIL_DELIVERIES, None, None);
    export_doc_contracts::validation::response(LIST_EMAIL_DELIVERIES.id, &deliveries).unwrap();
    assert_eq!(deliveries["totalCount"], 1);
    assert_eq!(deliveries["items"][0]["status"], "Sent");
    assert_eq!(deliveries["items"][0]["kind"], "ReportDocumentEmail");

    let repeat = start_job(
        &fixture,
        START_INVOICE_DOCUMENT_EMAIL_JOB,
        invoice_id,
        json!({"items": items, "includeMergedPdf": true, "toAddress": "package@example.invalid", "subject": "", "body": ""}),
    );
    let repeated =
        jobs::wait_for_completion(fixture.client(), repeat, &AtomicBool::new(false)).unwrap();
    assert_eq!(repeated.status, "Succeeded");
    assert_eq!(
        fixture.request(LIST_EMAIL_DELIVERIES, None, None)["totalCount"],
        1
    );

    let fallback = start_job(
        &fixture,
        START_INVOICE_DOCUMENT_EMAIL_JOB,
        invoice_id,
        json!({"items": items, "includeMergedPdf": false, "toAddress": "", "subject": "客户邮箱回退", "body": ""}),
    );
    let fell_back =
        jobs::wait_for_completion(fixture.client(), fallback, &AtomicBool::new(false)).unwrap();
    assert_eq!(fell_back.status, "Succeeded");
    let after = fixture.request(LIST_EMAIL_DELIVERIES, None, None);
    assert_eq!(after["totalCount"], 2);
    assert_eq!(after["items"][0]["recipient"], "package@example.invalid");
    assert_eq!(after["items"][0]["subject"], "客户邮箱回退");
}
