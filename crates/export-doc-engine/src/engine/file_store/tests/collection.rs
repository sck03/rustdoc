use super::*;
use crate::engine::test_support::{TestService, administrator};

#[test]
fn collection_failure_keeps_deleted_counts_and_retryable_journal() {
    let root = ScratchDirectory::new();
    let service = TestService::open(root.paths());
    let files = &service.store.files;
    let first = files.prepare(b"first").unwrap().object.clone();
    let second = files.prepare(b"second").unwrap().object.clone();
    let config = files.configuration().unwrap();
    let blocked = files.object_path(&config, &second).unwrap();
    fs::remove_file(&blocked).unwrap();
    fs::create_dir(&blocked).unwrap();
    service
        .store
        .transaction(|tx| {
            tx.set_settings(
                "file-collection-plan",
                1,
                &json!({
                    "token":"test-plan", "owner":administrator().id,
                    "expiresAt":(chrono::Utc::now()+chrono::Duration::minutes(15)).to_rfc3339(),
                    "identity":config.identity, "revision":config.revision,
                    "objects":[first,second],"archiveCleanup":[],
                }),
            )?;
            Ok(())
        })
        .unwrap();
    let error =
        super::super::cleanup::execute(&service.store, &administrator(), "test-plan").unwrap_err();
    assert_eq!(
        error.public_body("collection")["outcome"],
        "PartiallyCommitted"
    );
    assert!(error.message.contains("1 个对象、5 字节"));
    assert!(error.message.contains("无引用对象删除"));
    assert!(!files.object_path(&config, &first).unwrap().exists());
    assert!(
        service
            .store
            .settings("file-collection-plan")
            .unwrap()
            .is_some()
    );
    fs::remove_dir(&blocked).unwrap();
    assert_eq!(files.reconcile(&service.store).unwrap(), 11);
    assert_eq!(files.reconcile(&service.store).unwrap(), 0);
}

#[test]
fn cancellation_after_a_physical_delete_is_partial() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    let first = store.files.prepare(b"deleted").unwrap().object.clone();
    let second = store.files.prepare(b"keep").unwrap().object.clone();
    let config = store.files.configuration().unwrap();
    let mut progress = super::super::CollectionProgress::default();
    let scope = crate::operation::OperationScope::new(std::time::Duration::from_secs(10));
    let result = scope.run(|| {
        store
            .files
            .collect(&store, &config, &first, &mut progress)?;
        scope.cancel();
        store.files.collect(&store, &config, &second, &mut progress)
    });
    let cause = progress.failure(result.unwrap_err());
    assert_eq!(
        cause.public_body("collection")["outcome"],
        "PartiallyCommitted"
    );
    assert!(cause.message.contains("1 个对象、7 字节"));
    assert!(store.files.object_path(&config, &second).unwrap().exists());
}
