use super::*;
use std::time::Instant;
async fn queued(gate: &Admission) {
    let deadline = Instant::now() + Duration::from_secs(5);
    while gate.queued() == 0 {
        assert!(Instant::now() < deadline);
        tokio::task::yield_now().await;
    }
}
#[tokio::test]
async fn queue_is_fair_bounded_and_releases_canceled_waiters() {
    let root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../.codex-runtime/admission-tests")
        .join(export_doc_engine::paths::nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let telemetry = Arc::new(Telemetry::new(root.clone()));
    let gate = Arc::new(Admission::new(1, 1, Duration::from_secs(1)));
    let first = gate.acquire(&telemetry).await.unwrap();
    let wait = {
        let gate = gate.clone();
        let telemetry = telemetry.clone();
        tokio::spawn(async move { gate.acquire(&telemetry).await.unwrap() })
    };
    queued(&gate).await;
    assert_eq!(
        gate.acquire(&telemetry).await.err().unwrap().status,
        Some(429)
    );
    drop(first);
    let competing = {
        let gate = gate.clone();
        let telemetry = telemetry.clone();
        tokio::spawn(async move { gate.acquire(&telemetry).await.unwrap() })
    };
    let older = wait.await.unwrap();
    tokio::task::yield_now().await;
    assert!(
        !competing.is_finished(),
        "the earlier waiter owns the next slot"
    );
    drop(older);
    let held = competing.await.unwrap();
    let canceled = {
        let gate = gate.clone();
        let telemetry = telemetry.clone();
        tokio::spawn(async move { gate.acquire(&telemetry).await })
    };
    queued(&gate).await;
    canceled.abort();
    assert!(canceled.await.is_err());
    assert_eq!(gate.queued(), 0);
    assert_eq!(
        gate.acquire(&telemetry).await.err().unwrap().status,
        Some(429)
    );
    drop(held);
    assert_eq!(gate.available(), 1);
    let metrics = telemetry.snapshot(gate.available(), 2, gate.queued());
    assert!(metrics["queueWait"]["failures"].as_u64().unwrap() >= 3);
    drop(gate);
    drop(telemetry);
    std::fs::remove_dir_all(root).unwrap();
}
