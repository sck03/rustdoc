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
    let body = json!({"productId":product["id"],"supplierProductCode":"SUP-001","referencePrice":123.45,"currency":"CNY","leadTimeDays":30});
    let mut link = save(
        request,
        CREATE_SUPPLIER_PRODUCT_LINK,
        &supplier_path,
        body.clone(),
    );
    assert_eq!(link["productCode"], product["productCode"]);
    assert_eq!(link["status"], "供货中");
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
    let mut update = confirmed.clone();
    update["expectedVersion"] = confirmed["versionNumber"].clone();
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
