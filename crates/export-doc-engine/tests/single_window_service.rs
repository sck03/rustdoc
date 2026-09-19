#![cfg(feature = "single-window")]
#[path = "support/native_fixture.rs"]
mod native_fixture;
use export_doc_engine::{contracts, generated_api::*, invoice::InvoiceDraft};
use native_fixture::Fixture;
use serde_json::{Value, json};

fn call(f: &Fixture, op: Operation, id: i64, body: Option<Value>) -> Value {
    let path = if id > 0 {
        vec![(
            if op == GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL {
                "batchId"
            } else {
                "invoiceId"
            },
            id.to_string(),
        )]
    } else {
        vec![]
    };
    let value: Value = f
        .client()
        .json(op, &path, &[], body)
        .unwrap_or_else(|e| panic!("{}: {e}", op.id));
    export_doc_contracts::validation::response(op.id, &value)
        .unwrap_or_else(|e| panic!("{} response: {e}\n{value}", op.id));
    assert!(!value.to_string().contains("_assignmentSecret"));
    assert!(!value.to_string().contains("_locks"));
    value
}
fn invoice(f: &Fixture, name: &str) -> Value {
    f.create(
        CREATE_INVOICE,
        json!(InvoiceDraft::demo("2026-09-17", name).build().unwrap()),
    )
}
fn profile(f: &Fixture) -> Value {
    let body = contracts::overlay(
        contracts::object(SAVE_SINGLE_WINDOW_CLIENT_PROFILE.id, true),
        &json!({"profileName":"测试持卡机","companyScope":"DEFAULT","cardIdentifier":"CARD-TEST","canSubmitCustomsCoo":true,"canSubmitAgentConsignment":true}),
    );
    let value = call(f, SAVE_SINGLE_WINDOW_CLIENT_PROFILE, 0, Some(body));
    assert_eq!(value["profiles"].as_array().unwrap().len(), 1);
    assert!(value["profiles"][0].get("_secret").is_none());
    value["profiles"][0].clone()
}
fn acd(f: &Fixture, id: i64) -> Value {
    let mut draft = call(f, GET_AGENT_CONSIGNMENT_DOCUMENT, id, None);
    for (key, value) in [
        ("copCusCode", "1234567890"),
        ("operType", "1"),
        ("gName", "T SHIRTS"),
        ("codeTS", "6109100000"),
        ("declTotal", "100.00"),
        ("ieDate", "20260917"),
        ("tradeMode", "0110"),
        ("oriCountry", "142"),
        ("tradeCode", "1234567890"),
        ("agentCode", "0987654321"),
        ("curr", "502"),
        ("qtyOrWeight", "10"),
        ("packingCondition", "CARTONS"),
        ("consignTele", "12345678901"),
    ] {
        draft[key] = json!(value);
    }
    call(f, SAVE_AGENT_CONSIGNMENT_DOCUMENT, id, Some(draft))["document"].clone()
}

#[test]
fn declaration_revision_locks_and_stable_source_items_survive_reorder() {
    let f = Fixture::new();
    let created = invoice(&f, "NATIVE-SW-LOCKS");
    let id = created["id"].as_i64().unwrap();
    let mut source: Value = f
        .client()
        .json(GET_INVOICE, &[("id", id.to_string())], &[], None)
        .unwrap();
    let mut coo = call(&f, GET_CUSTOMS_COO_DOCUMENT, id, None);
    assert!(!coo["items"].as_array().unwrap().is_empty());
    let item_id = coo["items"][0]["sourceItemId"].clone();
    coo["items"][0]["goodsNameE"] = json!("MANUALLY LOCKED NAME");
    let saved = call(&f, SAVE_CUSTOMS_COO_DOCUMENT, id, Some(coo.clone()));
    assert_eq!(saved["document"]["draftRevision"], 1);
    assert_eq!(
        f.client()
            .json::<Value>(
                SAVE_CUSTOMS_COO_DOCUMENT,
                &[("invoiceId", id.to_string())],
                &[],
                Some(coo)
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let locks = call(&f, GET_CUSTOMS_COO_LOCKED_FIELDS, id, None);
    assert!(
        locks["fields"]
            .as_array()
            .unwrap()
            .iter()
            .any(|l| l["key"].as_str().unwrap().ends_with(":GoodsNameE"))
    );
    source["items"].as_array_mut().unwrap().reverse();
    let mut added = source["items"][0].clone();
    added["id"] = json!(0);
    added["styleNo"] = json!("ADDED-ITEM");
    source["items"].as_array_mut().unwrap().push(added);
    f.client()
        .json::<Value>(UPDATE_INVOICE, &[("id", id.to_string())], &[], Some(source))
        .unwrap();
    let current = call(&f, GET_CUSTOMS_COO_DOCUMENT, id, None);
    let rows = current["items"].as_array().unwrap();
    assert_eq!(
        rows.iter()
            .find(|row| row["sourceItemId"] == item_id)
            .unwrap()["goodsNameE"],
        "MANUALLY LOCKED NAME"
    );
    assert_eq!(
        rows.iter()
            .filter(|row| row["goodsNameE"] == "MANUALLY LOCKED NAME")
            .count(),
        1
    );
    let key = locks["fields"]
        .as_array()
        .unwrap()
        .iter()
        .find(|l| l["key"].as_str().unwrap().ends_with(":GoodsNameE"))
        .unwrap()["key"]
        .clone();
    let unlocked = call(
        &f,
        UNLOCK_CUSTOMS_COO_FIELDS,
        id,
        Some(json!({"fieldKeys":[key]})),
    );
    assert_eq!(unlocked["changedCount"], 1);
    assert!(
        unlocked["document"]["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|row| row["goodsNameE"] != "MANUALLY LOCKED NAME")
    );
}

#[test]
fn submit_dispatch_receipt_roundtrip_is_authenticated_and_idempotent() {
    let f = Fixture::new();
    let station = profile(&f);
    let source = invoice(&f, "NATIVE-SW-HANDOFF");
    let id = source["id"].as_i64().unwrap();
    acd(&f, id);
    let review = call(&f, BUILD_AGENT_CONSIGNMENT_EXPORT_REVIEW, id, None);
    assert_eq!(review["totalErrorCount"], 0, "{review}");
    let package_path = f.root.join("submit.zip");
    let exported = call(
        &f,
        SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
        id,
        Some(json!({"packagePath":package_path,"stationAssignmentCode":""})),
    );
    let batch = exported["trackingBatchId"].as_i64().unwrap();
    let bytes = std::fs::read(&package_path).unwrap();
    let imported = f
        .client()
        .upload(
            UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
            &[],
            json!({}),
            "submit.zip",
            &bytes,
        )
        .unwrap();
    export_doc_contracts::validation::response(UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE.id, &imported)
        .unwrap();
    assert_eq!(imported["trackingBatchId"], batch);
    call(
        &f,
        DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT,
        0,
        Some(json!({"batchId":batch})),
    );
    let root = f
        .root
        .join(station["agentConsignmentClientRootPath"].as_str().unwrap());
    assert_eq!(std::fs::read_dir(root.join("OutBox")).unwrap().count(), 1);
    call(
        &f,
        DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT,
        0,
        Some(json!({"batchId":batch})),
    );
    assert_eq!(
        std::fs::read_dir(root.join("OutBox")).unwrap().count(),
        1,
        "repeat dispatch must not duplicate XML"
    );
    std::fs::create_dir_all(root.join("InBox")).unwrap();
    let receipt_path = root.join("InBox").join(format!(
        "{}.xml",
        exported["manifest"]["batchReference"].as_str().unwrap()
    ));
    std::fs::write(&receipt_path,"<ImportAgrResponse><ResponseCode>0</ResponseCode><ConsignNo>ACD-2026-01</ConsignNo><ResponseMessage>accepted &amp; archived</ResponseMessage></ImportAgrResponse>").unwrap();
    let collected = call(
        &f,
        COLLECT_SINGLE_WINDOW_CLIENT_RECEIPTS,
        0,
        Some(json!({"batchId":batch})),
    );
    assert_eq!(collected["receiptFiles"].as_array().unwrap().len(), 1);
    let receipt_zip = f.root.join("receipt.zip");
    call(
        &f,
        SAVE_SINGLE_WINDOW_RECEIPT_PACKAGE_TO_PATH,
        0,
        Some(
            json!({"businessType":"AgentConsignment","batchReference":exported["manifest"]["batchReference"],"invoiceNo":"NATIVE-SW-HANDOFF","receiptFiles":[receipt_path],"packagePath":receipt_zip}),
        ),
    );
    let receipt = std::fs::read(receipt_zip).unwrap();
    let first = f
        .client()
        .upload(
            UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
            &[],
            json!({}),
            "receipt.zip",
            &receipt,
        )
        .unwrap();
    assert_eq!(first["persistedReceiptCount"], 1);
    assert_eq!(first["trackingStatus"], "Accepted");
    let second = f
        .client()
        .upload(
            UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
            &[],
            json!({}),
            "receipt.zip",
            &receipt,
        )
        .unwrap();
    assert_eq!(second["persistedReceiptCount"], 0);
    let current = call(&f, GET_AGENT_CONSIGNMENT_DOCUMENT, id, None);
    assert_eq!(current["consignNo"], "ACD-2026-01");
    assert_eq!(current["status"], "Accepted");
    let detail = call(&f, GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL, batch, None);
    assert_eq!(detail["receiptRecords"].as_array().unwrap().len(), 1);
    assert!(detail.get("_manifest").is_none());
    let attacker = Fixture::new();
    profile(&attacker);
    assert!(
        attacker
            .client()
            .upload(
                UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
                &[],
                json!({}),
                "submit.zip",
                &bytes
            )
            .is_err()
    );
}
