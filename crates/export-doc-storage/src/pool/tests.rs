use super::*;
use serde_json::json;
use std::{
    path::PathBuf,
    sync::atomic::AtomicBool,
    time::{SystemTime, UNIX_EPOCH},
};

#[test]
fn bounded_wait_cancellation_and_abandoned_transactions_are_observable() {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/pool-tests")
        .join(format!(
            "{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
    std::fs::create_dir_all(&root).unwrap();
    let pool = Arc::new(Pool::new(
        vec![Connection::sqlite(&root.join("pool.db")).unwrap()],
        PoolOptions {
            size: 1,
            acquire_timeout: Duration::from_millis(100),
        },
    ));
    let held = pool.acquire(|| false).unwrap();
    assert!(
        pool.health().is_ok(),
        "saturation must not invalidate readiness"
    );
    let waiting = pool.clone();
    let failed = std::thread::spawn(move || waiting.acquire(|| false).err().unwrap().kind)
        .join()
        .unwrap();
    assert_eq!(failed, ErrorKind::Busy);
    assert_eq!(pool.snapshot()["acquireTimeouts"], 1);
    let canceled = AtomicBool::new(true);
    assert_eq!(
        pool.acquire(|| canceled.load(Relaxed)).err().unwrap().kind,
        ErrorKind::Timeout
    );
    drop(held);
    let id = {
        let lease = pool.acquire(|| false).unwrap();
        lease.begin().unwrap();
        lease.insert(&crate::RecordWrite {kind:"pool-contract",identity:None,body:&json!({"versionNumber":1,"ownerUserId":1,"companyScope":"A","departmentId":"D"})}).unwrap()
    };
    assert!(
        pool.acquire(|| false)
            .unwrap()
            .get("pool-contract", id)
            .unwrap()
            .is_none()
    );
    let snapshot = pool.snapshot();
    assert_eq!(snapshot["leased"], 0);
    assert_eq!(snapshot["failed"], false);
    assert!(snapshot["wait"]["failures"].as_u64().unwrap() >= 2);
    assert!(
        snapshot["wait"]["buckets"]
            .as_array()
            .unwrap()
            .last()
            .unwrap()["upperBoundMicroseconds"]
            .is_null()
    );
    drop(pool);
    std::fs::remove_dir_all(root).unwrap();
}
