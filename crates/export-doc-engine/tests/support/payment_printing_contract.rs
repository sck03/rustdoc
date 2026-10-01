use super::*;

pub(super) fn exercise(service: &NativeService, admin: &str, owner: &Value, other: &Value) {
    let invoke = |token: &str,
                  op: Operation,
                  parameters: &[(&str, String)],
                  query: &[(&str, String)],
                  body: Option<Value>| {
        let body = body.map(|value| contracts::overlay(contracts::object(op.id, true), &value));
        service
            .dispatch(op, parameters, query, body, token)
            .map(|bytes| serde_json::from_slice::<Value>(&bytes).unwrap())
    };
    let employee = token(owner);
    for op in [
        GET_REPORT_TEMPLATE_CONTENT,
        LIST_USER_REPORT_TEMPLATES,
        GET_REPORT_TEMPLATE_FIELD_CATALOG,
    ] {
        assert_eq!(
            invoke(
                employee,
                op,
                &[],
                &[("reportType", "ExportDocument".into())],
                None
            )
            .unwrap_err()
            .status,
            Some(403)
        );
    }
    assert_eq!(
        invoke(
            employee,
            LIST_REPORT_TEMPLATES,
            &[],
            &[("reportType", "ExportDocument".into())],
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let catalog = invoke(
        employee,
        LIST_REPORT_TEMPLATES,
        &[],
        &[("reportType", "PaymentVoucher".into())],
        None,
    )
    .unwrap();
    assert!(!catalog.as_array().unwrap().is_empty());
    assert!(
        catalog
            .as_array()
            .unwrap()
            .iter()
            .all(|row| row["reportType"] == "PaymentVoucher")
    );
    let created = call(
        service,
        employee,
        CREATE_PAYMENT,
        0,
        Some(json!({
            "voucherNo":format!("PAY-{}",nonce().unwrap()),"payeeName":"本人报销打印验收",
        "paymentDate":"2026-10-02","cnyAmount":123.45
        })),
    )
    .unwrap()["payment"]
        .clone();
    let id = created["id"].as_i64().unwrap();
    assert_eq!(created["ownerUserId"], owner["user"]["id"]);
    for op in [GET_PAYMENT, UPDATE_PAYMENT, DELETE_PAYMENT] {
        assert_eq!(
            call(
                service,
                token(other),
                op,
                id,
                if op == UPDATE_PAYMENT {
                    Some(created.clone())
                } else {
                    None
                }
            )
            .unwrap_err()
            .status,
            Some(403)
        );
    }
    let parameters = [("paymentId", id.to_string())];
    assert_eq!(
        invoke(
            token(other),
            PREVIEW_PAYMENT_VOUCHER_HTML,
            &parameters,
            &[],
            Some(json!({}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        invoke(
            token(other),
            START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB,
            &parameters,
            &[],
            Some(json!({}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    // A shared template can be used without permission to read/edit its source.
    let draft = call(service, admin, CREATE_USER_REPORT_TEMPLATE, 0, Some(json!({"reportType":"PaymentVoucher","name":format!("共享付款-{}",nonce().unwrap()),"contentHtml":""}))).unwrap();
    let template_id = draft["id"].as_i64().unwrap();
    let published = call(
        service,
        admin,
        PUBLISH_USER_REPORT_TEMPLATE,
        template_id,
        Some(json!({"expectedVersion":draft["versionNumber"]})),
    )
    .unwrap();
    let path = format!("user-template:{template_id}");
    let includes = || {
        invoke(
            employee,
            LIST_REPORT_TEMPLATES,
            &[],
            &[("reportType", "PaymentVoucher".into())],
            None,
        )
        .unwrap()
        .as_array()
        .unwrap()
        .iter()
        .any(|row| row["templatePath"] == path)
    };
    assert!(!includes());
    let shared = call(
        service,
        admin,
        SHARE_USER_REPORT_TEMPLATE,
        template_id,
        Some(json!({"expectedVersion":published["versionNumber"],"shareScope":"Company"})),
    )
    .unwrap();
    assert!(includes());
    call(
        service,
        admin,
        DISABLE_USER_REPORT_TEMPLATE,
        template_id,
        Some(json!({"expectedVersion":shared["versionNumber"]})),
    )
    .unwrap();
    assert!(!includes());
}
