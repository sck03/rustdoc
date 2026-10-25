use super::*;

#[test]
fn exports_refresh_permissions_and_only_include_contacts_in_their_independent_scope() {
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/party-export-tests")
        .join(crate::paths::nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service =
        NativeService::open(crate::paths::RuntimePaths::server(&root, &root.join("Data")).unwrap())
            .unwrap();
    let id = service.store.all("users").unwrap()[0]["id"]
        .as_i64()
        .unwrap();
    let admin = auth::current_actor(&service.store, id).unwrap();
    let saved = super::super::accounts::save(&service.store, &admin, 0, json!({
        "username":"party-export", "fullName":"导出账号", "role":"User", "companyScope":"DEFAULT", "departmentId":"GENERAL", "isActive":true, "resetPassword":"Export-Test-2026",
        "permissionGrants":[
            {"resourceKey":"sales.customers","action":"export","dataScope":"company"},
            {"resourceKey":"sales.suppliers","action":"export","dataScope":"company"},
            {"resourceKey":"sales.contacts","action":"view","dataScope":"own"},
            {"resourceKey":"sales.supplier-contacts","action":"view","dataScope":"own"}
        ]
    })).unwrap();
    let mut account = saved["user"].clone();
    let actor = auth::current_actor(&service.store, account["id"].as_i64().unwrap()).unwrap();
    for supplier in [false, true] {
        service.store.transaction(|tx| {
            let party = store::save(tx, kind(supplier), 0, json!({"name":"权限测试单位","status":"合作中"}), None, &admin, "create")?;
            for (owner, primary, name) in [(&admin, true, "隐藏的主要联系人"), (&actor, false, "可见的联系人")] {
                let mut contact = json!({"name":name,"email":"contact@example.test","phone":"123456","isPrimary":primary});
                contact[relation(supplier)] = party["id"].clone();
                store::save(tx, contact_kind(supplier), 0, contact, None, owner, "create")?;
            }
            Ok(())
        }).unwrap();
    }
    for operation in [EXPORT_CRM_CUSTOMERS, EXPORT_SUPPLIERS] {
        let bytes = export(&service, &actor, operation, &[]).unwrap();
        let rows = export_doc_excel::read_table(&bytes, "导出.xlsx", 100, &|| Ok(())).unwrap();
        assert!(rows[1].iter().any(|value| value == "可见的联系人"));
        assert!(!rows[1].iter().any(|value| value == "隐藏的主要联系人"));
    }
    account["expectedVersion"] = account["versionNumber"].clone();
    account["permissionGrants"]
        .as_array_mut()
        .unwrap()
        .retain(|grant| {
            ["sales.customers", "sales.suppliers"]
                .iter()
                .any(|key| grant["resourceKey"] == *key)
        });
    account =
        super::super::accounts::save(&service.store, &admin, actor.id, account).unwrap()["user"]
            .clone();
    for operation in [EXPORT_CRM_CUSTOMERS, EXPORT_SUPPLIERS] {
        let bytes = export(&service, &actor, operation, &[]).unwrap();
        let rows = export_doc_excel::read_table(&bytes, "导出.xlsx", 100, &|| Ok(())).unwrap();
        assert!(!rows[1].iter().any(|value| value.contains("联系人")
            || value == "contact@example.test"
            || value == "123456"));
    }
    account["expectedVersion"] = account["versionNumber"].clone();
    account["permissionGrants"] = json!([]);
    super::super::accounts::save(&service.store, &admin, actor.id, account).unwrap();
    assert_eq!(
        export(&service, &actor, EXPORT_CRM_CUSTOMERS, &[])
            .unwrap_err()
            .status,
        Some(403)
    );
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
