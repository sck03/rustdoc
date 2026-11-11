#![cfg(all(feature = "excel", feature = "single-window"))]
#[path = "../../export-doc-excel/tests/support/coo_template.rs"]
mod coo_fixture;
#[path = "support/jobs.rs"]
pub mod jobs;
#[path = "support/native_fixture.rs"]
mod native_fixture;
use export_doc_engine::{generated_api::*, invoice::InvoiceDraft};
use native_fixture::Fixture;
use serde_json::{Value, json};
use std::{fs, path::Path, sync::atomic::AtomicBool};

#[test]
fn standalone_template_permissions_and_fixed_mapping_roundtrip() {
    let f = Fixture::new();
    let template = f
        .root
        .join("Resources/ExcelTemplates/single-window-coo-template.xlsx");
    fs::create_dir_all(template.parent().unwrap()).unwrap();
    fs::copy(
        Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../Resources/ExcelTemplates/single-window-coo-template.xlsx"),
        template,
    )
    .unwrap();
    let user = |operate: bool, jobs: bool| {
        let name = format!("sw-template-{operate}-{jobs}");
        let mut grants =
            vec![json!({"resourceKey":"document.single-window","action":"view","dataScope":"own"})];
        if operate {
            grants.push(json!({"resourceKey":"document.single-window","action":"operate","dataScope":"own"}));
        }
        if jobs {
            for action in ["view", "operate"] {
                grants
                    .push(json!({"resourceKey":"document.jobs","action":action,"dataScope":"own"}));
            }
        }
        let scheme = f.client().publish_scheme(f.create(
            CREATE_PERMISSION_TEMPLATE,
            json!({"code":name,"name":name,"isActive":true,"grants":grants}),
        ));
        f.create(CREATE_USER_ACCOUNT, json!({"username":name,"fullName":name,"role":"User","companyScope":"DEFAULT","departmentId":"GENERAL",
            "permissionTemplateId":scheme["id"],"permissionSchemeVersion":scheme["publishedVersion"],"isActive":true,"resetPassword":"Sw-Test-2026"}));
        f.client()
            .login(name.into(), "Sw-Test-2026".into())
            .unwrap()
            .0
    };
    let editor = user(true, true);
    let viewer = user(false, false);
    let without_jobs = user(true, false);
    for operation in [
        START_SINGLE_WINDOW_EXCEL_TEMPLATE_DOWNLOAD_JOB,
        START_SINGLE_WINDOW_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
    ] {
        for denied in [&viewer, &without_jobs] {
            assert_eq!(
                denied
                    .json::<Value>(
                        operation,
                        &[],
                        &[],
                        Some(json!({"destinationPath":f.root.join("denied.xlsx")}))
                    )
                    .unwrap_err()
                    .status,
                Some(403)
            );
        }
    }
    assert_eq!(
        editor
            .json::<Value>(START_EXCEL_TEMPLATE_DOWNLOAD_JOB, &[], &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let start = editor
        .json(
            START_SINGLE_WINDOW_EXCEL_TEMPLATE_DOWNLOAD_JOB,
            &[],
            &[],
            None,
        )
        .unwrap();
    let job = jobs::wait_for_completion(&editor, start, &AtomicBool::new(false)).unwrap();
    let bytes = editor
        .bytes(
            DOWNLOAD_JOB_RESULT,
            &[("jobId", job.job_id)],
            &[],
            None,
            25 * 1024 * 1024,
        )
        .unwrap();
    let destination = f.root.join("single-window.xlsx");
    let start = editor
        .json(
            START_SINGLE_WINDOW_EXCEL_TEMPLATE_SAVE_TO_PATH_JOB,
            &[],
            &[],
            Some(json!({"destinationPath":destination})),
        )
        .unwrap();
    jobs::wait_for_completion(&editor, start, &AtomicBool::new(false)).unwrap();
    assert_eq!(fs::read(destination).unwrap(), bytes);
    assert_eq!(bytes, coo_fixture::TEMPLATE);

    let imported = editor
        .upload(
            PREVIEW_SINGLE_WINDOW_EXCEL_IMPORT,
            &[],
            json!({}),
            "coo.xlsx",
            &coo_fixture::filled(&[(1, "B22", "THAILAND"), (2, "F4", "KGS")]),
        )
        .unwrap();
    assert_eq!(imported["result"]["success"], true, "{imported}");
    let payload = json!({"referenceNo":"COO-FACTORIES","invoice":imported["result"]["invoice"],"coo":imported["coo"]});
    for invalid_coo in [
        json!({"items":[]}),
        json!({"items":[{"fobValue":"-1"},{}]}),
        json!({"producer":"bad\u{1}","items":[{},{}]}),
        json!({"items":[{"fobValue":"9999999999999999999"},{"fobValue":"1"}]}),
    ] {
        let mut body = payload.clone();
        body["coo"] = invalid_coo;
        assert_eq!(
            editor
                .json::<Value>(CREATE_SINGLE_WINDOW_SOURCE, &[], &[], Some(body))
                .unwrap_err()
                .status,
            Some(400)
        );
    }
    assert_eq!(
        editor
            .json::<Value>(LIST_SINGLE_WINDOW_SOURCES, &[], &[], None)
            .unwrap()["totalCount"],
        0
    );
    let created: Value = editor
        .json(CREATE_SINGLE_WINDOW_SOURCE, &[], &[], Some(payload))
        .unwrap();
    let source_path = [("sourceId", created["id"].to_string())];
    let document: Value = editor
        .json(GET_CUSTOMS_COO_DOCUMENT, &source_path, &[], None)
        .unwrap();
    assert_eq!(document["certType"], "RC");
    assert_eq!(document["fobValue"], "130.12345");
    assert_eq!(document["totalAmt"], "151.25");
    assert_eq!(document["items"][0]["ciqRegNo"], "012345678");
    assert_eq!(document["items"][0]["goodsUnit"], "千克");
    assert_eq!(document["items"][0]["goodsOriginCountryEn"], "THAILAND");
    assert_eq!(document["items"][1]["goodsOriginCountryEn"], "VIETNAM");
    assert_eq!(
        document["items"][0]["producer"],
        "TEST FACTORY ONE LTD.\n3 FACTORY ROAD, CHINA"
    );
    assert_eq!(document["items"][1]["prdcEtpsConcEr"], "李四");
    assert_eq!(document["items"][0]["sourceItemId"], 1);
    assert_eq!(document["items"][1]["sourceItemId"], 2);
    let agent: Value = editor
        .json(GET_AGENT_CONSIGNMENT_DOCUMENT, &source_path, &[], None)
        .unwrap();
    assert_eq!(agent["declTotal"], "151.25");

    let invoice = InvoiceDraft::demo("2026-11-10", "SW-TEMPLATE-001")
        .build()
        .unwrap();
    let invoice_template =
        include_bytes!("../../../Resources/ExcelTemplates/invoice-import-template.xlsx");
    let filled =
        export_doc_excel::booking_from_invoice(invoice_template, &invoice, &|| Ok(())).unwrap();
    let mut settings: Value = f.client().json(GET_SETTINGS, &[], &[], None).unwrap();
    let mut mapping = settings["settings"]["excelImport"].clone();
    mapping["schemeName"] = json!("Customer-A");
    settings["settings"]["excelImportSchemes"] = json!([mapping]);
    settings["settings"]["excelImport"]["styleNoCol"] = json!(128);
    f.create(
        UPDATE_SETTINGS,
        json!({"settings":settings["settings"],"updateSecrets":false}),
    );
    let preview = editor
        .upload(
            PREVIEW_SINGLE_WINDOW_EXCEL_IMPORT,
            &[],
            json!({}),
            "filled.xlsx",
            &filled,
        )
        .unwrap()["result"]
        .clone();
    assert_eq!(preview["success"], true);
    let manual = editor
        .upload(
            PREVIEW_SINGLE_WINDOW_EXCEL_IMPORT,
            &[],
            json!({"schemeName":"Customer-A"}),
            "filled.xlsx",
            &filled,
        )
        .unwrap()["result"]
        .clone();
    assert_eq!(manual["invoice"]["items"], preview["invoice"]["items"]);
    assert_eq!(
        editor
            .upload(
                PREVIEW_SINGLE_WINDOW_EXCEL_IMPORT,
                &[],
                json!({"schemeName":"missing"}),
                "filled.xlsx",
                &filled
            )
            .unwrap_err()
            .status,
        Some(400)
    );
    assert_eq!(
        preview["invoice"]["customerNameEN"],
        invoice.customer_name_en
    );
    assert_eq!(
        preview["invoice"]["items"][0]["styleNo"],
        invoice.items[0].style_no
    );
    assert_eq!(f.request(LIST_INVOICES, None, None)["totalCount"], 0);
    let source: Value = editor
        .json(
            CREATE_SINGLE_WINDOW_SOURCE,
            &[],
            &[],
            Some(json!({"referenceNo":"SW-TEMPLATE-001","invoice":preview["invoice"]})),
        )
        .unwrap();
    let path = [("sourceId", source["id"].to_string())];
    let coo: Value = editor
        .json(GET_CUSTOMS_COO_DOCUMENT, &path, &[], None)
        .unwrap();
    let acd: Value = editor
        .json(GET_AGENT_CONSIGNMENT_DOCUMENT, &path, &[], None)
        .unwrap();
    assert_eq!(coo["items"].as_array().unwrap().len(), invoice.items.len());
    assert!(!acd["gName"].as_str().unwrap().is_empty());
}
