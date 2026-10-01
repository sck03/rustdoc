use super::*;

#[test]
fn saved_private_template_prints_without_publish_or_share_permission() {
    let fixture = Fixture::new();
    let owner = fixture.user("private-designer");
    let other = fixture.user("private-reader");
    let draft: Value = owner.json(CLONE_USER_REPORT_TEMPLATE, &[], &[], Some(json!({"reportType":"ExportDocument","name":"仅自己使用","sourceTemplatePath":Builtin::Invoice.path()}))).unwrap();
    assert_eq!(draft["status"], "Draft");
    assert_eq!(draft["shareScope"], "Private");
    assert_eq!(draft["canPublish"], false);
    let template_path = format!("user-template:{}", draft["id"]);
    let parameters = [("id", draft["id"].to_string())];
    let saved: Value = owner.json(SAVE_USER_REPORT_TEMPLATE_DRAFT, &parameters, &[], Some(json!({"reportType":"ExportDocument","name":"私人发票样式","contentHtml":draft["contentHtml"],"expectedVersion":draft["versionNumber"]}))).unwrap();
    assert_eq!(saved["status"], "Draft");
    for (client, expected) in [(&owner, true), (&other, false), (fixture.client(), false)] {
        let catalog: Vec<Value> = client
            .json(
                LIST_REPORT_TEMPLATES,
                &[],
                &[("reportType", "ExportDocument".into())],
                None,
            )
            .unwrap();
        assert_eq!(
            catalog
                .iter()
                .any(|row| row["templatePath"] == template_path),
            expected
        );
    }
    let invoice = owner
        .save_invoice(
            &InvoiceDraft::demo("2026-10-01", "PRIVATE-OUTPUT")
                .build()
                .unwrap(),
        )
        .unwrap();
    let parameters = [("invoiceId", invoice.id.to_string())];
    let body = json!({"templatePath":template_path,"withSeal":false});
    let preview: Value = owner
        .json(
            PREVIEW_INVOICE_REPORT_HTML,
            &parameters,
            &[],
            Some(body.clone()),
        )
        .unwrap();
    assert!(preview["html"].as_str().unwrap().contains("svg"));
    let pdf = jobs::render_report_pdf(
        &owner,
        START_INVOICE_REPORT_PDF_DOWNLOAD_JOB,
        &parameters,
        body,
        &AtomicBool::new(false),
        &mut |_| {},
    )
    .unwrap();
    assert!(pdf.starts_with(b"%PDF-"));
    let unchanged: Value = owner
        .json(
            GET_USER_REPORT_TEMPLATE,
            &[("id", draft["id"].to_string())],
            &[],
            None,
        )
        .unwrap();
    assert_eq!(unchanged["status"], "Draft");
    assert_eq!(unchanged["shareScope"], "Private");
    assert_eq!(unchanged["versionNumber"], saved["versionNumber"]);
}
