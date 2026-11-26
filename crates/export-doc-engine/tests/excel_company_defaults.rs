#![cfg(feature = "excel")]
#[path = "support/jobs.rs"]
pub mod jobs;
#[path = "support/native_fixture.rs"]
mod native_fixture;

use export_doc_engine::{generated_api::*, invoice::InvoiceDraft};
use native_fixture::Fixture;
use serde_json::{Value, json};
use std::{
    fs,
    io::{Cursor, Read},
    path::Path,
    sync::atomic::AtomicBool,
};

#[test]
fn excel_preserves_explicit_exporter_and_defaults_to_the_current_company() {
    let mut fixture = Fixture::new();
    let source = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../Resources/ExcelTemplates/invoice-import-template.xlsx");
    let template = fs::read(source).unwrap();
    let target = fixture
        .root
        .join("Resources/ExcelTemplates/invoice-import-template.xlsx");
    fs::create_dir_all(target.parent().unwrap()).unwrap();
    fs::write(target, &template).unwrap();
    let directory = fixture.request(GET_ORGANIZATION_DIRECTORY, None, None);
    let company = &directory["companies"][0];
    fixture.request(
        UPDATE_ORGANIZATION_COMPANY,
        company["id"].as_i64(),
        Some(json!({
            "code":"DEFAULT", "name":"更新后的组织公司名称", "isActive":true,
            "expectedVersion":company["versionNumber"]
        })),
    );
    fixture.create(
        CREATE_ORGANIZATION_COMPANY,
        json!({"code":"OTHER","name":"另一单位公司名称","isActive":true}),
    );
    for (code, name) in [
        ("DEFAULT", "更新后的组织公司名称"),
        ("OTHER", "另一单位公司名称"),
        ("", ""),
    ] {
        let accounts = fixture.request(LIST_USERS, None, None);
        let mut account = accounts["users"][0].clone();
        account["expectedVersion"] = account["versionNumber"].clone();
        account["companyScope"] = json!(code);
        account["departmentId"] = json!("");
        fixture.request(UPDATE_USER_ACCOUNT, account["id"].as_i64(), Some(account));
        fixture.client = Some(
            fixture
                .client()
                .login("admin".into(), String::new())
                .unwrap()
                .0,
        );
        for supplied in ["", "Excel原有出口商名称"] {
            let mut invoice = InvoiceDraft::demo("2026-11-26", "COMPANY-DEFAULT-1");
            invoice.header.exporter_name_cn = supplied.into();
            let invoice = invoice.build().unwrap();
            let bytes =
                export_doc_excel::booking_from_invoice(&template, &invoice, &|| Ok(())).unwrap();
            let expected = if supplied.is_empty() { name } else { supplied };
            let path = fixture.root.join("导入.xlsx");
            fs::write(&path, &bytes).unwrap();
            let uploaded = fixture
                .client()
                .upload(
                    PREVIEW_UPLOADED_EXCEL_IMPORT,
                    &[],
                    json!({}),
                    "导入.xlsx",
                    &bytes,
                )
                .unwrap();
            let local = fixture.request(PREVIEW_EXCEL_IMPORT, None, Some(json!({"filePath":path})));
            for preview in [uploaded, local] {
                assert_eq!(preview["success"], true);
                assert_eq!(preview["invoice"]["exporterNameCN"], expected);
                assert_eq!(preview["exporter"]["exporterNameCN"], expected);
                assert_eq!(
                    preview["invoice"]["exporterNameEN"],
                    invoice.exporter_name_en
                );
            }
        }
        for operation in [
            START_EXCEL_TEMPLATE_DOWNLOAD_JOB,
            START_BLANK_BOOKING_SHEET_DOWNLOAD_JOB,
        ] {
            let start: BackgroundJobSnapshot =
                fixture.client().json(operation, &[], &[], None).unwrap();
            let done = jobs::wait_for_completion(fixture.client(), start, &AtomicBool::new(false))
                .unwrap();
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
            assert_eq!(first_cell_text(&sheet), name);
        }
        let invoices: Value = fixture
            .client()
            .json(LIST_INVOICES, &[], &[], None)
            .unwrap();
        assert_eq!(
            invoices["totalCount"], 0,
            "preview never saves business records"
        );
    }
}

fn first_cell_text(sheet: &str) -> String {
    use quick_xml::{Reader, events::Event};
    let mut reader = Reader::from_str(sheet);
    let mut first = false;
    loop {
        match reader.read_event().unwrap() {
            Event::Start(node) if node.local_name().as_ref() == "c" => {
                first = node
                    .attributes()
                    .any(|a| a.is_ok_and(|a| a.key.as_ref() == "r" && a.value.as_ref() == "A1"));
            }
            Event::Start(node) if first && node.local_name().as_ref() == "t" => {
                return reader.read_text(node.name()).unwrap().to_string();
            }
            Event::End(node) if first && node.local_name().as_ref() == "c" => return String::new(),
            Event::Eof => panic!("Missing A1 exporter cell"),
            _ => {}
        }
    }
}
