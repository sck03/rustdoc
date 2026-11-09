use super::*;
use std::time::Duration;

#[test]
fn dispatch_completion_preserves_receipts_and_rejects_obsolete_attempts() {
    for succeeded in [false, true] {
        for status in ["Accepted", "PendingReview", "Approved", "Rejected"] {
            let mut row = json!({"status":status,"clientDispatchOperationId":"current","clientDispatchLeaseUntil":"lease"});
            let before = row.clone();
            assert!(bridge::complete_dispatch(&mut row, "old", succeeded).is_err());
            assert_eq!(row, before);
            bridge::complete_dispatch(&mut row, "current", succeeded).unwrap();
            assert_eq!(row["status"], status);
            assert!(row["clientDispatchLeaseUntil"].is_null());
        }
        let mut row = json!({"status":"ClientDispatching","clientDispatchOperationId":"current"});
        bridge::complete_dispatch(&mut row, "current", succeeded).unwrap();
        assert_eq!(
            row["status"],
            if succeeded {
                "QueuedToClient"
            } else {
                "ClientDispatchFailed"
            }
        );
    }
}

#[test]
fn operation_center_reads_projected_pages_while_a_writer_is_busy() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
    let actor = auth::current_actor(
        &service.store,
        service.store.all("users").unwrap()[0]["id"]
            .as_i64()
            .unwrap(),
    )
    .unwrap();
    let row = service.store.transaction(|tx| tracking::save(tx, &actor, json!({
        "batchReference":"BATCH-ONE", "businessType":"AgentConsignment", "invoiceNo":"CAFÉ%_", "status":"Pending",
        "_manifest":{"padding":"x".repeat(100_000)}, "_assignmentSecret":"private"
    }), None)).unwrap();
    let external =
        export_doc_storage::Connection::sqlite(&service.paths.sqlite_database_path()).unwrap();
    let (ready, started) = std::sync::mpsc::channel();
    let (release, blocked) = std::sync::mpsc::channel();
    let writer = std::thread::spawn(move || {
        external.begin().unwrap();
        ready.send(()).unwrap();
        blocked.recv_timeout(Duration::from_secs(5)).unwrap();
        external.rollback().unwrap();
    });
    started.recv().unwrap();
    let result = crate::operation::OperationScope::new(Duration::from_secs(2)).run(|| {
        let bytes = handle(
            &service,
            &actor,
            LIST_SINGLE_WINDOW_OPERATION_CENTER,
            &[],
            &[
                ("keyword", "cafe\u{301}%_".into()),
                ("pageSize", "1".into()),
            ],
            &Value::Null,
        )?;
        let page: Value = serde_json::from_slice(&bytes)?;
        assert_eq!(page["totalCount"], 1);
        assert_eq!(page["rows"][0]["batchId"], row["batchId"]);
        assert!(!String::from_utf8(bytes).unwrap().contains("private"));
        assert!(page["rows"][0].get("_manifest").is_none());
        let detail = handle(
            &service,
            &actor,
            GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL,
            &[("batchId", row["batchId"].to_string())],
            &[],
            &Value::Null,
        )?;
        assert!(!String::from_utf8(detail).unwrap().contains("private"));
        Result::Ok(())
    });
    release.send(()).unwrap();
    writer.join().unwrap();
    result.unwrap();
}
