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
            json!({"code":code,"name":"历史棉制T恤衫","status":status}),
        );
        // Only storage fixtures can backdate a record; API writes reset this timestamp.
        let id = row["id"].as_i64().unwrap();
        let mut body = db.get("hs-codes", id).unwrap().unwrap();
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
    assert_eq!(
        call(client, PREVIEW_HS_CODE_CLEANUP, "", Value::Null).unwrap()["eligibleCount"],
        0
    );
}
