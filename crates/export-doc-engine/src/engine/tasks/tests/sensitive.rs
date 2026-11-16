use super::*;

#[test]
fn replay_separates_private_fields_and_bounds_retention() {
    let replay = retry::Replay::new(
        crate::generated_api::START_INVOICE_DOCUMENT_EMAIL_JOB,
        &[],
        &json!({"body":"private canary","subject":"public subject"}),
    )
    .with_sensitive_fields(&["body"])
    .unwrap();
    let stored = serde_json::to_string(&replay).unwrap();
    assert!(!stored.contains("private canary"));
    assert!(stored.contains("public subject"));
    let key = replay.sensitive_key.unwrap();
    let mut cache = Inputs::default();
    cache.insert(key.clone(), replay.sensitive.unwrap());
    assert_eq!(cache.get(&key).unwrap()["body"], "private canary");
    for index in 0..128 {
        cache.insert(index.to_string(), json!({"body":"bounded"}));
    }
    assert_eq!(cache.get(&key).unwrap_err().status, Some(409));
    cache.remove("127");
    assert!(cache.get("127").is_err());
    assert!(
        Inputs::default().get("126").is_err(),
        "restart requires re-entry"
    );
    assert!(
        retry::Replay::new(
            crate::generated_api::START_INVOICE_DOCUMENT_EMAIL_JOB,
            &[],
            &json!({"body":"x".repeat(65536)})
        )
        .with_sensitive_fields(&["body"])
        .is_err()
    );
}
