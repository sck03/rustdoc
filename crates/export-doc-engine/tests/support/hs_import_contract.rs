use export_doc_engine::{api::ApiError, engine::NativeService, generated_api::*};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, token: &str, day: &str) {
    let year: i32 = day[..4].parse().unwrap();
    let csv = "HS编码,商品名称,退税率,申报要素,备注\n9900987653,原名称,13%,原要素,原备注\n";
    let preview = |csv: &str| {
        service
            .upload(
                PREVIEW_HS_CODES_IMPORT_UPLOAD,
                &[],
                json!({"mode":"Incremental","sourceName":"字段保留回归","effectiveYear":year}),
                "tariff.csv",
                csv.as_bytes(),
                token,
            )
            .unwrap()
    };
    let call =
        |op, path: &[(&str, String)], query: &[(&str, String)], body| -> Result<Value, ApiError> {
            serde_json::from_slice(&service.dispatch(op, path, query, body, token)?).map_err(|e| {
                ApiError {
                    status: Some(500),
                    message: e.to_string(),
                    code: None,
                    diagnostic: None,
                }
            })
        };
    let commit = |p: &Value| {
        call(
            COMMIT_HS_CODES_IMPORT,
            &[],
            &[],
            Some(json!({"token":p["token"]})),
        )
    };
    let first = preview(csv);
    assert!(
        first.get("items").is_none(),
        "preview creation must return only the summary"
    );
    commit(&first).unwrap();
    let update = preview("HS编码,商品名称\n9900987653,新名称\n");
    let path = [("token", update["token"].as_str().unwrap().into())];
    let page = call(
        LIST_HS_CODE_IMPORT_PREVIEW_ITEMS,
        &path,
        &[("changeType", "Update".into()), ("pageSize", "1".into())],
        None,
    )
    .unwrap();
    export_doc_contracts::validation::response(LIST_HS_CODE_IMPORT_PREVIEW_ITEMS.id, &page)
        .unwrap();
    assert_eq!(page["totalCount"], 1);
    assert_eq!(page["items"][0]["item"]["notes"], "原备注");
    assert_eq!(page["items"][0]["item"]["rebateRate"], "13%");
    assert_eq!(page["items"][0]["item"]["elements"], "原要素");
    assert_eq!(
        page["items"][0]["changes"],
        json!([{"field":"name","before":"原名称","after":"新名称"}])
    );
    assert_eq!(
        call(
            LIST_HS_CODE_IMPORT_PREVIEW_ITEMS,
            &path,
            &[("changeType", "Add".into())],
            None
        )
        .unwrap()["totalCount"],
        0
    );
    commit(&update).unwrap();
    assert_eq!(
        call(LIST_HS_CODE_IMPORT_PREVIEW_ITEMS, &path, &[], None)
            .unwrap_err()
            .status,
        Some(409)
    );
    let clear = preview("HS编码,商品名称,备注\n9900987653,新名称,\n");
    commit(&clear).unwrap();
    let saved = call(GET_HS_CODE, &[("code", "9900987653".into())], &[], None).unwrap();
    assert_eq!(saved["notes"], "");
    assert_eq!(saved["rebateRate"], "13%");
    assert_eq!(saved["elements"], "原要素");
}
