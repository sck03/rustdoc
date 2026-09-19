#[path = "support/business_contract.rs"]
mod business_contract;
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;

#[test]
fn customer_followups_supplier_links_and_assessments_share_the_original_contract() {
    let fixture = native_fixture::Fixture::new();
    business_contract::exercise(&|operation, path, query, body| {
        fixture.client().json(operation, path, query, body)
    });
}

#[test]
fn attachment_categories_rename_with_versions_and_reject_in_use_deletion() {
    use export_doc_engine::{generated_api::*, invoice::InvoiceDraft, paths::nonce};
    use serde_json::{Value, json};
    let fixture = native_fixture::Fixture::new();
    let category = fixture.create(
        CREATE_BUSINESS_ATTACHMENT_CATEGORY,
        json!({"companyScope":"DEFAULT","name":"往来确认"}),
    );
    let id = category["id"].as_i64().unwrap();
    let updated = fixture.request(
        UPDATE_BUSINESS_ATTACHMENT_CATEGORY,
        Some(id),
        Some(json!({"expectedVersion":category["versionNumber"],"name":"客户确认资料"})),
    );
    assert_eq!(updated["name"], "客户确认资料");
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                CREATE_BUSINESS_ATTACHMENT_CATEGORY,
                &[],
                &[],
                Some(json!({"companyScope":"DEFAULT","name":"客户确认资料"}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    fixture.create(
        CREATE_ORGANIZATION_COMPANY,
        json!({"code":"CAT-OTHER","name":"另一公司","isActive":true}),
    );
    fixture.create(
        CREATE_BUSINESS_ATTACHMENT_CATEGORY,
        json!({"companyScope":"CAT-OTHER","name":"客户确认资料"}),
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                UPDATE_BUSINESS_ATTACHMENT_CATEGORY,
                &[("id", id.to_string())],
                &[],
                Some(json!({"expectedVersion":category["versionNumber"],"name":"过时修改"}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let invoice = fixture.create(
        CREATE_INVOICE,
        json!(
            InvoiceDraft::demo("2026-09-17", "CATEGORY-REF")
                .build()
                .unwrap()
        ),
    );
    let invoice_id = invoice["invoice"]["id"].as_i64().unwrap();
    fixture
        .client()
        .upload(
            UPLOAD_BUSINESS_ATTACHMENT,
            &[("invoiceId", invoice_id.to_string())],
            json!({"title":"确认文件","categoryId":id,"uploadKey":nonce().unwrap()}),
            "confirmation.txt",
            b"approved content",
        )
        .unwrap();
    let catalog: Value = fixture
        .client()
        .json(
            LIST_BUSINESS_ATTACHMENT_CATEGORIES,
            &[],
            &[("invoiceId", invoice_id.to_string())],
            None,
        )
        .unwrap();
    assert_eq!(
        catalog["items"]
            .as_array()
            .unwrap()
            .iter()
            .find(|row| row["id"] == id)
            .unwrap()["attachmentCount"],
        1
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                DELETE_BUSINESS_ATTACHMENT_CATEGORY,
                &[("id", id.to_string())],
                &[("expectedVersion", updated["versionNumber"].to_string())],
                None
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let spare = fixture.create(
        CREATE_BUSINESS_ATTACHMENT_CATEGORY,
        json!({"companyScope":"DEFAULT","name":"未使用分类"}),
    );
    let deleted: Value = fixture
        .client()
        .json(
            DELETE_BUSINESS_ATTACHMENT_CATEGORY,
            &[("id", spare["id"].to_string())],
            &[("expectedVersion", spare["versionNumber"].to_string())],
            None,
        )
        .unwrap();
    assert_eq!(deleted["success"], true);
}

#[cfg(feature = "excel")]
#[test]
fn imports_use_single_use_server_previews_and_export_contacts_in_original_columns() {
    use export_doc_engine::{contracts, generated_api::*};
    use serde_json::{Value, json};
    let fixture = native_fixture::Fixture::new();
    let client = fixture.client();
    let data = "客户名称,国家/地区,网站,联系人,邮箱,备注\nCafé客户,CN,https://example.test,张经理,zhang@example.test,\"第一行\n第二行\"\nCafe\u{301}客户,CN,,,,\n无姓名联系人,,, ,wrong@example.test,\n邮箱无效,,,联系人,invalid,\n";
    let preview = client
        .upload(
            PREVIEW_CRM_CUSTOMER_IMPORT,
            &[],
            json!({}),
            "客户.csv",
            data.as_bytes(),
        )
        .unwrap();
    export_doc_contracts::validation::response(PREVIEW_CRM_CUSTOMER_IMPORT.id, &preview).unwrap();
    assert_eq!(preview["totalRows"], 4);
    assert_eq!(preview["validRows"], 1);
    assert_eq!(preview["duplicateRows"], 1);
    assert_eq!(
        fixture.request(QUERY_CRM_CUSTOMERS, None, None)["totalCount"],
        0
    );
    let imported = fixture.request(
        IMPORT_CRM_CUSTOMERS,
        None,
        Some(json!({"previewId":preview["previewId"],"rows":[{"name":"不能注入"}]})),
    );
    assert_eq!(imported["createdCustomers"], 1);
    assert_eq!(imported["createdContacts"], 1);
    assert_eq!(imported["skippedDuplicates"], 3);
    let audit: Value = client
        .json(
            LIST_AUDIT_LOGS,
            &[],
            &[
                ("entityName", "CrmCustomer".into()),
                ("action", "Added".into()),
            ],
            None,
        )
        .unwrap();
    assert_eq!(audit["totalCount"], 1);
    assert_eq!(
        client
            .json::<Value>(
                IMPORT_CRM_CUSTOMERS,
                &[],
                &[],
                Some(json!({"previewId":preview["previewId"]}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let exported = client
        .bytes(EXPORT_CRM_CUSTOMERS, &[], &[], None, 32 * 1024 * 1024)
        .unwrap();
    let rows = export_doc_excel::read_table(&exported, "客户.xlsx", 10000, &|| Ok(())).unwrap();
    assert_eq!(rows.len(), 2);
    assert_eq!(rows[0][6], "主要联系人");
    assert_eq!(rows[1][6], "张经理");
    assert_eq!(rows[1][5], "第一行\n第二行");
    let reimport = client
        .upload(
            PREVIEW_CRM_CUSTOMER_IMPORT,
            &[],
            json!({}),
            "客户.xlsx",
            &exported,
        )
        .unwrap();
    assert_eq!(reimport["rows"][0]["contactName"], "张经理");
    assert_eq!(reimport["duplicateRows"], 1);
    let supplier = client
        .upload(
            PREVIEW_SUPPLIER_IMPORT,
            &[],
            json!({}),
            "供应商.csv",
            "供应商名称,分类,主要产品,联系人,邮箱\n甲供应商,服装,外套,李经理,li@example.test"
                .as_bytes(),
        )
        .unwrap();
    export_doc_contracts::validation::response(PREVIEW_SUPPLIER_IMPORT.id, &supplier).unwrap();
    let result = fixture.request(
        IMPORT_SUPPLIERS,
        None,
        Some(json!({"previewId":supplier["previewId"]})),
    );
    assert_eq!(result["createdSuppliers"], 1);
    assert_eq!(result["createdContacts"], 1);
    let suppliers = fixture.request(QUERY_SUPPLIERS, None, None);
    let contacts: Value = client
        .json(
            QUERY_SUPPLIER_CONTACTS,
            &[("supplierId", suppliers["items"][0]["id"].to_string())],
            &[],
            None,
        )
        .unwrap();
    assert_eq!(contacts["items"][0]["name"], "李经理");
    assert_eq!(contacts["items"][0]["isPrimary"], true);
    let template = fixture.create(CREATE_PERMISSION_TEMPLATE,json!({"code":"IMPORT-ONLY","name":"导入业务","isActive":true,"grants":[{"resourceKey":"sales.customers","action":"import","dataScope":"company"}]}));
    fixture.create(CREATE_USER_ACCOUNT,json!({"username":"import-user","fullName":"导入业务","role":"User","permissionTemplateId":template["id"],"companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Import-Test-2026"}));
    let other = client
        .clone()
        .login("import-user".into(), "Import-Test-2026".into())
        .unwrap()
        .0;
    let body = contracts::overlay(
        contracts::object(IMPORT_CRM_CUSTOMERS.id, true),
        &json!({"previewId":reimport["previewId"]}),
    );
    assert_eq!(
        other
            .json::<Value>(IMPORT_CRM_CUSTOMERS, &[], &[], Some(body))
            .unwrap_err()
            .status,
        Some(404)
    );
}
