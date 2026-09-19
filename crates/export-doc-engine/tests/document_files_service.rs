#![cfg(all(feature = "excel", feature = "invoice-transfer"))]
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{generated_api::*, invoice::InvoiceDraft, jobs};
use serde_json::{Value, json};
use std::{
    io::{Cursor, Read, Write},
    sync::atomic::AtomicBool,
};

#[test]
fn invoice_package_preview_conflicts_and_atomic_rejection_preserve_business_data() {
    let source = native_fixture::Fixture::new();
    let invoice = source
        .client()
        .save_invoice(
            &InvoiceDraft::demo("2026-09-17", "TRANSFER-001")
                .build()
                .unwrap(),
        )
        .unwrap();
    let bytes = source
        .client()
        .bytes(
            DOWNLOAD_INVOICE_TRANSFER_PACKAGE,
            &[("id", invoice.id.to_string())],
            &[],
            None,
            25 * 1024 * 1024,
        )
        .unwrap();
    let mut zip = zip::ZipArchive::new(Cursor::new(&bytes)).unwrap();
    let mut data = String::new();
    zip.by_name("data.json")
        .unwrap()
        .read_to_string(&mut data)
        .unwrap();
    let data: Value = serde_json::from_str(&data).unwrap();
    assert_eq!(data["SchemaVersion"], "1.0");
    assert_eq!(data["Invoice"]["InvoiceNo"], "TRANSFER-001");
    assert!(data["Invoice"].get("CompanyScope").is_none());
    assert!(data["Invoice"].get("RowVersion").is_none());
    assert!(data["Items"][0].get("HSCode").is_some());
    let target = native_fixture::Fixture::new();
    let upload = |op, body| {
        target
            .client()
            .upload(op, &[], body, "单据.edpkg", &bytes)
            .unwrap()
    };
    let preview = upload(PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE, json!({}));
    export_doc_contracts::validation::response(
        PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE.id,
        &preview,
    )
    .unwrap();
    assert_eq!(preview["preview"]["invoiceExists"], false);
    assert_eq!(target.request(LIST_INVOICES, None, None)["totalCount"], 0);
    let imported = upload(
        IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
        json!({"conflictAction":"Skip"}),
    );
    export_doc_contracts::validation::response(
        IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE.id,
        &imported,
    )
    .unwrap();
    let id = imported["result"]["invoiceId"].as_i64().unwrap();
    let saved = target.request(GET_INVOICE, Some(id), None);
    assert_eq!(
        saved["totalAmount"],
        serde_json::to_value(invoice.total_amount).unwrap()
    );
    let skipped = upload(
        IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
        json!({"conflictAction":"Skip"}),
    );
    assert_eq!(skipped["result"]["invoiceId"], id);
    assert_eq!(
        target.request(GET_INVOICE, Some(id), None)["rowVersion"],
        saved["rowVersion"]
    );
    upload(
        IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
        json!({"conflictAction":"AppendItems"}),
    );
    let appended = target.request(GET_INVOICE, Some(id), None);
    assert_eq!(
        appended["items"].as_array().unwrap().len(),
        invoice.items.len() * 2
    );
    assert_eq!(appended["items"][0]["id"], saved["items"][0]["id"]);
    upload(
        IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
        json!({"conflictAction":"Overwrite"}),
    );
    assert_eq!(
        target.request(GET_INVOICE, Some(id), None)["items"]
            .as_array()
            .unwrap()
            .len(),
        invoice.items.len()
    );
    let renamed = upload(
        IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
        json!({"conflictAction":"NewInvoiceNo","newInvoiceNo":"TRANSFER-002"}),
    );
    assert_eq!(renamed["result"]["finalInvoiceNo"], "TRANSFER-002");
    let before = target.request(LIST_INVOICES, None, None)["totalCount"].clone();
    let mut corrupt = zip::ZipWriter::new(Cursor::new(vec![]));
    for (name, content) in [
        ("data.json", serde_json::to_vec(&data).unwrap()),
        ("meta.json", br#"{"checksum":"bad"}"#.to_vec()),
    ] {
        corrupt
            .start_file(name, zip::write::SimpleFileOptions::default())
            .unwrap();
        corrupt.write_all(&content).unwrap();
    }
    let corrupt = corrupt.finish().unwrap().into_inner();
    let invalid = target
        .client()
        .upload(
            IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
            &[],
            json!({"conflictAction":"NewInvoiceNo"}),
            "无效.edpkg",
            &corrupt,
        )
        .unwrap_err();
    assert_eq!(invalid.status, Some(400));
    assert_eq!(
        target.request(LIST_INVOICES, None, None)["totalCount"],
        before
    );
}

#[test]
fn query_filters_shipment_date_and_exports_the_same_sorted_records() {
    let fixture = native_fixture::Fixture::new();
    for (number, shipment) in [
        ("Q-100", "2026-10-01"),
        ("Q-200", "2026-10-02"),
        ("Q-300", "2026-09-17"),
    ] {
        let mut draft = InvoiceDraft::demo("2026-09-17", number);
        draft.header.shipment_date = shipment.into();
        fixture
            .client()
            .save_invoice(&draft.build().unwrap())
            .unwrap();
    }
    let query = [
        ("startDate", "2026-10-01".into()),
        ("endDateExclusive", "2026-10-03".into()),
    ];
    let selected: Value = fixture
        .client()
        .json(LIST_QUERIED_INVOICES, &[], &query, None)
        .unwrap();
    export_doc_contracts::validation::response(LIST_QUERIED_INVOICES.id, &selected).unwrap();
    assert_eq!(selected["totalCount"], 2);
    assert_eq!(selected["items"][0]["invoiceNo"], "Q-200");
    let prefix: Value = fixture
        .client()
        .json(
            LIST_QUERIED_INVOICES,
            &[],
            &[("keyword", "Q-1".into())],
            None,
        )
        .unwrap();
    assert_eq!(prefix["totalCount"], 1);
    let suffix: Value = fixture
        .client()
        .json(
            LIST_QUERIED_INVOICES,
            &[],
            &[("keyword", "100".into())],
            None,
        )
        .unwrap();
    assert_eq!(
        suffix["totalCount"], 0,
        "identifier searches are prefixes, not arbitrary JSON substrings"
    );
    let bad = fixture
        .client()
        .json::<Value>(
            LIST_QUERIED_INVOICES,
            &[],
            &[("startDate", "2026-02-30".into())],
            None,
        )
        .unwrap_err();
    assert_eq!(bad.status, Some(400));
    let start: BackgroundJobSnapshot = fixture
        .client()
        .json(
            DOWNLOAD_QUERIED_INVOICES,
            &[],
            &[],
            Some(json!({"startDate":"2026-10-01","endDateExclusive":"2026-10-03"})),
        )
        .unwrap();
    let done = jobs::wait_for_completion(fixture.client(), start, &AtomicBool::new(false)).unwrap();
    let bytes = fixture
        .client()
        .bytes(
            DOWNLOAD_JOB_RESULT,
            &[("jobId", done.job_id)],
            &[],
            None,
            jobs::PDF_LIMIT,
        )
        .unwrap();
    let mut zip = zip::ZipArchive::new(Cursor::new(bytes)).unwrap();
    let mut sheet = String::new();
    zip.by_name("xl/worksheets/sheet1.xml")
        .unwrap()
        .read_to_string(&mut sheet)
        .unwrap();
    assert!(sheet.contains("Q-100") && sheet.contains("Q-200"));
    assert!(!sheet.contains("Q-300"));
    assert!(sheet.contains("船期/航期"));
}
