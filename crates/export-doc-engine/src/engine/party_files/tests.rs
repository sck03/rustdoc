use super::*;

#[test]
fn invalid_rows_do_not_hide_valid_names_and_commit_rechecks_the_snapshot() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
    let actor = auth::current_actor(
        &service.store,
        service.store.all("users").unwrap()[0]["id"]
            .as_i64()
            .unwrap(),
    )
    .unwrap();
    for (preview_op, commit_op, created_key) in [
        (
            PREVIEW_CRM_CUSTOMER_IMPORT,
            IMPORT_CRM_CUSTOMERS,
            "createdCustomers",
        ),
        (
            PREVIEW_SUPPLIER_IMPORT,
            IMPORT_SUPPLIERS,
            "createdSuppliers",
        ),
    ] {
        let data = "公司名称,联系人,邮箱\nCafe\u{301},测试,invalid-email\nCAFÉ,测试,valid@example.test\ncafé,测试,valid@example.test\n其他单位,,\n";
        let preview = upload(&service, &actor, preview_op, "names.csv", data.as_bytes()).unwrap();
        assert_eq!(preview["validRows"], 2);
        assert_eq!(preview["duplicateRows"], 1);
        assert_eq!(preview["rows"][0]["isDuplicate"], false);
        assert_eq!(preview["rows"][1]["isDuplicate"], false);
        let resource = kind(supplier(commit_op));
        service
            .store
            .transaction(|tx| {
                store::save(
                    tx,
                    resource,
                    0,
                    json!({"name":"其他单位"}),
                    None,
                    &actor,
                    "create",
                )?;
                Ok(())
            })
            .unwrap();
        let result = import(
            &service,
            &actor,
            commit_op,
            &json!({"previewId":preview["previewId"]}),
        )
        .unwrap();
        assert_eq!(result[created_key], 1);
        assert_eq!(result["createdContacts"], 1);
        assert!(
            import(
                &service,
                &actor,
                commit_op,
                &json!({"previewId":preview["previewId"]})
            )
            .is_err()
        );
    }
}

#[test]
fn import_enforces_initial_status_and_independent_contact_permissions() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
    let admin = auth::current_actor(
        &service.store,
        service.store.all("users").unwrap()[0]["id"]
            .as_i64()
            .unwrap(),
    )
    .unwrap();
    let grants = json!([
        {"resourceKey":"sales.suppliers","action":"import","dataScope":"company"},
        {"resourceKey":"sales.suppliers","action":"view","dataScope":"company"},
        {"resourceKey":"sales.customers","action":"import","dataScope":"company"},
        {"resourceKey":"sales.customers","action":"view","dataScope":"company"}
    ]);
    let saved = super::super::accounts::save(&service.store, &admin, 0, json!({
        "username":"party-import", "fullName":"导入账号", "role":"User", "companyScope":"DEFAULT", "departmentId":"GENERAL", "isActive":true, "resetPassword":"Import-Test-2026", "permissionGrants":grants
    })).unwrap();
    let mut account = saved["user"].clone();
    let actor = auth::current_actor(&service.store, account["id"].as_i64().unwrap()).unwrap();
    for (preview_op, commit_op, supplier) in [
        (PREVIEW_SUPPLIER_IMPORT, IMPORT_SUPPLIERS, true),
        (PREVIEW_CRM_CUSTOMER_IMPORT, IMPORT_CRM_CUSTOMERS, false),
    ] {
        let preview = upload(
            &service,
            &actor,
            preview_op,
            "status.csv",
            "公司名称,状态\n禁止直接启用,合作中\n正常导入,\n".as_bytes(),
        )
        .unwrap();
        assert_eq!(preview["validRows"], 1);
        assert!(!text(&preview["rows"][0], "error").is_empty());
        assert_eq!(
            preview["rows"][1]["status"],
            party::initial_status(supplier)
        );
        import(
            &service,
            &actor,
            commit_op,
            &json!({"previewId":preview["previewId"]}),
        )
        .unwrap();
        let rows = service.store.all(kind(supplier)).unwrap();
        assert!(rows.iter().any(
            |row| row["name"] == "正常导入" && row["status"] == party::initial_status(supplier)
        ));
        assert!(!rows.iter().any(|row| row["name"] == "禁止直接启用"));
        assert_eq!(
            upload(
                &service,
                &actor,
                preview_op,
                "contact.csv",
                "公司名称,联系人\n关联联系人,测试\n".as_bytes()
            )
            .unwrap_err()
            .status,
            Some(403)
        );
    }
    // Creating a contact alone never grants the independent primary-contact action.
    let mut allowed = grants.as_array().unwrap().clone();
    allowed.push(
        json!({"resourceKey":"sales.supplier-contacts","action":"create","dataScope":"company"}),
    );
    account["permissionGrants"] = json!(allowed);
    account["expectedVersion"] = account["versionNumber"].clone();
    account =
        super::super::accounts::save(&service.store, &admin, actor.id, account).unwrap()["user"]
            .clone();
    let csv = "公司名称,联系人\n关联联系人,测试\n";
    assert_eq!(
        upload(
            &service,
            &actor,
            PREVIEW_SUPPLIER_IMPORT,
            "contact.csv",
            csv.as_bytes()
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    allowed.push(json!({"resourceKey":"sales.supplier-contacts","action":"set-primary","dataScope":"company"}));
    account["permissionGrants"] = json!(allowed);
    account["expectedVersion"] = account["versionNumber"].clone();
    account =
        super::super::accounts::save(&service.store, &admin, actor.id, account).unwrap()["user"]
            .clone();
    let preview = upload(
        &service,
        &actor,
        PREVIEW_SUPPLIER_IMPORT,
        "contact.csv",
        csv.as_bytes(),
    )
    .unwrap();
    account["permissionGrants"] = grants;
    account["expectedVersion"] = account["versionNumber"].clone();
    super::super::accounts::save(&service.store, &admin, actor.id, account).unwrap();
    assert_eq!(
        import(
            &service,
            &actor,
            IMPORT_SUPPLIERS,
            &json!({"previewId":preview["previewId"]})
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert!(
        !service
            .store
            .all("suppliers")
            .unwrap()
            .iter()
            .any(|row| row["name"] == "关联联系人")
    );
}

#[test]
fn exports_refresh_permissions_and_only_include_contacts_in_their_independent_scope() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
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
}
