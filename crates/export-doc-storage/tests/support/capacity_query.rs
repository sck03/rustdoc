use export_doc_storage::{BlobWrite, Connection, HsQuery, JobOutputRetention, RecordWrite};
use serde_json::{Value, json};

fn insert(c: &Connection, kind: &str, mut body: Value, owner: i64) -> i64 {
    body["versionNumber"] = json!(1);
    body["ownerUserId"] = json!(owner);
    body["companyScope"] = json!("CAPACITY");
    body["departmentId"] = json!("D");
    let id = c
        .insert(&RecordWrite {
            kind,
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(id);
    c.set_body(id, &body).unwrap();
    id
}

pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    for n in 0..250 {
        insert(
            c,
            "hs-codes",
            json!({"code":format!("6109{n:06}"),"status":"Active","effectiveYear":if n%2==0 {2026}else{2025},"name":"棉针织","privateMarker":"not projected"}),
            1,
        );
    }
    let fields = [("code", "code"), ("effectiveYear", "effectiveYear")];
    let query = HsQuery {
        kind: "hs-codes",
        current_year: Some(2026),
        prefix: "6109",
        ranked_examples: false,
        fields: &fields,
        offset: 3,
        limit: 10,
    };
    let rows = c.query_hs(&query).unwrap();
    assert_eq!(rows.len(), 10);
    assert_eq!(rows[0]["code"], "6109000006");
    assert!(
        rows.iter()
            .all(|row| row["effectiveYear"] == 2026 && row.get("privateMarker").is_none())
    );
    assert!(
        c.query_hs(&HsQuery {
            limit: 2001,
            ..query
        })
        .is_err()
    );
    for n in 0..205 {
        insert(
            c,
            "hs-examples",
            json!({"rawReportedHsCode":"6109100000","resolvedCurrentHsCode":"6109100000","isManuallyVerified":n%3==0,"useCount":n,"updatedAt":"2026-10-24T00:00:00Z"}),
            1,
        );
    }
    let examples = c
        .query_hs(&HsQuery {
            kind: "hs-examples",
            current_year: None,
            prefix: "6109",
            ranked_examples: true,
            fields: &[],
            offset: 0,
            limit: 2000,
        })
        .unwrap();
    assert_eq!(
        examples.len(),
        205,
        "bounded scan must not inherit the list-page cap of 200"
    );
    assert_eq!(examples[0]["useCount"], 204);
    assert_eq!(examples[0]["isManuallyVerified"], true);

    let mut ids = vec![];
    for (n, owner) in [1, 1, 1, 2, 2].into_iter().enumerate() {
        let time = format!("2026-10-24T00:00:{n:02}Z");
        let id = insert(
            c,
            "background-jobs",
            json!({"jobId":format!("capacity-{n}"),"requestedByUserId":owner,"status":"Succeeded","createdAt":time,"completedAt":time}),
            owner,
        );
        c.insert_blob(&BlobWrite {
            kind: "job-output",
            record_id: id,
            file_name: "result.bin",
            media_type: "application/octet-stream",
            digest: "test",
            content: b"123456",
            created_at: &time,
        })
        .unwrap();
        let info = c.blob_info(id, "job-output").unwrap().unwrap();
        assert_eq!(info.size, 6);
        assert_eq!(info.file_name, "result.bin");
        ids.push(id);
    }
    let policy = JobOutputRetention {
        global_bytes: 18,
        per_user_bytes: 12,
    };
    let expired = c.expired_job_outputs(&policy).unwrap();
    assert_eq!(
        expired
            .iter()
            .map(|row| row["id"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        ids[..2]
    );
    assert_eq!(c.job_output_bytes().unwrap(), 30);
    for row in expired {
        c.delete_blob(row["id"].as_i64().unwrap(), "job-output")
            .unwrap();
    }
    assert_eq!(c.job_output_bytes().unwrap(), 18);
    assert!(c.expired_job_outputs(&policy).unwrap().is_empty());
    assert!(
        ids.iter()
            .all(|id| c.get("background-jobs", *id).unwrap().is_some())
    );
    c.rollback().unwrap();
}
