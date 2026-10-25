use super::*;

fn fixture() -> (std::path::PathBuf, Arc<NativeService>, store::Actor) {
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
    (root, service, admin)
}

#[test]
fn exact_product_lookup_is_independent_of_fuzzy_pages_and_keeps_visibility() {
    let (root, service, admin) = fixture();
    service
        .store
        .transaction(|tx| {
            for code in
                std::iter::once("ÉX-1".to_owned()).chain((0..51).map(|n| format!("A{n:02}-ÉX-1")))
            {
                let mut row = contracts::initial(contracts::schema("ApiProductDto"));
                row["productCode"] = json!(code);
                row["nameCN"] = json!("商品");
                store::save(
                    tx,
                    "products",
                    0,
                    row,
                    Some(store::normalize(&code)),
                    &admin,
                    "create",
                )?;
            }
            Ok(())
        })
        .unwrap();
    let resource = catalog::RESOURCES
        .iter()
        .find(|resource| resource.key == "products")
        .unwrap();
    let query = [
        ("productCode", " e\u{301}x-1 ".into()),
        ("pageSize", "1".into()),
    ];
    let exact =
        records::list(&service.store, &admin, resource, LIST_PRODUCTS, &query, &[]).unwrap();
    assert_eq!(exact["totalCount"], 1);
    assert_eq!(exact["items"][0]["productCode"], "ÉX-1");
    let account = accounts::save(
        &service.store,
        &admin,
        0,
        json!({
            "username":"product-reader", "fullName":"商品阅览员", "role":"User",
            "companyScope":"DEFAULT", "departmentId":"GENERAL", "isActive":true,
            "resetPassword":"Product-Test-2026", "permissionGrants":[
                {"resourceKey":"document.master-data","action":"view","dataScope":"own"}
            ]
        }),
    )
    .unwrap()["user"]
        .clone();
    let own_reader = auth::current_actor(&service.store, account["id"].as_i64().unwrap()).unwrap();
    assert!(
        own_reader
            .grants
            .iter()
            .any(|grant| grant["resourceKey"] == "common.product-reference"
                && grant["action"] == "view"
                && grant["dataScope"] == "own")
    );
    let hidden = records::list(
        &service.store,
        &own_reader,
        resource,
        LIST_PRODUCTS,
        &query,
        &[],
    )
    .unwrap();
    assert_eq!(hidden["totalCount"], 0);
    assert_eq!(hidden["items"], json!([]));
    let mut revoked = account;
    revoked["expectedVersion"] = revoked["versionNumber"].clone();
    revoked["permissionGrants"] = json!([]);
    accounts::save(&service.store, &admin, own_reader.id, revoked).unwrap();
    assert_eq!(
        records::list(
            &service.store,
            &own_reader,
            resource,
            LIST_PRODUCTS,
            &query,
            &[]
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn queued_writes_refresh_permissions_before_the_transaction() {
    let (root, service, admin) = fixture();
    let saved = accounts::save(&service.store, &admin, 0, json!({"username":"revoked","fullName":"待停用账号","role":"Admin","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Revocation-Test-2026"})).unwrap();
    let mut account = saved["user"].clone();
    let id = account["id"].as_i64().unwrap();
    let stale = auth::current_actor(&service.store, id).unwrap();
    account["expectedVersion"] = account["versionNumber"].clone();
    account["role"] = json!("OfficeEmployee");
    account = accounts::save(&service.store, &admin, id, account).unwrap()["user"].clone();
    for operation in [CREATE_HS_CODE, COMMIT_HS_CODE_CLEANUP] {
        assert_eq!(
            hs::handle(&service, &stale, operation, &[], &[], &json!({}))
                .unwrap_err()
                .status,
            Some(403)
        );
    }
    #[cfg(feature = "excel")]
    {
        for operation in [
            IMPORT_CRM_CUSTOMERS,
            IMPORT_SUPPLIERS,
            EXPORT_CRM_CUSTOMERS,
            EXPORT_SUPPLIERS,
        ] {
            assert_eq!(
                party_files::handle(&service, &stale, operation, &[], &json!({}))
                    .unwrap_err()
                    .status,
                Some(403)
            );
        }
        for operation in [PREVIEW_CRM_CUSTOMER_IMPORT, PREVIEW_SUPPLIER_IMPORT] {
            assert_eq!(
                party_files::upload(
                    &service,
                    &stale,
                    operation,
                    "party.csv",
                    "客户名称,供应商名称\n客户,供应商".as_bytes()
                )
                .unwrap_err()
                .status,
                Some(403)
            );
        }
    }
    assert_eq!(
        packing::handle(
            &service,
            &stale,
            SAVE_CONTAINER_PACKING_CONTAINER_TYPE,
            &[],
            &[],
            &json!({})
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    #[cfg(feature = "single-window")]
    assert_eq!(
        single_window::handle(
            &service,
            &stale,
            SAVE_CUSTOMS_COO_DOCUMENT,
            &[("invoiceId", "1".into())],
            &[],
            &json!({})
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    #[cfg(feature = "mail")]
    assert_eq!(email_templates::handle(&service,&stale,CREATE_EMAIL_TEMPLATE,&[],&[],&json!({"name":"撤权模板","subject":"不应保存","bodyHtml":"<p>正文</p>","expectedVersion":0})).unwrap_err().status,Some(403));
    let attempted = json!({"username":"unauthorized","fullName":"不应创建","role":"Admin","companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Revocation-Test-2026"});
    assert_eq!(
        accounts::save(&service.store, &stale, 0, attempted)
            .unwrap_err()
            .status,
        Some(403)
    );
    let called = std::cell::Cell::new(false);
    for operation in [CREATE_REPORT_TEMPLATE, CREATE_USER_REPORT_TEMPLATE] {
        let body = json!({"reportType":"ExportDocument","name":"撤权后不得新建","displayName":"撤权后不得新建","contentHtml":""});
        let result = if operation == CREATE_REPORT_TEMPLATE {
            report_template_files::handle(&service, &stale, operation, &[], &[], &body)
        } else {
            report_templates::handle(&service, &stale, operation, &[], &[], &body)
        };
        assert_eq!(result.unwrap_err().status, Some(403));
    }
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
    assert_eq!(
        custom_options::handle(
            &service.store,
            &stale,
            SAVE_CUSTOM_OPTION,
            &[("optionType", "PaymentMethod".into())],
            &json!({"value":"撤权后不得写入"})
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn existing_dangling_opportunity_product_links_fail_instead_of_returning_empty_product_data() {
    let (root, service, admin) = fixture();
    service
        .store
        .transaction(|tx| {
            let customer = store::save(
                tx,
                "crm-customers",
                0,
                json!({"name":"遗留关联客户"}),
                None,
                &admin,
                "create",
            )?;
            let mut row = contracts::initial(contracts::schema("ApiSalesOpportunityDto"));
            row["crmCustomerId"] = customer["id"].clone();
            row["productId"] = json!(i64::MAX);
            row["stage"] = json!("线索");
            row["expectedCloseDate"] = json!("2026-12-15");
            store::save(tx, "opportunities", 0, row, None, &admin, "create")?;
            Ok(())
        })
        .unwrap();
    assert_eq!(
        sales::handle(
            &service.store,
            &admin,
            QUERY_SALES_OPPORTUNITIES,
            &[],
            &[],
            &json!({})
        )
        .unwrap_err()
        .status,
        Some(404)
    );
    assert_eq!(
        crm_dashboard::query(&service.store, &admin, &service.clock)
            .unwrap_err()
            .status,
        Some(404)
    );
    service.close().unwrap();
    drop(service);
    std::fs::remove_dir_all(root).unwrap();
}
