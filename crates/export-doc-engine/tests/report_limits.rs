#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod fixture;
use export_doc_engine::generated_api::*;
use serde_json::{Value, json};

#[test]
fn invalid_direct_api_templates_do_not_create_records() {
    let f = fixture::Fixture::new();
    let saved = f.request(
        CREATE_USER_REPORT_TEMPLATE,
        None,
        Some(json!({"reportType":"PaymentVoucher","name":"结构边界","contentHtml":""})),
    );
    let source: Value = serde_json::from_str(saved["contentHtml"].as_str().unwrap()).unwrap();
    let mut cases = vec![];
    let mut value = source.clone();
    value["layers"] = json!([]);
    cases.push(value);
    let mut value = source.clone();
    value["layers"]
        .as_array_mut()
        .unwrap()
        .retain(|layer| layer["role"] != "Body");
    cases.push(value);
    let mut value = source.clone();
    value["layers"][1]["id"] = value["layers"][0]["id"].clone();
    cases.push(value);
    let mut value = source.clone();
    value["resources"] = json!([{"id":format!("img-{}.webp","a".repeat(64)),"mediaType":"image/webp","sha256":"a".repeat(64),"byteLength":8}]);
    cases.push(value);
    for (index, value) in cases.into_iter().enumerate() {
        let result: Result<Value, _> = f.client().json(CREATE_USER_REPORT_TEMPLATE, &[], &[], Some(json!({"reportType":"PaymentVoucher","name":format!("非法-{index}"),"contentHtml":value.to_string()})));
        assert_eq!(result.unwrap_err().status, Some(400));
    }
    let page: Value = f
        .client()
        .json(
            LIST_USER_REPORT_TEMPLATES,
            &[],
            &[("reportType", "PaymentVoucher".into())],
            None,
        )
        .unwrap();
    assert_eq!(page["totalCount"], 1);
}
