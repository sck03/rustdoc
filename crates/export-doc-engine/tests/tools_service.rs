#[path = "support/native_fixture.rs"]
mod native_fixture;
#[path = "support/tools_contract.rs"]
mod tools_contract;

#[test]
fn packing_and_knowledge_follow_shared_business_contracts() {
    let fixture = native_fixture::Fixture::new();
    tools_contract::exercise(&|op, path, query, body| fixture.client().json(op, path, query, body));
}

#[cfg(feature = "excel")]
#[test]
fn tariff_preview_is_single_use_detects_stale_changes_and_roundtrips_knowledge() {
    use export_doc_engine::generated_api::*;
    use serde_json::{Value, json};
    let fixture = native_fixture::Fixture::new();
    let client = fixture.client();
    let csv = "HS编码,商品名称,法定第一单位,退税率,申报要素\n6109100000,棉制针织T恤衫,011,13%,100%棉 针织\n6109900000,化纤针织T恤衫,011,13%,100%涤纶 针织\n";
    let preview = || {
        client
            .upload(
                PREVIEW_HS_CODES_IMPORT_UPLOAD,
                &[],
                json!({"mode":"Incremental","sourceName":"回归税则","effectiveYear":2026}),
                "税则.csv",
                csv.as_bytes(),
            )
            .unwrap()
    };
    let commit = |token: &Value| {
        let body = Some(json!({"token":token}));
        client.json::<Value>(COMMIT_HS_CODES_IMPORT, &[], &[], body)
    };
    let first = preview();
    assert_eq!(first["addCount"], 2);
    export_doc_contracts::validation::response(PREVIEW_HS_CODES_IMPORT_UPLOAD.id, &first).unwrap();
    let before: Value = client.json(LIST_HS_CODES, &[], &[], None).unwrap();
    assert_eq!(before["totalCount"], 0);
    assert_eq!(commit(&first["token"]).unwrap()["addedCount"], 2);
    assert!(commit(&first["token"]).is_err());
    let stale = preview();
    let code = [("code", "6109100000".into())];
    let mut current: Value = client.json(GET_HS_CODE, &code, &[], None).unwrap();
    assert_eq!(current["status"], "Active");
    assert_eq!(current["unit"], "件");
    current["notes"] = json!("年度复核");
    client
        .json::<Value>(UPDATE_HS_CODE, &code, &[], Some(current))
        .unwrap();
    assert_eq!(commit(&stale["token"]).unwrap_err().status, Some(409));
    let query = [("query", "610910".into())];
    let response: Value = client
        .json(SEARCH_HS_CODE_KNOWLEDGE, &[], &query, None)
        .unwrap();
    assert_eq!(response["items"][0]["canUse"], true);
    for _ in 0..3 {
        client.json::<Value>(RECORD_HS_CODE_KNOWLEDGE_FEEDBACK, &[], &[], Some(json!({"queryText":"棉制针织T恤衫","productName":"棉制针织T恤衫","specification":"100%棉 针织","candidateCode":"6109100000","accepted":true}))).unwrap();
    }
    let examples: Value = client
        .json(LIST_HS_CODE_KNOWLEDGE_EXAMPLES, &[], &[], None)
        .unwrap();
    assert_eq!(examples["totalCount"], 1);
    let archive = client
        .bytes(EXPORT_HS_CODE_KNOWLEDGE, &[], &[], None, 1024 * 1024)
        .unwrap();
    let future = client
        .bytes(
            EXPORT_HS_CODE_KNOWLEDGE,
            &[],
            &[("since", "2099-01-01T00:00:00Z".into())],
            None,
            1024 * 1024,
        )
        .unwrap();
    let empty = native_fixture::Fixture::new();
    let result = empty
        .client()
        .upload(
            IMPORT_HS_CODE_KNOWLEDGE,
            &[],
            json!({}),
            "future.zip",
            &future,
        )
        .unwrap();
    assert_eq!(result["hsCodeCount"], 0);
    // A failure in the last member must roll back earlier codes and examples.
    let broken = corrupt_last_checksum(&archive);
    assert_eq!(
        empty
            .client()
            .upload(
                IMPORT_HS_CODE_KNOWLEDGE,
                &[],
                json!({}),
                "broken.zip",
                &broken
            )
            .unwrap_err()
            .status,
        Some(400)
    );
    let rolled_back: Value = empty.client().json(LIST_HS_CODES, &[], &[], None).unwrap();
    assert_eq!(rolled_back["totalCount"], 0);
    let second = native_fixture::Fixture::new();
    let import = |bytes: &[u8]| {
        second
            .client()
            .upload(IMPORT_HS_CODE_KNOWLEDGE, &[], json!({}), "知识.zip", bytes)
    };
    import(&archive).unwrap();
    let imported: Value = second
        .client()
        .json(SEARCH_HS_CODE_KNOWLEDGE, &[], &query, None)
        .unwrap();
    assert_eq!(imported["items"][0]["canUse"], true);
    assert_eq!(imported["items"][0]["exampleCount"], 1);
    assert!(import(&archive[..archive.len() / 2]).is_err());
    let after: Value = second.client().json(LIST_HS_CODES, &[], &[], None).unwrap();
    assert_eq!(after["totalCount"], 2);
}

#[cfg(feature = "excel")]
fn corrupt_last_checksum(bytes: &[u8]) -> Vec<u8> {
    use std::io::{Cursor, Read, Write};
    let mut source = zip::ZipArchive::new(Cursor::new(bytes)).unwrap();
    let mut target = zip::ZipWriter::new(Cursor::new(Vec::new()));
    for index in 0..source.len() {
        let mut entry = source.by_index(index).unwrap();
        let name = entry.name().unwrap().into_owned();
        let mut content = Vec::new();
        entry.read_to_end(&mut content).unwrap();
        if name == "manifest.json" {
            let mut manifest: serde_json::Value = serde_json::from_slice(&content).unwrap();
            manifest["checksums"]["search-feedback.json"] = serde_json::json!("invalid");
            content = serde_json::to_vec(&manifest).unwrap();
        }
        target
            .start_file(name, zip::write::SimpleFileOptions::default())
            .unwrap();
        target.write_all(&content).unwrap();
    }
    target.finish().unwrap().into_inner()
}
