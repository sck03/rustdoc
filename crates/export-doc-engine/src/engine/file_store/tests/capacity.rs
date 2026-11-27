use super::*;
use crate::engine::{
    file_store::capacity,
    test_support::{TestService, administrator},
};
use crate::generated_api::*;
#[test]
fn capacity_concurrent_sampling_publishes_each_transition_once() {
    let root = ScratchDirectory::new();
    let service = TestService::open(root.paths());
    let guard = service
        .capacity_sampling
        .write(std::time::Duration::from_secs(5))
        .unwrap();
    service.store.transaction(|tx| {
        tx.set_settings("storage-capacity-state", 0, &json!({}))?;
        tx.set_settings("storage-capacity-policy", 1, &json!({"revision":1,"policy":{"warningPercent":1,"criticalPercent":2,"minimumFreeBytes":u64::MAX,"growthDays":14}}))?;
        Ok(())
    }).unwrap();
    std::thread::scope(|scope| {
        let threads: Vec<_> = (0..4)
            .map(|_| scope.spawn(|| capacity::observe(&service, true)))
            .collect();
        drop(guard);
        for thread in threads {
            thread.join().unwrap().unwrap();
        }
    });
    let result =
        capacity::handle(&service, &administrator(), GET_STORAGE_CAPACITY, &json!({})).unwrap();
    assert_eq!(result["alerts"].as_array().unwrap().len(), 2);
    let state = service
        .store
        .settings("storage-capacity-state")
        .unwrap()
        .unwrap();
    assert_eq!(state["policyRevision"], 1);
    assert!(state["revision"].as_i64().unwrap() >= 4);
    let saved = capacity::handle(&service, &administrator(), SAVE_STORAGE_CAPACITY_POLICY, &json!({"expectedVersion":1,"policy":{"warningPercent":99,"criticalPercent":100,"minimumFreeBytes":0,"growthDays":14}})).unwrap();
    assert_eq!(saved["revision"], 2);
    assert_eq!(
        service
            .store
            .settings("storage-capacity-state")
            .unwrap()
            .unwrap()["policyRevision"],
        2
    );
    let guard = service
        .capacity_sampling
        .write(std::time::Duration::from_secs(5))
        .unwrap();
    let operation = crate::operation::OperationScope::new(std::time::Duration::from_secs(2));
    operation
        .cancellation_flag()
        .store(true, std::sync::atomic::Ordering::Release);
    assert!(operation.run(|| capacity::observe(&service, true)).is_err());
    drop(guard);
}
