use export_doc_engine::{api::ApiError, contracts, generated_api::*};
use serde_json::{Value, json};

pub fn exercise(
    call: &impl Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let input = |name| {
        contracts::overlay(
            contracts::object(SAVE_HS_CODE_KNOWLEDGE_EXAMPLE.id, true),
            &json!({"id":0,"expectedVersion":0,"rawReportedHsCode":"6109100000","productName":name,"specification":"棉","source":"ConcurrencyContract"}),
        )
    };
    let original = input("并发案例甲");
    let first = call(
        SAVE_HS_CODE_KNOWLEDGE_EXAMPLE,
        &[],
        &[],
        Some(original.clone()),
    )
    .unwrap();
    assert_eq!(first["versionNumber"], 1);
    assert_eq!(
        call(
            SAVE_HS_CODE_KNOWLEDGE_EXAMPLE,
            &[],
            &[],
            Some(original.clone())
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let second = call(
        SAVE_HS_CODE_KNOWLEDGE_EXAMPLE,
        &[],
        &[],
        Some(input("并发案例乙")),
    )
    .unwrap();
    let mut edit = original;
    edit["id"] = first["id"].clone();
    edit["expectedVersion"] = first["versionNumber"].clone();
    edit["source"] = json!("Updated");
    let saved = call(SAVE_HS_CODE_KNOWLEDGE_EXAMPLE, &[], &[], Some(edit.clone())).unwrap();
    assert_eq!(saved["versionNumber"], 2);
    edit["productName"] = json!("旧页面覆盖");
    assert_eq!(
        call(SAVE_HS_CODE_KNOWLEDGE_EXAMPLE, &[], &[], Some(edit))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        call(
            DELETE_HS_CODE_KNOWLEDGE_EXAMPLE,
            &[("id", first["id"].to_string())],
            &[("expectedVersion", "1".into())],
            None
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let stale = json!({"items":[{"id":second["id"],"expectedVersion":1},{"id":first["id"],"expectedVersion":1}]});
    assert_eq!(
        call(
            DELETE_HS_CODE_KNOWLEDGE_EXAMPLES_BATCH,
            &[],
            &[],
            Some(stale)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let page = call(
        LIST_HS_CODE_KNOWLEDGE_EXAMPLES,
        &[],
        &[("keyword", "并发案例".into())],
        None,
    )
    .unwrap();
    assert_eq!(
        page["totalCount"], 2,
        "stale batch must roll back every deletion"
    );
    export_doc_contracts::validation::response(LIST_HS_CODE_KNOWLEDGE_EXAMPLES.id, &page).unwrap();
    call(
        DELETE_HS_CODE_KNOWLEDGE_EXAMPLES_BATCH,
        &[],
        &[],
        Some(json!({"items":[
            {"id":first["id"],"expectedVersion":2},{"id":second["id"],"expectedVersion":1}
        ]})),
    )
    .unwrap();
    let mut invoice = serde_json::to_value(
        export_doc_engine::invoice::InvoiceDraft::demo("2026-11-09", "HS-HISTORY-VARIANTS")
            .build()
            .unwrap(),
    )
    .unwrap();
    let items = invoice["items"].as_array_mut().unwrap();
    let count = items.len();
    for (index, item) in items.iter_mut().enumerate() {
        item["styleNo"] = json!(format!("HS-VARIANT-{index}"));
        item["styleNameCN"] = json!("历史款式聚合回归");
        item["hsCode"] = json!("6109100000");
        for field in [
            "material",
            "specification",
            "composition",
            "declarationElements",
        ] {
            item[field] = json!("");
        }
    }
    call(CREATE_INVOICE, &[], &[], Some(invoice)).unwrap();
    let history = call(
        DISCOVER_HS_CODE_HISTORY_CANDIDATES,
        &[],
        &[("keyword", "历史款式聚合回归".into())],
        None,
    )
    .unwrap();
    assert_eq!(history["totalCount"], 1);
    assert_eq!(history["items"][0]["sourceCount"], count);
    assert_eq!(history["items"][0]["variantCount"], count);
    assert_eq!(
        history["items"][0]["variantSamples"]
            .as_array()
            .unwrap()
            .len(),
        count.min(8)
    );
}
