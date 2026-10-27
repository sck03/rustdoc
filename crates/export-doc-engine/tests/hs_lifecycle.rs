#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{
    api::{ApiClient, ApiError},
    generated_api::*,
};
use export_doc_storage::{Connection, RecordWrite};
use serde_json::{Value, json};

fn call(client: &ApiClient, op: Operation, code: &str, body: Value) -> Result<Value, ApiError> {
    let parameters = if code.is_empty() {
        vec![]
    } else {
        vec![("code", code.into())]
    };
    client.json(op, &parameters, &[], (!body.is_null()).then_some(body))
}

#[test]
fn cleanup_rechecks_versions_is_atomic_and_retains_history() {
    let fixture = native_fixture::Fixture::new();
    let client = fixture.client();
    let request = |op, code, body| call(client, op, code, body);
    let db = Connection::sqlite(&fixture.root.join("Database/exportdoc-native.db")).unwrap();
    for (code, status, date) in [
        ("6109100000", "Obsolete", "2024-01-01T00:00:00Z"),
        ("6109100001", "Obsolete", "2024-01-01T00:00:00Z"),
        ("6109100002", "SuspectedObsolete", "2024-01-01T00:00:00Z"),
        ("6109100003", "Obsolete", "2099-01-01T00:00:00Z"),
    ] {
        let row = fixture.create(
            CREATE_HS_CODE,
            json!({"code":code,"name":"历史棉制T恤衫","status":"ReferenceOnly"}),
        );
        // Only storage fixtures can backdate a record; API writes reset this timestamp.
        let id = row["id"].as_i64().unwrap();
        let mut body = db.get("hs-codes", id).unwrap().unwrap();
        body["status"] = json!(status);
        body["updateTime"] = json!(date);
        let version = body["versionNumber"].as_i64().unwrap();
        assert!(
            db.update(
                id,
                version,
                &RecordWrite {
                    kind: "hs-codes",
                    identity: Some(code),
                    body: &body
                }
            )
            .unwrap()
        );
    }
    let history = request(
        SAVE_HS_CODE_KNOWLEDGE_EXAMPLE,
        "",
        json!({"rawReportedHsCode":"6109100000","productName":"历史棉制T恤衫","source":"历史单据"}),
    )
    .unwrap();
    let preview = || request(PREVIEW_HS_CODE_CLEANUP, "", Value::Null).unwrap();
    let total = || request(LIST_HS_CODES, "", Value::Null).unwrap()["totalCount"].clone();
    let first = preview();
    assert_eq!(first["eligibleCount"], 2);
    assert_eq!(first["retainedCount"], 2);
    export_doc_contracts::validation::response(PREVIEW_HS_CODE_CLEANUP.id, &first).unwrap();
    let mut changed = request(GET_HS_CODE, "6109100001", Value::Null).unwrap();
    changed["notes"] = json!("重新核对，重新计算过渡期");
    request(UPDATE_HS_CODE, "6109100001", changed).unwrap();
    let commit = |token: &Value| request(COMMIT_HS_CODE_CLEANUP, "", json!({"token":token}));
    assert_eq!(commit(&first["token"]).unwrap_err().status, Some(409));
    assert_eq!(total(), 4);
    let fresh = preview();
    assert_eq!(fresh["eligibleCount"], 1);
    commit(&fresh["token"]).unwrap();
    assert!(commit(&fresh["token"]).is_err());
    assert_eq!(total(), 3);
    let examples = request(LIST_HS_CODE_KNOWLEDGE_EXAMPLES, "", Value::Null).unwrap();
    assert_eq!(examples["items"][0]["id"], history["id"]);
    assert_eq!(examples["items"][0]["rawReportedHsCode"], "6109100000");
}

#[test]
fn missing_annual_update_never_makes_active_reference_or_suspected_codes_cleanup_eligible() {
    let fixture = native_fixture::Fixture::new();
    let client = fixture.client();
    let db = Connection::sqlite(&fixture.root.join("Database/exportdoc-native.db")).unwrap();
    for (code, status) in [
        ("6200000000", "Active"),
        ("6200000001", "ReferenceOnly"),
        ("6200000002", "SuspectedObsolete"),
    ] {
        let row = fixture.create(
            CREATE_HS_CODE,
            json!({"code":code,"name":"多年未更新的资料","status":"ReferenceOnly"}),
        );
        let id = row["id"].as_i64().unwrap();
        let mut body = db.get("hs-codes", id).unwrap().unwrap();
        body["status"] = json!(status);
        body["effectiveYear"] = json!(2020);
        body["sourceName"] = json!("旧年度税则");
        body["lastVerifiedAt"] = json!("2020-01-01T00:00:00Z");
        body["updateTime"] = json!("2020-01-01T00:00:00Z");
        assert!(
            db.update(
                id,
                1,
                &RecordWrite {
                    kind: "hs-codes",
                    identity: Some(code),
                    body: &body
                }
            )
            .unwrap()
        );
    }
    let preview = call(client, PREVIEW_HS_CODE_CLEANUP, "", Value::Null).unwrap();
    assert_eq!(preview["eligibleCount"], 0);
    assert_eq!(preview["retainedCount"], 3);
    call(
        client,
        COMMIT_HS_CODE_CLEANUP,
        "",
        json!({"token":preview["token"]}),
    )
    .unwrap();
    assert_eq!(
        call(client, LIST_HS_CODES, "", Value::Null).unwrap()["totalCount"],
        3
    );
    assert_eq!(
        call(client, GET_HS_CODE, "6200000000", Value::Null).unwrap()["requiresAnnualReview"],
        true
    );
}

#[cfg(feature = "excel")]
#[test]
fn annual_import_rejects_regressions_and_incomplete_snapshots() {
    use chrono::Datelike;
    let fixture = native_fixture::Fixture::new();
    let client = fixture.client();
    let year = chrono::Utc::now().year();
    let csv = "HS编码,商品名称\n6109100000,棉制针织T恤衫\n";
    let preview = |year, mode, csv: &str| {
        client.upload(
            PREVIEW_HS_CODES_IMPORT_UPLOAD,
            &[],
            json!({"mode":mode,"sourceName":"年度税则","effectiveYear":year}),
            "税则.csv",
            csv.as_bytes(),
        )
    };
    let commit = |preview: Value| {
        call(
            client,
            COMMIT_HS_CODES_IMPORT,
            "",
            json!({"token":preview["token"]}),
        )
        .unwrap()
    };
    let code = || call(client, GET_HS_CODE, "6109100000", Value::Null).unwrap();
    commit(preview(year - 1, "Incremental", csv).unwrap());
    assert_eq!(code()["requiresAnnualReview"], true);
    let old: Value = client
        .json(
            SEARCH_HS_CODE_KNOWLEDGE,
            &[],
            &[("query", "610910".into())],
            None,
        )
        .unwrap();
    assert!(old["items"].as_array().unwrap().is_empty());
    commit(preview(year, "Incremental", csv).unwrap());
    assert_eq!(code()["requiresAnnualReview"], false);
    assert_eq!(
        preview(year - 1, "Incremental", csv).unwrap()["conflictCount"],
        1
    );
    assert!(preview(year + 1, "Incremental", csv).is_err());
    assert!(preview(year, "CompleteSnapshot", "HS编码,商品名称\nBAD,错误行\n").is_err());
    let partial = preview(
        year,
        "CompleteSnapshot",
        "HS编码,商品名称\n6109900000,化纤T恤衫\n",
    )
    .unwrap();
    assert_eq!(partial["suspectedObsoleteCount"], 1);
    commit(partial);
    assert_eq!(code()["status"], "SuspectedObsolete");
    let review = call(client, PREVIEW_HS_CODES_OBSOLETE, "", Value::Null).unwrap();
    assert_eq!(review["totalCount"], 1);
    assert_eq!(review["items"][0]["code"], "6109100000");
    export_doc_contracts::validation::response(PREVIEW_HS_CODES_OBSOLETE.id, &review).unwrap();
    call(client, CONFIRM_HS_CODES_OBSOLETE, "", json!({"items":review["items"].as_array().unwrap().iter()
        .map(|row| json!({"id":row["id"],"rowVersion":row["rowVersion"]})).collect::<Vec<_>>(),"reason":"官方税则已核实失效"})).unwrap();
    assert_eq!(code()["status"], "Obsolete");
    assert_eq!(
        call(client, PREVIEW_HS_CODE_CLEANUP, "", Value::Null).unwrap()["eligibleCount"],
        0
    );
}
