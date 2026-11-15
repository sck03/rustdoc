use crate::engine::{store::Store, test_support::ScratchDirectory};

#[test]
fn collection_claim_excludes_only_the_same_object_and_recovers_after_interruption() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let upload = store.files.prepare(b"orphan").unwrap();
    let object = upload.object.clone();
    let config = store.files.configuration().unwrap();
    assert!(
        store
            .files
            .claim_collection(&store, &config, &object)
            .unwrap()
            .is_none()
    );
    drop(upload);
    let claim = store
        .files
        .claim_collection(&store, &config, &object)
        .unwrap()
        .unwrap();
    assert_eq!(
        store.files.prepare(b"orphan").err().unwrap().status,
        Some(429)
    );
    let other = store.files.prepare(b"unrelated").unwrap();
    crate::operation::OperationScope::new(std::time::Duration::from_millis(500))
        .run(|| store.transaction(|_| Ok(())))
        .unwrap();
    // The delete completed but its SQL completion was interrupted. A later run
    // sees the durable intent, accepts a missing object and clears the intent.
    store.files.remove_object(&config, &object).unwrap();
    drop(claim);
    store.files.reconcile(&store).unwrap();
    assert!(store.files.prepare(b"orphan").is_ok());
    assert!(store.files.open_object(&other.object).is_ok());
}
