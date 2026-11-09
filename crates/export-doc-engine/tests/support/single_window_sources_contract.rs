use export_doc_engine::{api::ApiError, contracts, generated_api::*, invoice::InvoiceDraft};
use serde_json::{Value, json};

pub fn exercise(
    request: &dyn Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let call = |op: Operation, id: i64, body: Option<Value>| {
        let path = if id > 0 {
            vec![("sourceId", id.to_string())]
        } else {
            vec![]
        };
        let value = request(op, &path, &[], body).unwrap();
        export_doc_contracts::validation::response(op.id, &value).unwrap();
        value
    };
    let source = call(
        CREATE_SINGLE_WINDOW_SOURCE,
        0,
        Some(json!({"referenceNo":"CUSTOMER-EXCEL-001"})),
    );
    let id = source["id"].as_i64().unwrap();
    assert_eq!(source["sourceKind"], "Manual");
    assert!(request(GET_INVOICE, &[("id", id.to_string())], &[], None).is_err());
    let mut coo = call(GET_CUSTOMS_COO_DOCUMENT, id, None);
    coo["consignee"] = json!("DESTINATION RECEIVER");
    coo["contractNo"] = json!("COO-CONTRACT");
    let mut item = contracts::initial(contracts::schema("ApiCustomsCooItemDto"));
    item["gNo"] = json!(1);
    item["goodsNameE"] = json!("DETAILED CUSTOMER GOODS DESCRIPTION");
    coo["items"] = json!([item.clone()]);
    call(SAVE_CUSTOMS_COO_DOCUMENT, id, Some(coo));
    for _ in 0..2 {
        let mut loaded = call(GET_CUSTOMS_COO_DOCUMENT, id, None);
        assert_eq!(loaded["consignee"], "DESTINATION RECEIVER");
        assert_eq!(loaded["contractNo"], "COO-CONTRACT");
        assert_eq!(loaded["items"].as_array().unwrap().len(), 1);
        assert_eq!(loaded["items"][0]["goodsNameE"], item["goodsNameE"]);
        loaded["items"][0]["goodsDesc"] = json!("MORE DETAIL");
        call(SAVE_CUSTOMS_COO_DOCUMENT, id, Some(loaded));
    }
    let mut acd = call(GET_AGENT_CONSIGNMENT_DOCUMENT, id, None);
    acd["gName"] = json!("CUSTOMS GOODS");
    call(SAVE_AGENT_CONSIGNMENT_DOCUMENT, id, Some(acd));
    assert_eq!(
        call(GET_CUSTOMS_COO_DOCUMENT, id, None)["consignee"],
        "DESTINATION RECEIVER"
    );
    let list = call(LIST_SINGLE_WINDOW_SOURCES, 0, None);
    assert_eq!(list["totalCount"], 1);
    assert_eq!(list["items"][0]["id"], id);
    let imported = call(
        CREATE_SINGLE_WINDOW_SOURCE,
        0,
        Some(json!({
            "referenceNo":"CUSTOMER-EXCEL-002", "invoice":InvoiceDraft::demo("2026-11-09", "CUSTOMER").build().unwrap()
        })),
    );
    assert_eq!(imported["sourceKind"], "Excel");
    let imported_id = imported["id"].as_i64().unwrap();
    let mut draft = call(GET_CUSTOMS_COO_DOCUMENT, imported_id, None);
    let original = draft["items"].as_array().unwrap().len();
    assert!(original > 1);
    draft["items"].as_array_mut().unwrap().remove(0);
    for (index, row) in draft["items"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .enumerate()
    {
        row["gNo"] = json!(index + 1);
    }
    call(SAVE_CUSTOMS_COO_DOCUMENT, imported_id, Some(draft));
    assert_eq!(
        call(GET_CUSTOMS_COO_DOCUMENT, imported_id, None)["items"]
            .as_array()
            .unwrap()
            .len(),
        original - 1
    );
}
