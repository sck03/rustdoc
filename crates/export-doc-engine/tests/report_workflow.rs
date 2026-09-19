use base64::{Engine, engine::general_purpose::STANDARD};
use export_doc_engine::{
    api::ApiClient,
    contracts,
    designer::{Design, field_catalog},
    generated_api::*,
    invoice::InvoiceDraft,
    jobs,
    paths::{RuntimePaths, nonce},
    template,
};
use export_doc_report::Builtin;
use serde_json::{Value, json};
use std::{fs, io::Cursor, path::PathBuf, sync::atomic::AtomicBool};

struct Fixture {
    root: PathBuf,
    client: Option<ApiClient>,
}
impl Fixture {
    fn new() -> Self {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf();
        let root = workspace
            .join(".codex-runtime/report-workflow-tests")
            .join(nonce().unwrap());
        fs::create_dir_all(&root).unwrap();
        let paths = RuntimePaths::server(&workspace, &root.join("Data")).unwrap();
        let client = ApiClient::native(paths)
            .unwrap()
            .login("admin".into(), "".into())
            .unwrap()
            .0;
        Self {
            root,
            client: Some(client),
        }
    }
    fn client(&self) -> &ApiClient {
        self.client.as_ref().unwrap()
    }
    fn request(
        &self,
        op: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
    ) -> Value {
        let value = self
            .client()
            .json(op, parameters, query, body)
            .unwrap_or_else(|error| panic!("{}: {error}", op.id));
        export_doc_contracts::validation::response(op.id, &value)
            .unwrap_or_else(|error| panic!("{}: {error}", op.id));
        value
    }
    fn create(&self, op: Operation, body: Value) -> Value {
        self.request(
            op,
            &[],
            &[],
            Some(contracts::overlay(contracts::object(op.id, true), &body)),
        )
    }
    fn fields(&self) -> ApiReportTemplateFieldCatalogResponse {
        serde_json::from_value(self.request(
            GET_REPORT_TEMPLATE_FIELD_CATALOG,
            &[],
            &[("reportType", "ExportDocument".into())],
            None,
        ))
        .unwrap()
    }
    fn png() -> Vec<u8> {
        let image = image::RgbaImage::from_pixel(32, 20, image::Rgba([180, 30, 50, 255]));
        let mut bytes = Cursor::new(vec![]);
        image.write_to(&mut bytes, image::ImageFormat::Png).unwrap();
        bytes.into_inner()
    }
    fn user(&self, name: &str) -> ApiClient {
        let permissions = self.create(
            CREATE_PERMISSION_TEMPLATE,
            json!({"code":format!("ROLE-{name}"),"name":name,"isActive":true,"grants":[
                {"resourceKey":"document.report-templates","action":"view","dataScope":"company"},
                {"resourceKey":"document.report-templates","action":"design","dataScope":"own"},
                {"resourceKey":"document.report-resources","action":"view","dataScope":"own"},
                {"resourceKey":"document.report-resources","action":"upload","dataScope":"own"},
                {"resourceKey":"document.report-resources","action":"recycle","dataScope":"own"},
                {"resourceKey":"document.invoices","action":"view","dataScope":"own"},
                {"resourceKey":"document.invoices","action":"operate","dataScope":"own"}
            ]}),
        );
        self.create(CREATE_USER_ACCOUNT, json!({"username":name,"fullName":name,"role":"User","permissionTemplateId":permissions["id"],"departmentId":"GENERAL","companyScope":"DEFAULT","isActive":true,"resetPassword":"Report-Test-2026"}));
        self.client()
            .login(name.into(), "Report-Test-2026".into())
            .unwrap()
            .0
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        self.client.take();
        if self
            .root
            .parent()
            .and_then(|p| p.file_name())
            .is_some_and(|n| n == "report-workflow-tests")
            && self
                .root
                .file_name()
                .is_some_and(|n| n.to_string_lossy().len() == 32)
        {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}

#[test]
fn builtins_images_pdf_jobs_and_backup_restore_share_the_business_store() {
    let fixture = Fixture::new();
    let exporter = fixture.create(
        CREATE_EXPORTER,
        json!({"exporterNameEN":"REPORT TEST EXPORTER","exporterNameCN":"报表验证出口单位"}),
    );
    let exporter_id = exporter["id"].as_i64().unwrap();
    for kind in ["document", "customs"] {
        let uploaded = fixture
            .client()
            .upload(
                UPLOAD_EXPORTER_SEAL,
                &[("id", exporter_id.to_string()), ("sealType", kind.into())],
                json!({}),
                "验证印章.png",
                &Fixture::png(),
            )
            .unwrap();
        export_doc_contracts::validation::response(UPLOAD_EXPORTER_SEAL.id, &uploaded).unwrap();
    }
    let shipping = fixture.request(SAVE_SHIPPING_MARK_IMAGE, &[], &[], Some(json!({"imageDataUrl":format!("data:image/png;base64,{}",STANDARD.encode(Fixture::png()))})));
    let mut draft = InvoiceDraft::demo("2026-09-16", "REPORT-JOB-001");
    draft.header.exporter_id = exporter_id;
    draft.header.shipping_marks_type = "Image".into();
    draft.header.shipping_marks_image = shipping["imagePath"].as_str().unwrap().into();
    let invoice = fixture
        .client()
        .save_invoice(&draft.build().unwrap())
        .unwrap();
    let invoice_parameters = [("invoiceId", invoice.id.to_string())];
    let catalog = fixture.request(
        LIST_REPORT_TEMPLATES,
        &[],
        &[("reportType", "ExportDocument".into())],
        None,
    );
    assert_eq!(catalog.as_array().unwrap().len(), 4);
    let preview = fixture.request(
        PREVIEW_INVOICE_REPORT_HTML,
        &invoice_parameters,
        &[],
        Some(json!({"templatePath":Builtin::Invoice.path(),"withSeal":true})),
    );
    assert_eq!(
        preview["html"]
            .as_str()
            .unwrap()
            .matches("data:image/png;base64,")
            .count(),
        2
    );
    let pdf = jobs::render_report_pdf(
        fixture.client(),
        START_INVOICE_REPORT_PDF_DOWNLOAD_JOB,
        &invoice_parameters,
        json!({"templatePath":Builtin::CustomsDeclaration.path(),"withSeal":true}),
        &AtomicBool::new(false),
        &mut |_| {},
    )
    .unwrap();
    assert!(pdf.starts_with(b"%PDF-"));
    let payment = fixture.create(CREATE_PAYMENT, json!({"voucherNo":"PAY-REPORT-001","payeeName":"测试收款单位","paymentDate":"2026-09-16","shipmentDate":"2026-09-16","cnyAmount":1234.56,"travelExpense":1234.56,"paymentMethod":"电汇","notes":"报销验证"}));
    let payment = &payment["payment"];
    for template in [Builtin::PaymentVoucher, Builtin::ExpenseReimbursement] {
        let pdf = jobs::render_report_pdf(
            fixture.client(),
            START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB,
            &[("paymentId", payment["id"].to_string())],
            json!({"templatePath":template.path()}),
            &AtomicBool::new(false),
            &mut |_| {},
        )
        .unwrap();
        assert!(pdf.starts_with(b"%PDF-"));
    }
    let job: BackgroundJobSnapshot = serde_json::from_value(fixture.request(START_INVOICE_REPORT_PDF_ZIP_DOWNLOAD_JOB, &[], &[], Some(json!({"invoiceIds":[invoice.id],"templatePath":Builtin::Invoice.path(),"withSeal":true})))).unwrap();
    let finished =
        jobs::wait_for_completion(fixture.client(), job, &AtomicBool::new(false)).unwrap();
    let parameters = [("jobId", finished.job_id)];
    let archive = fixture
        .client()
        .bytes(DOWNLOAD_JOB_RESULT, &parameters, &[], None, jobs::PDF_LIMIT)
        .unwrap();
    assert!(archive.starts_with(b"PK"));
    fixture.request(CREATE_DATABASE_BACKUP, &[], &[], None);
    let backups = fixture.request(LIST_DATABASE_BACKUPS, &[], &[], None);
    let name = backups["backups"][0]["fileName"].as_str().unwrap();
    fixture.request(DELETE_JOB, &parameters, &[], None);
    fixture.request(
        RESTORE_DATABASE_BACKUP,
        &[],
        &[],
        Some(contracts::overlay(
            contracts::object(RESTORE_DATABASE_BACKUP.id, true),
            &json!({"backupFileName":name,"confirmationText":"RESTORE"}),
        )),
    );
    let restored = fixture.client().login("admin".into(), "".into()).unwrap().0;
    assert_eq!(
        restored
            .bytes(DOWNLOAD_JOB_RESULT, &parameters, &[], None, jobs::PDF_LIMIT)
            .unwrap(),
        archive
    );
    let image: Value = restored
        .json(
            PREVIEW_SHIPPING_MARK_IMAGE,
            &[],
            &[],
            Some(json!({"imagePath":shipping["imagePath"]})),
        )
        .unwrap();
    assert!(
        image["dataUrl"]
            .as_str()
            .unwrap()
            .starts_with("data:image/png;base64,")
    );
}

#[test]
fn template_publication_sharing_history_and_concurrency_are_not_generic_record_updates() {
    let fixture = Fixture::new();
    let content = template::export(&Design::invoice(), &field_catalog(&fixture.fields())).unwrap();
    let template = fixture.create(
        CREATE_USER_REPORT_TEMPLATE,
        json!({"reportType":"ExportDocument","name":"共享模板","contentHtml":content}),
    );
    assert_eq!(template["status"], "Draft");
    assert_eq!(template["canEdit"], true);
    assert_eq!(template["canPublish"], true);
    let parameters = [("id", template["id"].to_string())];
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                SHARE_USER_REPORT_TEMPLATE,
                &parameters,
                &[],
                Some(json!({"expectedVersion":1,"shareScope":"Company"}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let published = fixture.request(
        PUBLISH_USER_REPORT_TEMPLATE,
        &parameters,
        &[],
        Some(json!({"expectedVersion":1})),
    );
    let shared = fixture.request(
        SHARE_USER_REPORT_TEMPLATE,
        &parameters,
        &[],
        Some(json!({"expectedVersion":published["versionNumber"],"shareScope":"Company"})),
    );
    assert_eq!(shared["status"], "Published");
    assert_eq!(shared["shareScope"], "Company");
    let versions = fixture.request(LIST_USER_REPORT_TEMPLATE_VERSIONS, &parameters, &[], None);
    assert_eq!(versions["totalCount"], 3);
    assert_eq!(versions["items"][0]["versionNumber"], 3);
    let reader = fixture.user("report-reader");
    let visible: Value = reader
        .json(
            LIST_USER_REPORT_TEMPLATES,
            &[],
            &[("reportType", "ExportDocument".into())],
            None,
        )
        .unwrap();
    assert_eq!(visible["totalCount"], 1);
    assert_eq!(visible["items"][0]["canEdit"], false);
    assert_eq!(reader.json::<Value>(SAVE_USER_REPORT_TEMPLATE_DRAFT, &parameters, &[], Some(json!({"reportType":"ExportDocument","name":"夺取草稿","contentHtml":content,"expectedVersion":3}))).unwrap_err().status, Some(403));
    let restored = fixture.request(
        RESTORE_USER_REPORT_TEMPLATE_VERSION,
        &[
            ("id", template["id"].to_string()),
            ("versionNumber", "1".into()),
        ],
        &[],
        Some(json!({"expectedVersion":3})),
    );
    assert_eq!(restored["versionNumber"], 4);
    assert_eq!(restored["status"], "Draft");
    assert_eq!(restored["shareScope"], "Private");
    let hidden: Value = reader
        .json(
            LIST_USER_REPORT_TEMPLATES,
            &[],
            &[("reportType", "ExportDocument".into())],
            None,
        )
        .unwrap();
    assert_eq!(hidden["totalCount"], 0);
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                PUBLISH_USER_REPORT_TEMPLATE,
                &parameters,
                &[],
                Some(json!({"expectedVersion":3}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let archived = fixture.request(
        ARCHIVE_USER_REPORT_TEMPLATE,
        &parameters,
        &[("expectedVersion", "4".into())],
        None,
    );
    assert_eq!(archived["status"], "Archived");
    let restored = fixture.request(
        RESTORE_USER_REPORT_TEMPLATE,
        &parameters,
        &[],
        Some(json!({"expectedVersion":5})),
    );
    assert_eq!(restored["status"], "Draft");
    let fields = fixture.request(
        GET_REPORT_TEMPLATE_FIELD_CATALOG,
        &[],
        &[("reportType", "PaymentVoucher".into())],
        None,
    );
    assert_eq!(fields["reportType"], "PaymentVoucher");
    assert!(fields["fields"].as_array().unwrap().iter().any(|field| {
        field["value"]
            .as_str()
            .unwrap()
            .contains("Payment.CNYAmount")
    }));
}

#[test]
fn report_images_enforce_upload_ownership_and_references() {
    let fixture = Fixture::new();
    let uploader = fixture.user("image-owner");
    let stranger = fixture.user("image-reader");
    let image = uploader
        .upload(
            UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
            &[],
            json!({}),
            "图片.png",
            &Fixture::png(),
        )
        .unwrap();
    let parameters = [("resourceId", image["id"].as_str().unwrap().into())];
    assert_eq!(
        stranger
            .bytes(
                DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &parameters,
                &[],
                None,
                jobs::PDF_LIMIT
            )
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        uploader
            .upload(
                UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &[],
                json!({}),
                "图片.png",
                b"invalid image"
            )
            .unwrap_err()
            .status,
        Some(400)
    );
    let mut design = Design::invoice();
    design.resources.push(serde_json::from_value(json!({"id":image["id"],"mediaType":image["mediaType"],"byteLength":image["byteLength"],"sha256":image["sha256"],"altText":"验证图片"})).unwrap());
    let content = template::export(&design, &field_catalog(&fixture.fields())).unwrap();
    let saved: Value = uploader
        .json(
            CREATE_USER_REPORT_TEMPLATE,
            &[],
            &[],
            Some(
                json!({"reportType":"ExportDocument","name":"图片归属验证","contentHtml":content}),
            ),
        )
        .unwrap();
    let recycle = uploader
        .json::<Value>(
            RECYCLE_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
            &parameters,
            &[],
            None,
        )
        .unwrap_err();
    assert_eq!(recycle.status, Some(409));
    let entries: Value = uploader
        .json(QUERY_REPORT_TEMPLATE_V3_IMAGE_RESOURCES, &[], &[], None)
        .unwrap();
    assert_eq!(entries["items"][0]["isReferenced"], true);
    assert_eq!(entries["items"][0]["canRecycle"], false);
    assert!(saved["id"].as_i64().unwrap() > 0);
}

#[test]
fn payment_drafts_preview_without_persistence_and_save_with_a_real_identity() {
    let fixture = Fixture::new();
    let mut draft = export_doc_domain::payment::new("2026-09-16").unwrap();
    draft.voucher_no = "PAY-DRAFT-001".into();
    draft.notes = "当前未保存草稿".into();
    draft.spare7 = "保留备用字段".into();
    draft.payment_date = None;
    draft.cny_amount = "0.3000001".parse().unwrap();
    let preview_body = json!({"templatePath":Builtin::PaymentVoucher.path(),"payment":draft});
    let html = fixture.request(
        PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML,
        &[],
        &[],
        Some(preview_body.clone()),
    );
    assert!(html["html"].as_str().unwrap().contains("当前未保存草稿"));
    assert_eq!(html["paymentId"], 0);
    assert!(
        fixture
            .client()
            .preview_report_pdf(PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML, &[], &preview_body)
            .unwrap()
            .starts_with(b"%PDF-")
    );
    assert_eq!(
        fixture.request(LIST_PAYMENTS, &[], &[], None)["totalCount"],
        0
    );
    let saved = fixture.request(CREATE_PAYMENT, &[], &[], Some(json!(draft)));
    assert_eq!(saved["id"], saved["payment"]["id"]);
    assert!(saved["id"].as_i64().unwrap() > 0);
    assert_eq!(saved["payment"]["spare7"], draft.spare7);
    assert!(saved["payment"]["paymentDate"].is_null());
    assert!(saved["payment"].get("versionNumber").is_none());
    let mut updated = saved["payment"].clone();
    updated["notes"] = json!("更新后的付款备注");
    let parameters = [("id", saved["id"].to_string())];
    let persisted = fixture.request(UPDATE_PAYMENT, &parameters, &[], Some(updated.clone()));
    assert_ne!(
        persisted["payment"]["rowVersion"],
        saved["payment"]["rowVersion"]
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(UPDATE_PAYMENT, &parameters, &[], Some(updated))
            .unwrap_err()
            .status,
        Some(409)
    );
    let current = fixture.request(GET_PAYMENT, &parameters, &[], None);
    assert_eq!(current["notes"], "更新后的付款备注");
    let precise: ApiPaymentDto = serde_json::from_value(current.clone()).unwrap();
    assert_eq!(precise.cny_amount, draft.cny_amount);
    for (key, invalid) in [
        ("otherExpense", json!(-1)),
        ("receiptDate", json!("2101-01-01")),
        ("notes", json!("备".repeat(2001))),
        ("payeeId", json!(-1)),
    ] {
        let mut invalid_body = current.clone();
        invalid_body[key] = invalid;
        assert_eq!(
            fixture
                .client()
                .json::<Value>(UPDATE_PAYMENT, &parameters, &[], Some(invalid_body))
                .unwrap_err()
                .status,
            Some(400),
            "{key}"
        );
    }
    let reader = fixture.user("invoice-only-reader");
    assert_eq!(
        reader
            .preview_report_pdf(PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML, &[], &preview_body)
            .unwrap_err()
            .status,
        Some(403)
    );
}

#[test]
fn custom_payment_methods_are_typed_deduplicated_and_persistent() {
    let fixture = Fixture::new();
    let parameters = [("optionType", "PaymentMethod".into())];
    let initial = fixture.request(LIST_CUSTOM_OPTIONS, &parameters, &[], None);
    assert_eq!(
        initial["predefinedOptions"],
        json!(["支票", "电汇", "预付"])
    );
    fixture.request(
        SAVE_CUSTOM_OPTION,
        &parameters,
        &[],
        Some(json!({"value":"  SEPA  "})),
    );
    let duplicate = fixture.request(
        SAVE_CUSTOM_OPTION,
        &parameters,
        &[],
        Some(json!({"value":"sepa"})),
    );
    assert_eq!(duplicate["customOptions"], json!(["SEPA"]));
    assert_eq!(
        fixture.request(LIST_CUSTOM_OPTIONS, &parameters, &[], None)["options"],
        json!(["支票", "电汇", "预付", "SEPA"])
    );
    assert_eq!(
        fixture.request(
            LIST_CUSTOM_OPTIONS,
            &[("optionType", "PaymentPayerName".into())],
            &[],
            None
        )["options"],
        json!([])
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                SAVE_CUSTOM_OPTION,
                &[("optionType", "Type".into())],
                &[],
                Some(json!({"value":"自定义"}))
            )
            .unwrap_err()
            .status,
        Some(400)
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                LIST_CUSTOM_OPTIONS,
                &[("optionType", "Unknown".into())],
                &[],
                None
            )
            .unwrap_err()
            .status,
        Some(400)
    );
}
