#![cfg(feature = "single-window")]
#[path = "support/native_fixture.rs"]
mod native_fixture;
use export_doc_engine::{contracts, generated_api::*, invoice::InvoiceDraft};
#[path = "../../export-doc-single-window/tests/support/declarations.rs"]
#[allow(dead_code)]
mod declarations;
#[path = "support/single_window_sources_contract.rs"]
mod sources_contract;
use native_fixture::Fixture;
use serde_json::{Value, json};

fn call(f: &Fixture, op: Operation, id: i64, body: Option<Value>) -> Value {
    let path = if id > 0 {
        vec![(
            if op == GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL {
                "batchId"
            } else {
                "sourceId"
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

#[test]
fn independent_sources_keep_customs_parties_and_detail_rows_separate() {
    let f = Fixture::new();
    sources_contract::exercise(&|op, path, query, body| f.client().json(op, path, query, body));
}

#[test]
fn independent_sources_enforce_ownership_without_invoice_permissions() {
    let f = Fixture::new();
    let scheme = f.create(
        CREATE_PERMISSION_TEMPLATE,
        json!({"code":"SW-OWN","name":"本人申报","isActive":true,"grants":[
            {"resourceKey":"document.single-window","action":"view","dataScope":"own"},
            {"resourceKey":"document.single-window","action":"operate","dataScope":"own"}
        ]}),
    );
    let scheme = f.client().publish_scheme(scheme);
    let user = |name: &str| {
        f.create(CREATE_USER_ACCOUNT, json!({"username":name,"fullName":name,"role":"User","companyScope":"DEFAULT","departmentId":"GENERAL",
            "permissionTemplateId":scheme["id"],"permissionSchemeVersion":scheme["publishedVersion"],"isActive":true,"resetPassword":"Sw-Test-2026"}));
        f.client()
            .login(name.into(), "Sw-Test-2026".into())
            .unwrap()
            .0
    };
    let owner = user("sw-owner");
    let other = user("sw-other");
    let source: Value = owner
        .json(
            CREATE_SINGLE_WINDOW_SOURCE,
            &[],
            &[],
            Some(json!({"referenceNo":"OWN-001"})),
        )
        .unwrap();
    let parameters = [("sourceId", source["id"].to_string())];
    let draft: Value = owner
        .json(GET_CUSTOMS_COO_DOCUMENT, &parameters, &[], None)
        .unwrap();
    owner
        .json::<Value>(
            SAVE_CUSTOMS_COO_DOCUMENT,
            &parameters,
            &[],
            Some(draft.clone()),
        )
        .unwrap();
    assert_eq!(
        other
            .json::<Value>(GET_CUSTOMS_COO_DOCUMENT, &parameters, &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        other
            .json::<Value>(SAVE_CUSTOMS_COO_DOCUMENT, &parameters, &[], Some(draft))
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        other
            .json::<Value>(LIST_SINGLE_WINDOW_SOURCES, &[], &[], None)
            .unwrap()["totalCount"],
        0
    );
    let invoice = invoice(&f, "PROTECTED-INVOICE");
    assert_eq!(
        owner
            .json::<Value>(
                GET_AGENT_CONSIGNMENT_DOCUMENT,
                &[("sourceId", invoice["id"].to_string())],
                &[],
                None
            )
            .unwrap_err()
            .status,
        Some(403)
    );
    #[cfg(feature = "excel")]
    {
        let bytes = include_bytes!("../../export-doc-excel/tests/fixtures/last-row-formulas.xls");
        let preview = owner
            .upload(
                PREVIEW_SINGLE_WINDOW_EXCEL_IMPORT,
                &[],
                json!({}),
                "customer.xls",
                bytes,
            )
            .unwrap();
        assert_eq!(preview["success"], true, "{preview}");
        assert!(!preview["invoice"]["items"].as_array().unwrap().is_empty());
        assert_eq!(
            owner
                .json::<Value>(LIST_SINGLE_WINDOW_SOURCES, &[], &[], None)
                .unwrap()["totalCount"],
            1
        );
    }
}

#[test]
fn viewing_another_source_does_not_authorize_implicit_export_drafts() {
    let f = Fixture::new();
    let scheme = f.create(
        CREATE_PERMISSION_TEMPLATE,
        json!({"code":"SW-VIEW-ALL","name":"查看全部仅办理本人","isActive":true,"grants":[
            {"resourceKey":"document.single-window","action":"view","dataScope":"all"},
            {"resourceKey":"document.single-window","action":"operate","dataScope":"own"}
        ]}),
    );
    let scheme = f.client().publish_scheme(scheme);
    f.create(CREATE_USER_ACCOUNT, json!({"username":"sw-reader","fullName":"查看员","role":"User","companyScope":"DEFAULT","departmentId":"GENERAL",
        "permissionTemplateId":scheme["id"],"permissionSchemeVersion":scheme["publishedVersion"],"isActive":true,"resetPassword":"Sw-Test-2026"}));
    let reader = f
        .client()
        .login("sw-reader".into(), "Sw-Test-2026".into())
        .unwrap()
        .0;
    let source = f.create(
        CREATE_SINGLE_WINDOW_SOURCE,
        json!({"referenceNo":"NO-IMPLICIT-DRAFT"}),
    );
    let parameters = [("sourceId", source["id"].to_string())];
    let before: Value = reader
        .json(GET_CUSTOMS_COO_DOCUMENT, &parameters, &[], None)
        .unwrap();
    assert_eq!(
        reader
            .json::<Value>(
                DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE,
                &parameters,
                &[],
                Some(json!({"stationAssignmentCode":"","expectedDraftRevision":1}))
            )
            .unwrap_err()
            .status,
        Some(403)
    );
    let after: Value = reader
        .json(GET_CUSTOMS_COO_DOCUMENT, &parameters, &[], None)
        .unwrap();
    assert_eq!(after, before, "a rejected export must not persist a draft");
}

fn profile(f: &Fixture) -> Value {
    profile_at(f, &f.root)
}
fn profile_at(f: &Fixture, directory_root: &std::path::Path) -> Value {
    let boxes = |kind: &str| {
        let mut value = json!({});
        for key in ["outBox", "sentBox", "inBox", "failBox"] {
            let path = directory_root.join(format!("{key}-{kind}-自定义"));
            std::fs::create_dir_all(&path).unwrap();
            value[key] = json!(path);
        }
        value
    };
    let body = contracts::overlay(
        contracts::object(SAVE_SINGLE_WINDOW_CLIENT_PROFILE.id, true),
        &json!({"profileName":"测试持卡机","companyScope":"DEFAULT","cardIdentifier":"CARD-TEST","canSubmitCustomsCoo":true,"canSubmitAgentConsignment":true,"customsCooDirectories":boxes("coo"),"agentConsignmentDirectories":boxes("acd")}),
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
                &[("sourceId", id.to_string())],
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
    assert!(current["sourceDiffCount"].as_i64().unwrap() > 0);
    let export_path = f.root.join("changed-source.swpkg");
    assert_eq!(
        f.client().json::<Value>(SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
            &[("sourceId", id.to_string())], &[],
            Some(json!({"packagePath":export_path,"stationAssignmentCode":"","expectedDraftRevision":current["draftRevision"]})))
            .unwrap_err().status,
        Some(409)
    );
    assert!(!export_path.exists());
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
    let external = Fixture::new();
    let station = profile_at(&f, &external.root);
    let source = invoice(&f, "NATIVE-SW-HANDOFF");
    let id = source["id"].as_i64().unwrap();
    let saved = acd(&f, id);
    let review = call(&f, BUILD_AGENT_CONSIGNMENT_EXPORT_REVIEW, id, None);
    assert_eq!(review["totalErrorCount"], 0, "{review}");
    let package_path = f.root.join("submit.swpkg");
    let exported = call(
        &f,
        SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
        id,
        Some(
            json!({"packagePath":package_path,"stationAssignmentCode":"","expectedDraftRevision":saved["draftRevision"]}),
        ),
    );
    let batch = exported["trackingBatchId"].as_i64().unwrap();
    let bytes = std::fs::read(&package_path).unwrap();
    let imported = f
        .client()
        .upload(
            UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
            &[],
            json!({}),
            "submit.swpkg",
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
    let outbox = std::path::Path::new(
        station["agentConsignmentDirectories"]["outBox"]
            .as_str()
            .unwrap(),
    );
    let inbox = std::path::Path::new(
        station["agentConsignmentDirectories"]["inBox"]
            .as_str()
            .unwrap(),
    );
    assert_eq!(std::fs::read_dir(outbox).unwrap().count(), 1);
    call(
        &f,
        DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT,
        0,
        Some(json!({"batchId":batch})),
    );
    assert_eq!(
        std::fs::read_dir(outbox).unwrap().count(),
        1,
        "repeat dispatch must not duplicate XML"
    );
    let receipt_path = inbox.join(format!(
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
    let receipt_zip = f.root.join("receipt.swpkg");
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
            "receipt.swpkg",
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
            "receipt.swpkg",
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
                "submit.swpkg",
                &bytes
            )
            .is_err()
    );
}

#[test]
fn submit_packages_reject_missing_unsaved_and_stale_revisions_without_writing_files() {
    let f = Fixture::new();
    for (save, export) in [
        (
            SAVE_CUSTOMS_COO_DOCUMENT,
            SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
        ),
        (
            SAVE_AGENT_CONSIGNMENT_DOCUMENT,
            SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
        ),
    ] {
        let source = invoice(&f, export.id);
        let id = source["id"].as_i64().unwrap();
        let parameters = [("sourceId", id.to_string())];
        let path = f.root.join(format!("{}.swpkg", export.id));
        let mut request = json!({"packagePath":path,"stationAssignmentCode":""});
        assert_eq!(
            f.client()
                .json::<Value>(export, &parameters, &[], Some(request.clone()))
                .unwrap_err()
                .status,
            Some(400)
        );
        request["expectedDraftRevision"] = json!(1);
        assert_eq!(
            f.client()
                .json::<Value>(export, &parameters, &[], Some(request.clone()))
                .unwrap_err()
                .status,
            Some(409)
        );
        let mut draft = if save == SAVE_CUSTOMS_COO_DOCUMENT {
            let mut draft = declarations::coo("RC", "JAPAN", "9005");
            draft["sourceId"] = json!(id);
            call(&f, save, id, Some(draft))["document"].clone()
        } else {
            acd(&f, id)
        };
        request["expectedDraftRevision"] = draft["draftRevision"].clone();
        draft["expectedDraftRevision"] = draft["draftRevision"].clone();
        call(&f, save, id, Some(draft));
        assert_eq!(
            f.client()
                .json::<Value>(export, &parameters, &[], Some(request))
                .unwrap_err()
                .status,
            Some(409)
        );
        assert!(!path.exists());
    }
}

#[test]
fn client_directories_reject_missing_relative_and_overlapping_locations() {
    let f = Fixture::new();
    let station = profile(&f);
    let mut request =
        contracts::initial(contracts::schema("ApiSingleWindowClientProfileSaveRequest"));
    for key in request
        .as_object()
        .unwrap()
        .keys()
        .cloned()
        .collect::<Vec<_>>()
    {
        request[&key] = station[&key].clone();
    }
    for path in [
        "relative/OutBox".to_owned(),
        f.root.join("missing").to_string_lossy().into_owned(),
        station["customsCooDirectories"]["inBox"]
            .as_str()
            .unwrap()
            .to_owned(),
        f.root.to_string_lossy().into_owned(),
    ] {
        let mut invalid = request.clone();
        invalid["customsCooDirectories"]["outBox"] = json!(path);
        assert_eq!(
            f.client()
                .json::<Value>(SAVE_SINGLE_WINDOW_CLIENT_PROFILE, &[], &[], Some(invalid))
                .unwrap_err()
                .status,
            Some(400)
        );
    }
    // Two cards may use the same official task folders; the active manifest binding remains mandatory.
    request["profileKey"] = json!("");
    request["cardIdentifier"] = json!("SECOND-CARD");
    let result = call(&f, SAVE_SINGLE_WINDOW_CLIENT_PROFILE, 0, Some(request));
    assert_eq!(result["profiles"].as_array().unwrap().len(), 2);
    let mut disabled = station.clone();
    disabled["canSubmitCustomsCoo"] = json!(false);
    disabled["customsCooDirectories"]["outBox"] = json!(f.root.join("disconnected-device"));
    // Inactive task paths remain editable later and do not block the other task.
    let request_schema = contracts::schema("ApiSingleWindowClientProfileSaveRequest");
    call(
        &f,
        SAVE_SINGLE_WINDOW_CLIENT_PROFILE,
        0,
        Some(contracts::dto(request_schema, disabled)),
    );
}

#[test]
fn first_coo_receipt_matches_certificate_without_batch_filename() {
    let f = Fixture::new();
    let station = profile(&f);
    let source = call(
        &f,
        CREATE_SINGLE_WINDOW_SOURCE,
        0,
        Some(json!({"referenceNo":"COO-RECEIPT"})),
    );
    let id = source["id"].as_i64().unwrap();
    let mut draft = declarations::coo("RC", "JAPAN", "9005");
    draft["sourceId"] = json!(id);
    let saved = call(&f, SAVE_CUSTOMS_COO_DOCUMENT, id, Some(draft));
    let exported = call(
        &f,
        SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
        id,
        Some(
            json!({"packagePath":f.root.join("coo.swpkg"),"stationAssignmentCode":"","expectedDraftRevision":saved["document"]["draftRevision"]}),
        ),
    );
    let batch = exported["trackingBatchId"].as_i64().unwrap();
    f.client()
        .upload(
            UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
            &[],
            json!({}),
            "coo.swpkg",
            &std::fs::read(f.root.join("coo.swpkg")).unwrap(),
        )
        .unwrap();
    call(
        &f,
        DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT,
        0,
        Some(json!({"batchId":batch})),
    );
    let root = std::path::Path::new(station["customsCooDirectories"]["inBox"].as_str().unwrap())
        .join("2026-11-09");
    std::fs::create_dir_all(&root).unwrap();
    std::fs::write(root.join("official-random-name.xml"),
        "<Receipt><CertNo>RC261234567899005</CertNo><Channel>1</Channel><RepCode>0</RepCode><Note>received</Note></Receipt>").unwrap();
    let collected = call(
        &f,
        COLLECT_SINGLE_WINDOW_CLIENT_RECEIPTS,
        0,
        Some(json!({"batchId":batch})),
    );
    assert_eq!(
        collected["receiptFiles"].as_array().unwrap().len(),
        1,
        "{collected}"
    );
}
