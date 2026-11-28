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

#[test]
fn discovery_advances_past_a_full_page_and_tolerates_retired_receipts() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let config = store.files.configuration().unwrap();
    let object = export_doc_storage::FileObject {
        digest: "a".repeat(64),
        size: 1,
        archive_identity: String::new(),
    };
    let first = store.files.begin_upload(&config, &object).unwrap();
    let bytes = std::fs::read(&first.path).unwrap();
    drop(first);
    // Populate discovery volume directly; durable publication is covered above.
    for _ in 0..1000 {
        std::fs::write(
            store
                .files
                .journal_root()
                .join(format!("{}.json", crate::paths::nonce().unwrap())),
            &bytes,
        )
        .unwrap();
    }
    assert_eq!(
        store
            .files
            .discover_intents(&store, &config)
            .unwrap()
            .uploads
            .len(),
        1000
    );
    assert_eq!(
        store
            .files
            .discover_intents(&store, &config)
            .unwrap()
            .uploads
            .len(),
        1
    );
    let missing = store.files.journal_root().join("retired.json");
    assert!(super::discovery::read_intent(&missing).unwrap().is_none());
    std::fs::write(&missing, b"invalid receipt").unwrap();
    assert!(super::discovery::read_intent(&missing).is_err());
}
