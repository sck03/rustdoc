#[path = "support/account_scope.rs"]
mod account_scope;
use export_doc_engine::{
    engine::NativeService,
    generated_api::LOGIN,
    paths::{RuntimePaths, nonce},
};
use serde_json::{Value, json};
use std::path::PathBuf;

#[test]
fn sqlite_accounts_preserve_organization_and_revoke_changed_scope() {
    let parent = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/account-scope-tests");
    let root = parent.join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(RuntimePaths::server(&root, &root.join("Data")).unwrap()).unwrap();
    let login: Value = serde_json::from_slice(
        &service
            .dispatch(
                LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    account_scope::exercise(&service, login["accessToken"].as_str().unwrap());
    service.close().unwrap();
    drop(service);
    assert!(root.starts_with(&parent) && root != parent);
    std::fs::remove_dir_all(root).unwrap();
}
