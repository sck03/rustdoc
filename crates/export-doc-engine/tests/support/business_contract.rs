use export_doc_engine::{api::ApiError, contracts, generated_api::*, paths::nonce};
use serde_json::{Value, json};
pub type Request<'a> = dyn Fn(Operation, &[(&str, String)], &[(&str, String)], Option<Value>) -> Result<Value, ApiError>
    + 'a;
fn read(
    request: &Request<'_>,
    operation: Operation,
    path: &[(&str, String)],
    query: &[(&str, String)],
    body: Option<Value>,
) -> Value {
    let value = request(operation, path, query, body)
        .unwrap_or_else(|cause| panic!("{}: {cause}", operation.id));
    export_doc_contracts::validation::response(operation.id, &value).unwrap();
    value
}
fn save(
    request: &Request<'_>,
    operation: Operation,
    path: &[(&str, String)],
    body: Value,
) -> Value {
    read(
        request,
        operation,
        path,
        &[],
        Some(contracts::overlay(
            contracts::object(operation.id, true),
            &body,
        )),
    )
}
fn version(row: &Value) -> Value {
    json!({"expectedVersion":row["versionNumber"]})
}

/// Provider-independent regressions for the complete customer/supplier workflow.
pub fn exercise(request: &Request<'_>) {
    invoice_status_history(request);
    let option_type = [("optionType", "PaymentPayerName".into())];
    let option = format!("payer-{}", nonce().unwrap());
    for value in [option.clone(), option.to_uppercase()] {
        save(
            request,
            SAVE_CUSTOM_OPTION,
            &option_type,
            json!({"value":value}),
        );
    }
    let options = read(request, LIST_CUSTOM_OPTIONS, &option_type, &[], None);
    assert_eq!(
        options["customOptions"]
            .as_array()
            .unwrap()
            .iter()
            .filter(|value| **value == option)
            .count(),
        1
    );
    let currencies = read(
        request,
        LIST_CUSTOM_OPTIONS,
        &[("optionType", "Currency".into())],
        &[],
        None,
    );
    assert!(
        !currencies["customOptions"]
            .as_array()
            .unwrap()
            .iter()
            .any(|value| *value == option)
    );
    for (create, update, get, delete, fields) in [
        (
            CREATE_CUSTOMER,
            UPDATE_CUSTOMER,
            GET_CUSTOMER,
            DELETE_CUSTOMER,
            json!({"displayName":"版本客户","customerNameEN":"Version customer"}),
        ),
        (
            CREATE_EXPORTER,
            UPDATE_EXPORTER,
            GET_EXPORTER,
            DELETE_EXPORTER,
            json!({"exporterNameCN":"版本出口商","exporterNameEN":"Version exporter"}),
        ),
        (
            CREATE_PAYEE,
            UPDATE_PAYEE,
            GET_PAYEE,
            DELETE_PAYEE,
            json!({"name":"Version payee"}),
        ),
        (
            CREATE_PRODUCT,
            UPDATE_PRODUCT,
            GET_PRODUCT,
            DELETE_PRODUCT,
            json!({"productCode":nonce().unwrap(),"nameCN":"版本商品","nameEN":"Version product"}),
        ),
        (
            CREATE_PORT,
            UPDATE_PORT,
            GET_PORT,
            DELETE_PORT,
            json!({"nameCN":"版本港口","nameEN":"Version port"}),
        ),
        (
            CREATE_UNIT,
            UPDATE_UNIT,
            GET_UNIT,
            DELETE_UNIT,
            json!({"nameCN":"版本单位","nameEN":"Version unit"}),
        ),
    ] {
        let row = save(request, create, &[], fields);
        let path = [("id", row["id"].to_string())];
        let wrong_id = row["id"].as_i64().unwrap() + 1;
        for supplied in [
            json!(wrong_id),
            json!(wrong_id.to_string()),
            json!(-1),
            json!("invalid"),
        ] {
            let mut wrong_record = row.clone();
            wrong_record["id"] = supplied;
            assert_eq!(
                request(update, &path, &[], Some(wrong_record))
                    .unwrap_err()
                    .status,
                Some(400)
            );
        }
        let mut matching_identity = row.clone();
        matching_identity["id"] = json!(row["id"].to_string());
        let saved = save(request, update, &path, matching_identity);
        let stale = request(update, &path, &[], Some(row.clone())).unwrap_err();
        assert_eq!(
            stale.code,
            Some(export_doc_engine::api::ErrorCode::ConcurrencyConflict)
        );
        assert!(matches!(
            request(delete, &path, &[], None).unwrap_err().status,
            Some(400 | 409)
        ));
        assert_eq!(
            request(
                delete,
                &path,
                &[("rowVersion", row["rowVersion"].as_str().unwrap().into())],
                None
            )
            .unwrap_err()
            .status,
            Some(409)
        );
        assert_eq!(
            read(request, get, &path, &[], None)["rowVersion"],
            saved["rowVersion"]
        );
        read(
            request,
            delete,
            &path,
            &[("rowVersion", saved["rowVersion"].as_str().unwrap().into())],
            None,
        );
        assert_eq!(
            request(get, &path, &[], None).unwrap_err().status,
            Some(404)
        );
    }
    let suffix = nonce().unwrap();
    let customer = save(
        request,
        CREATE_CRM_CUSTOMER,
        &[],
        json!({"name":format!("客户-{suffix}")}),
    );
    let customer_id = customer["id"].to_string();
    let target = save(
        request,
        CREATE_CRM_CUSTOMER,
        &[],
        json!({"name":format!("目标客户-{suffix}")}),
    );
    // Names are searchable directory data, not a global cross-company identity.
    save(
        request,
        CREATE_CRM_CUSTOMER,
        &[],
        json!({"name":customer["name"]}),
    );
    let contact = save(
        request,
        CREATE_CRM_CONTACT,
        &[("customerId", customer_id.clone())],
        json!({"name":"王经理","email":"wang@example.test"}),
    );
    let path = vec![
        ("customerId", customer_id.clone()),
        ("id", contact["id"].to_string()),
    ];
    let primary = save(request, SET_PRIMARY_CRM_CONTACT, &path, version(&contact));
    assert_eq!(primary["isPrimary"], true);
    let next_contact = save(
        request,
        CREATE_CRM_CONTACT,
        &[("customerId", customer_id.clone())],
        json!({"name":"李经理"}),
    );
    save(
        request,
        SET_PRIMARY_CRM_CONTACT,
        &[
            ("customerId", customer_id.clone()),
            ("id", next_contact["id"].to_string()),
        ],
        version(&next_contact),
    );
    let contacts = read(
        request,
        QUERY_CRM_CONTACTS,
        &[("customerId", customer_id.clone())],
        &[],
        None,
    );
    assert_eq!(
        contacts["items"]
            .as_array()
            .unwrap()
            .iter()
            .filter(|row| row["isPrimary"] == true)
            .count(),
        1
    );
    let mut update = primary.clone();
    update["expectedVersion"] = primary["versionNumber"].clone();
    update["name"] = json!("跨客户覆盖");
    assert_eq!(
        request(
            UPDATE_CRM_CONTACT,
            &[
                ("customerId", target["id"].to_string()),
                ("id", contact["id"].to_string())
            ],
            &[],
            Some(update)
        )
        .unwrap_err()
        .status,
        Some(404)
    );
    let now = chrono::Utc::now();
    let mut follow_up = save(
        request,
        CREATE_CRM_FOLLOW_UP,
        &[],
        json!({"crmCustomerId":customer["id"],"crmContactId":contact["id"],"summary":"确认报价规格","type":"电话","nextAction":"回访","followedUpAt":now.to_rfc3339(),"nextFollowUpAt":(now-chrono::Duration::hours(1)).to_rfc3339()}),
    );
    let follow_up_path = vec![("id", follow_up["id"].to_string())];
    let dashboard = read(request, GET_CRM_DASHBOARD, &[], &[], None);
    let pending = dashboard["pendingFollowUpCount"].as_i64().unwrap();
    let overdue = dashboard["overdueFollowUpCount"].as_i64().unwrap();
    assert!(pending > 0 && overdue > 0);
    follow_up = save(
        request,
        COMPLETE_CRM_FOLLOW_UP,
        &follow_up_path,
        version(&follow_up),
    );
    assert_eq!(follow_up["isCompleted"], true);
    let page = read(
        request,
        QUERY_CRM_FOLLOW_UPS,
        &[],
        &[("crmCustomerId", customer_id.clone())],
        None,
    );
    assert_eq!(page["totalCount"], 0);
    let dashboard = read(request, GET_CRM_DASHBOARD, &[], &[], None);
    assert_eq!(dashboard["pendingFollowUpCount"], pending - 1);
    assert_eq!(dashboard["overdueFollowUpCount"], overdue - 1);
    assert_eq!(
        request(
            COMPLETE_CRM_FOLLOW_UP,
            &follow_up_path,
            &[],
            Some(version(&follow_up))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    follow_up = save(
        request,
        RESTORE_CRM_FOLLOW_UP,
        &follow_up_path,
        version(&follow_up),
    );
    let mut body = follow_up.clone();
    body["expectedVersion"] = follow_up["versionNumber"].clone();
    body["crmCustomerId"] = target["id"].clone();
    assert_eq!(
        request(UPDATE_CRM_FOLLOW_UP, &follow_up_path, &[], Some(body))
            .unwrap_err()
            .status,
        Some(400)
    );
    follow_up = save(
        request,
        TRANSFER_CRM_FOLLOW_UP,
        &follow_up_path,
        json!({"crmCustomerId":target["id"],"crmContactId":null,"expectedVersion":follow_up["versionNumber"]}),
    );
    assert_eq!(follow_up["customerName"], target["name"]);
    let mut body = follow_up.clone();
    body["expectedVersion"] = follow_up["versionNumber"].clone();
    body["nextFollowUpAt"] = Value::Null;
    follow_up = save(request, UPDATE_CRM_FOLLOW_UP, &follow_up_path, body);
    assert!(follow_up["nextFollowUpAt"].is_null());
    let supplier = save(
        request,
        CREATE_SUPPLIER,
        &[],
        json!({"name":format!("供应商-{suffix}"),"category":"服装"}),
    );
    let supplier_path = vec![("supplierId", supplier["id"].to_string())];
    let contact = save(
        request,
        CREATE_SUPPLIER_CONTACT,
        &supplier_path,
        json!({"name":"供应业务"}),
    );
    assert_eq!(contact["supplierCompanyId"], supplier["id"]);
    let mut path = supplier_path.clone();
    path.push(("id", contact["id"].to_string()));
    let contact = save(
        request,
        UPDATE_SUPPLIER_CONTACT,
        &path,
        json!({"name":"供应业务更新","expectedVersion":contact["versionNumber"]}),
    );
    assert_eq!(contact["name"], "供应业务更新");
    save(
        request,
        SET_PRIMARY_SUPPLIER_CONTACT,
        &path,
        version(&contact),
    );
    let product = save(
        request,
        CREATE_PRODUCT,
        &[],
        json!({"productCode":format!("P-{suffix}"),"nameCN":"棉外套","nameEN":"Cotton Coat"}),
    );
    let body = json!({"productId":product["id"],"supplierProductCode":"SUP-001","referencePrice":"9007199254740993.0100","currency":"CNY","leadTimeDays":30});
    let mut link = save(
        request,
        CREATE_SUPPLIER_PRODUCT_LINK,
        &supplier_path,
        body.clone(),
    );
    assert_eq!(link["productCode"], product["productCode"]);
    assert_eq!(link["status"], "供货中");
    assert_eq!(link["referencePrice"], "9007199254740993.01");
    for (field, invalid) in [
        ("id", json!(link["id"])),
        (
            "supplierCompanyId",
            json!(supplier["id"].as_i64().unwrap() + 1),
        ),
        ("productId", json!(product["id"].to_string())),
        ("expectedVersion", json!("0")),
        ("referencePrice", json!("+1")),
        ("referencePrice", json!("01")),
        ("referencePrice", json!("1_0")),
        ("unexpected", json!(true)),
    ] {
        let mut invalid_body = contracts::overlay(
            contracts::object(CREATE_SUPPLIER_PRODUCT_LINK.id, true),
            &body,
        );
        invalid_body[field] = invalid;
        assert_eq!(
            request(
                CREATE_SUPPLIER_PRODUCT_LINK,
                &supplier_path,
                &[],
                Some(invalid_body)
            )
            .unwrap_err()
            .status,
            Some(400),
            "{field}"
        );
    }
    assert_eq!(
        request(
            CREATE_SUPPLIER_PRODUCT_LINK,
            &supplier_path,
            &[],
            Some(contracts::overlay(
                contracts::object(CREATE_SUPPLIER_PRODUCT_LINK.id, true),
                &body
            ))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let mut path = supplier_path.clone();
    path.push(("id", link["id"].to_string()));
    link = save(
        request,
        DEACTIVATE_SUPPLIER_PRODUCT_LINK,
        &path,
        version(&link),
    );
    link = save(
        request,
        RESTORE_SUPPLIER_PRODUCT_LINK,
        &path,
        version(&link),
    );
    assert_eq!(link["status"], "供货中");
    let today = now.date_naive().to_string();
    let assessment = save(
        request,
        CREATE_SUPPLIER_ASSESSMENT,
        &supplier_path,
        json!({"assessmentDate":today,"assessmentKind":"定期评价","qualityScore":5,"deliveryScore":4,"serviceScore":3,"priceScore":2,"conclusion":"观察","notes":"复核交期"}),
    );
    assert_eq!(
        serde_json::from_value::<rust_decimal::Decimal>(assessment["averageScore"].clone())
            .unwrap(),
        "3.5".parse::<rust_decimal::Decimal>().unwrap()
    );
    let before = read(request, GET_SUPPLIER_ASSESSMENT_OVERVIEW, &[], &[], None);
    assert!(
        before["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|row| row["supplierCompanyId"] != supplier["id"])
    );
    let mut path = supplier_path.clone();
    path.push(("id", assessment["id"].to_string()));
    let confirmed = read(
        request,
        CONFIRM_SUPPLIER_ASSESSMENT,
        &path,
        &[("expectedVersion", assessment["versionNumber"].to_string())],
        None,
    );
    assert_eq!(confirmed["status"], "Confirmed");
    let overview = read(request, GET_SUPPLIER_ASSESSMENT_OVERVIEW, &[], &[], None);
    let latest = overview["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["supplierCompanyId"] == supplier["id"])
        .unwrap();
    assert_eq!(latest["conclusion"], "观察");
    assert_eq!(latest["assessmentCount"], 1);
    for _ in 0..13 {
        save(
            request,
            CREATE_SUPPLIER_ASSESSMENT,
            &supplier_path,
            json!({
                "assessmentDate":today,"assessmentKind":"定期评价","qualityScore":4,
                "deliveryScore":4,"serviceScore":4,"priceScore":4,"conclusion":"合格","notes":"分页历史记录"
            }),
        );
    }
    let page = read(
        request,
        LIST_SUPPLIER_ASSESSMENTS,
        &supplier_path,
        &[("pageNumber", "2".into()), ("pageSize", "7".into())],
        None,
    );
    assert_eq!(page["totalCount"], 14);
    assert_eq!(page["items"].as_array().unwrap().len(), 7);
    let analytics = read(
        request,
        GET_SUPPLIER_ASSESSMENT_ANALYTICS,
        &supplier_path,
        &[],
        None,
    );
    assert_eq!(analytics["totalCount"], 14);
    assert_eq!(analytics["qualityTotal"], 57);
    assert_eq!(analytics["qualifiedCount"], 13);
    assert_eq!(analytics["watchCount"], 1);
    assert_eq!(analytics["recent"].as_array().unwrap().len(), 12);
    assert_eq!(
        analytics["recent"][0]["id"],
        read(
            request,
            LIST_SUPPLIER_ASSESSMENTS,
            &supplier_path,
            &[("pageSize", "1".into())],
            None
        )["items"][0]["id"]
    );
    let mut update = confirmed.clone();
    update["expectedVersion"] = confirmed["versionNumber"].clone();
    let mut invalid_version = update.clone();
    invalid_version["expectedVersion"] = json!(confirmed["versionNumber"].to_string());
    assert_eq!(
        request(
            UPDATE_SUPPLIER_ASSESSMENT,
            &path,
            &[],
            Some(invalid_version)
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    assert_eq!(
        request(UPDATE_SUPPLIER_ASSESSMENT, &path, &[], Some(update))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        request(
            DELETE_SUPPLIER,
            &[("id", supplier["id"].to_string())],
            &[("expectedVersion", supplier["versionNumber"].to_string())],
            None
        )
        .unwrap_err()
        .status,
        Some(409)
    );
}

fn invoice_status_history(request: &Request<'_>) {
    let mut empty = export_doc_engine::invoice::InvoiceDraft::demo("2026-11-18", &nonce().unwrap());
    empty.rows.clear();
    let empty = save(
        request,
        CREATE_INVOICE,
        &[],
        serde_json::to_value(empty.build().unwrap()).unwrap(),
    )["invoice"]
        .clone();
    assert_eq!(
        read(request, REVIEW_INVOICE, &[], &[], Some(empty.clone()))["ready"],
        false
    );
    let path = [("id", empty["id"].to_string())];
    assert_eq!(request(TRANSITION_INVOICE_STATUS, &path, &[],
        Some(json!({"targetStatus":"Verified","rowVersion":empty["rowVersion"],"note":"空明细不能核对"}))).unwrap_err().status, Some(400));
    assert_eq!(
        read(request, GET_INVOICE, &path, &[], None)["status"],
        "Draft"
    );
    read(
        request,
        DELETE_INVOICE,
        &path,
        &[("rowVersion", empty["rowVersion"].as_str().unwrap().into())],
        None,
    );
    let draft = export_doc_engine::invoice::InvoiceDraft::demo("2026-11-15", &nonce().unwrap())
        .build()
        .unwrap();
    let created = save(request, CREATE_INVOICE, &[], json!(draft));
    let path = [("id", created["id"].to_string())];
    let mut invoice = created["invoice"].clone();
    let mut last_before = Value::Null;
    for (operation, target, reason) in [
        (TRANSITION_INVOICE_STATUS, "Verified", "核对通过"),
        (UNVERIFY_INVOICE, "Draft", "修正商品资料"),
        (TRANSITION_INVOICE_STATUS, "Cancelled", "客户取消订单"),
    ] {
        let old_version = invoice["rowVersion"].clone();
        let body = json!({"targetStatus":target,"rowVersion":old_version,"note":reason});
        let saved = read(request, operation, &path, &[], Some(body.clone()));
        invoice = saved["invoice"].clone();
        let history = read(
            request,
            LIST_INVOICE_STATUS_HISTORY,
            &path,
            &[("limit", "1".into())],
            None,
        );
        assert_eq!(history["items"][0]["toStatus"], target);
        assert_eq!(history["items"][0]["note"], reason);
        assert!(
            !history["items"][0]["changedByUsername"]
                .as_str()
                .unwrap()
                .is_empty()
        );
        chrono::DateTime::parse_from_rfc3339(history["items"][0]["changedAt"].as_str().unwrap())
            .unwrap();
        assert_eq!(
            request(operation, &path, &[], Some(body))
                .unwrap_err()
                .status,
            Some(409)
        );
        last_before = history["nextCursor"].clone();
    }
    let history = read(
        request,
        LIST_INVOICE_STATUS_HISTORY,
        &path,
        &[("limit", "2".into()), ("beforeId", last_before.to_string())],
        None,
    );
    assert_eq!(history["items"].as_array().unwrap().len(), 2);
    assert_eq!(history["items"][0]["toStatus"], "Draft");
    assert_eq!(history["items"][1]["fromStatus"], "Draft");
    assert!(history["nextCursor"].is_null());
    assert_eq!(
        request(
            LIST_INVOICE_STATUS_HISTORY,
            &path,
            &[("limit", "201".into())],
            None
        )
        .unwrap_err()
        .status,
        Some(400)
    );
}
