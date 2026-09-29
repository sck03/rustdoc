use super::*;

#[test]
fn queued_writes_refresh_permissions_before_the_transaction() {
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/authorization-tests")
        .join(crate::paths::nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(RuntimePaths::server(&root, &root.join("Data")).unwrap()).unwrap();
    let admin_id = service.store.all("users").unwrap()[0]["id"]
        .as_i64()
        .unwrap();
    let admin = auth::current_actor(&service.store, admin_id).unwrap();
    let saved = accounts::save(&service.store, &admin, 0, json!({"username":"revoked","fullName":"待停用账号","role":"Admin","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Revocation-Test-2026"})).unwrap();
    let mut account = saved["user"].clone();
    let id = account["id"].as_i64().unwrap();
    let stale = auth::current_actor(&service.store, id).unwrap();
    account["expectedVersion"] = account["versionNumber"].clone();
    account["role"] = json!("OfficeEmployee");
    account = accounts::save(&service.store, &admin, id, account).unwrap()["user"].clone();
    let attempted = json!({"username":"unauthorized","fullName":"不应创建","role":"Admin","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Revocation-Test-2026"});
    assert_eq!(
        accounts::save(&service.store, &stale, 0, attempted)
            .unwrap_err()
            .status,
        Some(403)
    );
    let called = std::cell::Cell::new(false);
    account["expectedVersion"] = account["versionNumber"].clone();
    account["isActive"] = json!(false);
    accounts::save(&service.store, &admin, id, account).unwrap();
    assert_eq!(
        service
            .store
            .transaction_as(&stale, |_, _| {
                called.set(true);
                Ok(())
            })
            .unwrap_err()
            .status,
        Some(403)
    );
    assert!(!called.get());
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
