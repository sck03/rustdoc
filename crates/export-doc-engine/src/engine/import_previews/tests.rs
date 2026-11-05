use super::*;

#[test]
fn previews_are_bounded_single_use_and_release_payloads_transactionally() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let path = directory.0.join("preview.db");
    let pool = export_doc_storage::pool::Pool::single(
        export_doc_storage::Connection::sqlite(&path).unwrap(),
    );
    let clock = BusinessClock::default();
    let tx = Connection::new(pool.acquire(|| false).unwrap(), &clock);
    let actor = Actor {
        company: "TEST".into(),
        department: "D".into(),
        admin: true,
        ..crate::engine::test_support::actor("test")
    };
    let rows = vec![json!({"name":"测试"})];
    tx.begin().unwrap();
    let token = save(&tx, &actor, &clock, "suppliers", &rows).unwrap();
    tx.commit().unwrap();
    tx.begin().unwrap();
    assert_eq!(
        consume(&tx, &actor, &clock, "suppliers", &token).unwrap(),
        rows
    );
    assert_eq!(tx.import_preview_usage(actor.id).unwrap().bytes, 0);
    tx.rollback().unwrap();
    assert!(tx.import_preview_usage(actor.id).unwrap().bytes > 0);
    tx.begin().unwrap();
    consume(&tx, &actor, &clock, "suppliers", &token).unwrap();
    assert_eq!(
        consume(&tx, &actor, &clock, "suppliers", &token)
            .unwrap_err()
            .status,
        Some(409)
    );
    tx.commit().unwrap();
    tx.begin().unwrap();
    assert_eq!(
        save(&tx, &actor, &clock, "suppliers", &vec![json!({}); 5001])
            .unwrap_err()
            .status,
        Some(400)
    );
    let large = vec![json!({"value":"a".repeat(16*1024*1024)})];
    assert_eq!(
        save(&tx, &actor, &clock, "suppliers", &large)
            .unwrap_err()
            .status,
        Some(400)
    );
    for _ in 0..200 {
        save(&tx, &actor, &clock, "suppliers", &rows).unwrap();
    }
    assert_eq!(
        save(&tx, &actor, &clock, "suppliers", &rows)
            .unwrap_err()
            .status,
        Some(429)
    );
    tx.rollback().unwrap();
    drop(tx);
    drop(pool);
    std::fs::remove_file(path).unwrap();
}
