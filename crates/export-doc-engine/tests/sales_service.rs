#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::{api::ApiClient, contracts, generated_api::*};
use serde_json::{Value, json};

fn call(client: &ApiClient, operation: Operation, id: i64, body: Value) -> Value {
    let value = client
        .json::<Value>(
            operation,
            &[("id", id.to_string())],
            &[],
            Some(contracts::overlay(
                contracts::object(operation.id, true),
                &body,
            )),
        )
        .unwrap();
    export_doc_contracts::validation::response(operation.id, &value).unwrap();
    value
}
fn failure(client: &ApiClient, operation: Operation, id: i64, body: Value, status: u16) {
    let cause = client
        .json::<Value>(
            operation,
            &[("id", id.to_string())],
            &[],
            Some(contracts::overlay(
                contracts::object(operation.id, true),
                &body,
            )),
        )
        .unwrap_err();
    assert_eq!(cause.status, Some(status), "{}: {cause}", operation.id);
}
fn request(record: &Value) -> Value {
    let mut value =
        contracts::overlay(contracts::object(UPDATE_SALES_OPPORTUNITY.id, true), record);
    value["expectedVersion"] = record["versionNumber"].clone();
    value
}

#[test]
fn quotes_transitions_nullable_fields_and_archive_keep_versioned_business_history() {
    let fixture = native_fixture::Fixture::new();
    let client = fixture.client();
    let customer = fixture.create(CREATE_CRM_CUSTOMER, json!({"name":"商机客户"}));
    let body = json!({"crmCustomerId":customer["id"], "title":"首批服装", "quotationNo":"Q-Café-2026", "estimatedAmount":123456789.1234, "currency":"usd", "probabilityPercent":40, "expectedCloseDate":"2026-12-15"});
    let mut saved = fixture.create(CREATE_SALES_OPPORTUNITY, body.clone());
    export_doc_contracts::validation::response(CREATE_SALES_OPPORTUNITY.id, &saved).unwrap();
    let id = saved["id"].as_i64().unwrap();
    assert_eq!(
        serde_json::from_value::<rust_decimal::Decimal>(saved["estimatedAmount"].clone())
            .unwrap()
            .to_string(),
        "123456789.1234"
    );
    assert_eq!(saved["customerName"], "商机客户");
    assert_eq!(saved["stage"], "线索");
    let mut duplicate = body.clone();
    duplicate["quotationNo"] = json!(" q-cafe\u{301}-2026 ");
    failure(client, CREATE_SALES_OPPORTUNITY, 0, duplicate, 409);
    failure(
        client,
        TRANSITION_SALES_OPPORTUNITY,
        id,
        json!({"nextStage":"已成交","expectedVersion":1}),
        400,
    );
    failure(
        client,
        TRANSITION_SALES_OPPORTUNITY,
        id,
        json!({"nextStage":"线索","expectedVersion":1}),
        409,
    );
    let mut update = request(&saved);
    update["expectedCloseDate"] = Value::Null;
    update["changeNote"] = json!("客户调整交付日期");
    saved = call(client, UPDATE_SALES_OPPORTUNITY, id, update.clone());
    assert!(saved["expectedCloseDate"].is_null());
    assert_eq!(saved["versionNumber"], 2);
    failure(client, UPDATE_SALES_OPPORTUNITY, id, update, 409);
    for stage in ["需求确认", "已报价", "谈判中", "已成交"] {
        saved = call(
            client,
            TRANSITION_SALES_OPPORTUNITY,
            id,
            json!({"nextStage":stage,"expectedVersion":saved["versionNumber"]}),
        );
    }
    failure(client, UPDATE_SALES_OPPORTUNITY, id, request(&saved), 409);
    saved = call(
        client,
        TRANSITION_SALES_OPPORTUNITY,
        id,
        json!({"nextStage":"谈判中","expectedVersion":saved["versionNumber"], "changeNote":"追加报价"}),
    );
    let archived = call(
        client,
        ARCHIVE_SALES_OPPORTUNITY,
        id,
        json!({"expectedVersion":saved["versionNumber"]}),
    );
    assert_eq!(archived["success"], true);
    let page: Value = client
        .json(QUERY_SALES_OPPORTUNITIES, &[], &[], None)
        .unwrap();
    assert_eq!(page["totalCount"], 0);
    assert_eq!(
        client
            .json::<Value>(GET_SALES_OPPORTUNITY, &[("id", id.to_string())], &[], None)
            .unwrap_err()
            .status,
        Some(404)
    );
    let history: Vec<ApiSalesOpportunityHistoryDto> = client
        .json(
            LIST_SALES_OPPORTUNITY_HISTORY,
            &[("id", id.to_string())],
            &[],
            None,
        )
        .unwrap();
    assert_eq!(history.len(), 8);
    assert_eq!(history[0].change_type, "归档");
    assert_eq!(history[0].version_number, 8);
    assert_eq!(history.last().unwrap().change_type, "创建");
    assert_eq!(
        history.last().unwrap().estimated_amount.to_string(),
        "123456789.1234"
    );
    assert!(history.iter().all(|event| !event.changed_by.is_empty()
        && chrono::DateTime::parse_from_rfc3339(&event.created_at).is_ok()));
}

#[test]
fn opportunity_edit_permissions_do_not_grant_quote_writes() {
    let fixture = native_fixture::Fixture::new();
    let customer = fixture.create(CREATE_CRM_CUSTOMER, json!({"name":"权限客户"}));
    let saved = fixture.create(CREATE_SALES_OPPORTUNITY, json!({"crmCustomerId":customer["id"],"title":"权限商机","currency":"USD","estimatedAmount":125.75,"probabilityPercent":20}));
    let template = fixture.create(
        CREATE_PERMISSION_TEMPLATE,
        json!({"code":"OPP-EDIT","name":"商机维护","isActive":true,
        "grants":[{"resourceKey":"sales.opportunities","action":"view","dataScope":"all"},
        {"resourceKey":"sales.opportunities","action":"edit","dataScope":"all"},
        {"resourceKey":"sales.opportunities","action":"create","dataScope":"all"}]}),
    );
    fixture.create(CREATE_USER_ACCOUNT, json!({"username":"opportunity-editor","fullName":"商机维护","role":"User","permissionTemplateId":template["id"],"companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Sales-Test-2026"}));
    let client = fixture
        .client()
        .clone()
        .login("opportunity-editor".into(), "Sales-Test-2026".into())
        .unwrap()
        .0;
    let id = saved["id"].as_i64().unwrap();
    let mut update = request(&saved);
    update["estimatedAmount"] = json!(999);
    failure(&client, UPDATE_SALES_OPPORTUNITY, id, update.clone(), 403);
    update["estimatedAmount"] = saved["estimatedAmount"].clone();
    update["nextAction"] = json!("联系客户确认规格");
    let edited = call(&client, UPDATE_SALES_OPPORTUNITY, id, update);
    assert_eq!(edited["estimatedAmount"], saved["estimatedAmount"]);
    assert_eq!(edited["nextAction"], "联系客户确认规格");
    failure(
        &client,
        CREATE_SALES_OPPORTUNITY,
        0,
        json!({"crmCustomerId":customer["id"],"title":"越权报价","currency":"USD","estimatedAmount":1}),
        403,
    );
    let created = call(
        &client,
        CREATE_SALES_OPPORTUNITY,
        0,
        json!({"crmCustomerId":customer["id"],"title":"普通线索","currency":"USD"}),
    );
    assert_eq!(created["stage"], "线索");
}
