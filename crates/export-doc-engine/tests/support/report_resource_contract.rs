use super::*;

fn resource(fixture: &Fixture) -> Value {
    fixture
        .client()
        .upload(
            UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
            &[],
            json!({}),
            "共享图片.png",
            &Fixture::png(),
        )
        .unwrap()
}

fn with_image(content: &str, image: &Value) -> String {
    let mut design: Value = serde_json::from_str(content).unwrap();
    design["resources"] = json!([{"id":image["id"],"mediaType":image["mediaType"],"byteLength":image["byteLength"],"sha256":image["sha256"],"altText":"共享图片"}]);
    design.to_string()
}

#[test]
fn file_template_resources_are_readable_and_cannot_be_recycled() {
    let fixture = Fixture::new();
    let image = resource(&fixture);
    let reader = fixture.user("file-image-reader");
    let file = fixture.create(
        CREATE_REPORT_TEMPLATE,
        json!({"reportType":"ExportDocument","displayName":"文件图片模板"}),
    );
    let saved = fixture.request(SAVE_REPORT_TEMPLATE_CONTENT, &[], &[], Some(json!({"reportType":"ExportDocument",
        "templatePath":file["templatePath"],"expectedRevision":file["revision"],"content":with_image(file["content"].as_str().unwrap(), &image)})));
    let parameters = [("resourceId", image["id"].as_str().unwrap().into())];
    assert_eq!(
        reader
            .bytes(
                DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &parameters,
                &[],
                None,
                jobs::PDF_LIMIT
            )
            .unwrap(),
        Fixture::png()
    );
    let entries: Value = reader
        .json(QUERY_REPORT_TEMPLATE_V3_IMAGE_RESOURCES, &[], &[], None)
        .unwrap();
    assert_eq!(entries["items"][0]["isReferenced"], true);
    assert_eq!(entries["items"][0]["canRecycle"], false);
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                RECYCLE_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &parameters,
                &[],
                None
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let clone: Value = reader.json(CLONE_USER_REPORT_TEMPLATE, &[], &[], Some(json!({"reportType":"ExportDocument","name":"图片副本","sourceTemplatePath":saved["templatePath"]}))).unwrap();
    assert_eq!(clone["status"], "Draft");
    fixture.request(
        DELETE_REPORT_TEMPLATE,
        &[],
        &[
            ("reportType", "ExportDocument".into()),
            (
                "templatePath",
                saved["templatePath"].as_str().unwrap().into(),
            ),
            (
                "expectedRevision",
                saved["revision"].as_str().unwrap().into(),
            ),
        ],
        None,
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                RECYCLE_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &parameters,
                &[],
                None
            )
            .unwrap_err()
            .status,
        Some(409)
    );
}

#[test]
fn shared_template_does_not_bypass_its_source_domain_permission_for_images() {
    let fixture = Fixture::new();
    let image = resource(&fixture);
    let reader = fixture.user("no-payment-image-view");
    let draft = fixture.create(
        CREATE_USER_REPORT_TEMPLATE,
        json!({"reportType":"PaymentVoucher","name":"付款图片模板","contentHtml":""}),
    );
    let parameters = [("id", draft["id"].to_string())];
    let saved = fixture.request(SAVE_USER_REPORT_TEMPLATE_DRAFT, &parameters, &[], Some(json!({"reportType":"PaymentVoucher","name":"付款图片模板","expectedVersion":draft["versionNumber"],"contentHtml":with_image(draft["contentHtml"].as_str().unwrap(), &image)})));
    let published = fixture.request(
        PUBLISH_USER_REPORT_TEMPLATE,
        &parameters,
        &[],
        Some(json!({"expectedVersion":saved["versionNumber"]})),
    );
    fixture.request(
        SHARE_USER_REPORT_TEMPLATE,
        &parameters,
        &[],
        Some(json!({"expectedVersion":published["versionNumber"],"shareScope":"Company"})),
    );
    assert_eq!(
        reader
            .bytes(
                DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &[("resourceId", image["id"].as_str().unwrap().into())],
                &[],
                None,
                jobs::PDF_LIMIT
            )
            .unwrap_err()
            .status,
        Some(403)
    );
}

#[test]
fn resource_id_in_invoice_text_does_not_grant_image_access() {
    let fixture = Fixture::new();
    let image = resource(&fixture);
    let reader = fixture.user("image-text-reference");
    let mut draft = InvoiceDraft::demo("2026-09-23", "IMAGE-TEXT-REFERENCE");
    draft.header.spare1 = image["id"].as_str().unwrap().into();
    reader.save_invoice(&draft.build().unwrap()).unwrap();
    assert_eq!(
        reader
            .bytes(
                DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
                &[("resourceId", image["id"].as_str().unwrap().into())],
                &[],
                None,
                jobs::PDF_LIMIT
            )
            .unwrap_err()
            .status,
        Some(403)
    );
}
