use super::*;

#[test]
fn unlinked_invoice_seals_persist_render_and_reject_foreign_references() {
    let fixture = Fixture::new();
    let saved = fixture.create(
        SAVE_INVOICE_SEAL_IMAGE,
        json!({"imageDataUrl":format!("data:image/png;base64,{}",STANDARD.encode(Fixture::png()))}),
    );
    let path = saved["imagePath"].as_str().unwrap();
    let mut draft = InvoiceDraft::demo("2026-09-28", "INVOICE-SEALS");
    draft.header.doc_seal_path = Some(path.into());
    draft.header.customs_seal_path = Some(path.into());
    let invoice = fixture
        .client()
        .save_invoice(&draft.build().unwrap())
        .unwrap();
    assert_eq!(invoice.exporter_id, 0);
    let loaded: ApiInvoiceDetailDto = fixture
        .client()
        .json(GET_INVOICE, &[("id", invoice.id.to_string())], &[], None)
        .unwrap();
    assert_eq!(loaded.doc_seal_path.as_deref(), Some(path));
    assert_eq!(loaded.customs_seal_path.as_deref(), Some(path));
    #[cfg(feature = "invoice-transfer")]
    {
        let bytes = fixture
            .client()
            .bytes(
                DOWNLOAD_INVOICE_TRANSFER_PACKAGE,
                &[("id", invoice.id.to_string())],
                &[],
                None,
                25 * 1024 * 1024,
            )
            .unwrap();
        let target = Fixture::new();
        let imported: Value = target
            .client()
            .upload(
                IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
                &[],
                json!({"conflictAction":"Skip"}),
                "invoice.edpkg",
                &bytes,
            )
            .unwrap();
        let imported = target.request(
            GET_INVOICE,
            &[("id", imported["result"]["invoiceId"].to_string())],
            &[],
            None,
        );
        assert_eq!(imported["docSealPath"], path);
        assert_eq!(imported["customsSealPath"], path);
    }
    for template in [Builtin::Invoice, Builtin::CustomsDeclaration] {
        let preview = fixture.request(
            PREVIEW_INVOICE_REPORT_HTML,
            &[("invoiceId", invoice.id.to_string())],
            &[],
            Some(json!({"templatePath":template.path(),"withSeal":true})),
        );
        assert_eq!(
            preview["html"]
                .as_str()
                .unwrap()
                .matches("data:image/png;base64,")
                .count(),
            1
        );
        let preview = fixture.request(
            PREVIEW_INVOICE_REPORT_HTML,
            &[("invoiceId", invoice.id.to_string())],
            &[],
            Some(json!({"templatePath":template.path(),"withSeal":false})),
        );
        assert!(
            !preview["html"]
                .as_str()
                .unwrap()
                .contains("data:image/png;base64,")
        );
    }
    let reader = fixture.user("seal-outsider");
    let mut foreign = draft.build().unwrap();
    foreign.invoice_no = "FOREIGN-SEAL".into();
    assert_eq!(reader.save_invoice(&foreign).unwrap_err().status, Some(403));
    let id = path.strip_prefix("Files/Seals/").unwrap();
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                RECYCLE_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &[("resourceId", id.into())],
                &[],
                None
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let mut invalid = draft.build().unwrap();
    invalid.doc_seal_path = Some("C:/outside.png".into());
    assert_eq!(
        fixture.client().save_invoice(&invalid).unwrap_err().status,
        Some(400)
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                SAVE_INVOICE_SEAL_IMAGE,
                &[],
                &[],
                Some(json!({"imageDataUrl":"data:image/png;base64,YmFk"}))
            )
            .unwrap_err()
            .status,
        Some(400)
    );
}

#[test]
fn inherited_seals_can_be_overridden_or_cleared_without_changing_exporter() {
    let fixture = Fixture::new();
    let exporter = fixture.create(CREATE_EXPORTER, json!({"exporterNameEN":"SEAL ARCHIVE"}));
    let sealed: Value = fixture
        .client()
        .upload(
            UPLOAD_EXPORTER_SEAL,
            &[
                ("id", exporter["id"].to_string()),
                ("sealType", "document".into()),
            ],
            json!({}),
            "seal.png",
            &Fixture::png(),
        )
        .unwrap();
    let mut draft = InvoiceDraft::demo("2026-09-28", "INHERITED-SEAL");
    draft.header.exporter_id = exporter["id"].as_i64().unwrap();
    for (seal, count) in [
        (None, 1),
        (Some(String::new()), 0),
        (sealed["docSealPath"].as_str().map(str::to_owned), 1),
    ] {
        draft.header.doc_seal_path = seal;
        let preview = fixture.request(PREVIEW_INVOICE_REPORT_DRAFT_HTML, &[], &[], Some(json!({"invoice":draft.build().unwrap(),"templatePath":Builtin::Invoice.path(),"withSeal":true})));
        assert_eq!(
            preview["html"]
                .as_str()
                .unwrap()
                .matches("data:image/png;base64,")
                .count(),
            count
        );
    }
    let current = fixture.request(
        GET_EXPORTER,
        &[("id", exporter["id"].to_string())],
        &[],
        None,
    );
    assert_eq!(current["docSealPath"], sealed["docSealPath"]);
}

#[test]
fn every_default_previews_samples_and_saved_documents_through_the_current_design() {
    let fixture = Fixture::new();
    let invoice = fixture
        .client()
        .save_invoice(
            &InvoiceDraft::demo("2026-09-28", "SAVED-PREVIEW")
                .build()
                .unwrap(),
        )
        .unwrap();
    let payment = fixture.create(
        CREATE_PAYMENT,
        serde_json::to_value(export_doc_domain::payment::new("2026-09-28").unwrap()).unwrap(),
    );
    for template in export_doc_report::BUILTINS {
        let mut design = template.design().unwrap();
        let original = serde_json::to_string(&design).unwrap();
        let title = design.element_mut("title").unwrap();
        title.kind = export_doc_domain::designer::Kind::Text {
            text: "DRAFT TITLE".into(),
        };
        let content = serde_json::to_string(&design).unwrap();
        let payment_type = template.report_type() == "PaymentVoucher";
        let profiles = if payment_type {
            vec!["apiSample", "paymentVoucher"]
        } else {
            vec![
                "apiSample",
                "exportStandard",
                "exportImageMarks",
                "exportLongItems",
            ]
        };
        for sample in profiles {
            let preview = fixture.create(PREVIEW_REPORT_TEMPLATE_CONTENT, json!({"reportType":template.report_type(),"content":content,"sampleProfile":sample}));
            let html = preview["html"].as_str().unwrap();
            assert!(
                html.contains("<svg") && html.contains("DRAFT TITLE"),
                "{} {sample}",
                template.label()
            );
            assert!(!html.contains("{{"));
            assert!(
                !html.contains("<text"),
                "preview must not depend on client fonts"
            );
            assert!(html.contains("<path") && html.contains("<desc>"));
            assert!(
                html.contains("viewBox=\"0 0"),
                "outlined pages must scale with the preview viewport"
            );
            if sample == "exportLongItems" {
                assert!(html.matches("<svg").count() > 1);
            }
        }
        let (operation, parameters) = if payment_type {
            (
                PREVIEW_PAYMENT_VOUCHER_HTML,
                vec![("paymentId", payment["id"].to_string())],
            )
        } else {
            (
                PREVIEW_INVOICE_REPORT_HTML,
                vec![("invoiceId", invoice.id.to_string())],
            )
        };
        let preview = fixture.request(operation, &parameters, &[], Some(json!({"reportType":template.report_type(),"templatePath":template.path(),"content":content})));
        assert!(preview["html"].as_str().unwrap().contains("DRAFT TITLE"));
        let stored = fixture.request(
            GET_REPORT_TEMPLATE_CONTENT,
            &[],
            &[
                ("reportType", template.report_type().into()),
                ("templatePath", template.path().into()),
            ],
            None,
        );
        assert_eq!(
            serde_json::from_str::<Value>(stored["content"].as_str().unwrap()).unwrap(),
            serde_json::from_str::<Value>(&original).unwrap()
        );
    }
}
