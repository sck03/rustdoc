use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};
#[path = "approval_fixture.rs"]
mod workflow;

fn request(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    query: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let body = body.map(|body| contracts::overlay(contracts::object(op.id, true), &body));
    let bytes = service.dispatch(op, &[("id", id.to_string())], query, body, token)?;
    let value = serde_json::from_slice(&bytes).unwrap();
    export_doc_contracts::validation::response(op.id, &value).unwrap();
    Ok(value)
}

pub fn exercise(service: &NativeService, admin: &str) {
    let call = |op: Operation, id, body| {
        request(service, admin, op, id, &[], Some(body))
            .unwrap_or_else(|cause| panic!("{}: {cause}", op.id))
    };
    let tag = nonce().unwrap();
    let customer = call(
        CREATE_CRM_CUSTOMER,
        0,
        json!({"name":format!("范围客户-{tag}")}),
    );
    let mut record = call(
        CREATE_SALES_OPPORTUNITY,
        0,
        json!({"crmCustomerId":customer["id"],"title":format!("范围商机-{tag}"),"currency":"USD","estimatedAmount":123.45}),
    );
    let id = record["id"].as_i64().unwrap();
    let scheme = workflow::publish_scheme(
        service,
        admin,
        call(
            CREATE_PERMISSION_TEMPLATE,
            0,
            json!({
                "code":format!("SCOPE-{tag}"),"name":"商机与客户独立范围","isActive":true,"grants":[
                    {"resourceKey":"sales.opportunities","action":"view","dataScope":"all"},
                    {"resourceKey":"sales.customers","action":"view","dataScope":"own"}
                ]
            }),
        ),
    );
    let current = request(service, admin, GET_CURRENT_USER, 0, &[], None).unwrap();
    let username = format!("history-{tag}");
    let account = call(
        CREATE_USER_ACCOUNT,
        0,
        json!({"username":username,"fullName":"范围验证","role":"User","isActive":true,
        "companyScope":current["companyScope"],"departmentId":current["departmentId"],"resetPassword":"Scope-Test-2026",
        "permissionTemplateId":scheme["id"],"permissionSchemeVersion":scheme["publishedVersion"]}),
    );
    call(
        SAVE_ACCESS_RESTRICTION,
        0,
        json!({"principalKind":"Account","principalId":account["user"]["id"].as_i64().unwrap().to_string(),
        "resourceKey":"sales.customers","action":"view","mode":"Deny","members":[],"reason":"验证关联客户显式限制","expectedVersion":0}),
    );
    let login = call(
        LOGIN,
        0,
        json!({"username":username,"password":"Scope-Test-2026"}),
    );
    let limited = login["accessToken"].as_str().unwrap();
    for op in [GET_SALES_OPPORTUNITY, LIST_SALES_OPPORTUNITY_HISTORY] {
        assert_eq!(
            request(service, limited, op, id, &[], None)
                .unwrap_err()
                .status,
            Some(403),
            "{} must enforce customer visibility",
            op.id
        );
    }
    let filter = [("keyword", format!("范围商机-{tag}"))];
    assert_eq!(
        request(
            service,
            limited,
            QUERY_SALES_OPPORTUNITIES,
            0,
            &filter,
            None
        )
        .unwrap()["totalCount"],
        0
    );
    for note in ["第一轮跟进", "第二轮跟进"] {
        let mut body = record.clone();
        body["changeNote"] = json!(note);
        body["expectedVersion"] = record["versionNumber"].clone();
        record = call(UPDATE_SALES_OPPORTUNITY, id, body);
    }
    let first = request(
        service,
        admin,
        LIST_SALES_OPPORTUNITY_HISTORY,
        id,
        &[("limit", "2".into())],
        None,
    )
    .unwrap();
    assert_eq!(first["items"].as_array().unwrap().len(), 2);
    let cursor = first["nextCursor"].as_i64().unwrap();
    call(
        ARCHIVE_SALES_OPPORTUNITY,
        id,
        json!({"expectedVersion":record["versionNumber"]}),
    );
    let next = request(
        service,
        admin,
        LIST_SALES_OPPORTUNITY_HISTORY,
        id,
        &[("limit", "2".into()), ("beforeId", cursor.to_string())],
        None,
    )
    .unwrap();
    assert_eq!(next["items"].as_array().unwrap().len(), 1);
    assert_eq!(next["items"][0]["changeType"], "创建");
    assert!(next["nextCursor"].is_null());
    assert_eq!(
        request(service, admin, QUERY_SALES_OPPORTUNITIES, 0, &filter, None).unwrap()["totalCount"],
        0
    );
    let archived_filter = [
        ("keyword", filter[0].1.clone()),
        ("includeArchived", "true".into()),
    ];
    let archived = request(
        service,
        admin,
        QUERY_SALES_OPPORTUNITIES,
        0,
        &archived_filter,
        None,
    )
    .unwrap();
    assert_eq!(archived["totalCount"], 1);
    assert_eq!(archived["items"][0]["isArchived"], true);
    let detail = request(service, admin, GET_SALES_OPPORTUNITY, id, &[], None).unwrap();
    assert_eq!(detail["isArchived"], true);
    assert_eq!(detail["allowedNextStages"], json!([]));
    for query in [
        vec![("beforeId", "0".into())],
        vec![("limit", "201".into())],
    ] {
        assert_eq!(
            request(
                service,
                admin,
                LIST_SALES_OPPORTUNITY_HISTORY,
                id,
                &query,
                None
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    #[cfg(feature = "excel")]
    invoice_feedback(service, admin, &tag, &current);
}

#[cfg(feature = "excel")]
fn invoice_feedback(service: &NativeService, admin: &str, tag: &str, current: &Value) {
    const CODE: &str = "9900987654";
    let year: i32 = current["businessDate"].as_str().unwrap()[..4]
        .parse()
        .unwrap();
    let preview = service
        .upload(
            PREVIEW_HS_CODES_IMPORT_UPLOAD,
            &[],
            json!({"mode":"Incremental","sourceName":"事务反馈税则","effectiveYear":year}),
            "tariff.csv",
            format!("HS编码,商品名称\n{CODE},事务专用匿名商品\n").as_bytes(),
            admin,
        )
        .unwrap();
    request(
        service,
        admin,
        COMMIT_HS_CODES_IMPORT,
        0,
        &[],
        Some(json!({"token":preview["token"]})),
    )
    .unwrap();
    let name = format!("反馈事务-{tag}");
    let feedback = json!({"queryText":name,"productName":name,"specification":"棉","candidateCode":CODE,"accepted":true});
    let mut invoice = export_doc_engine::invoice::InvoiceDraft::demo(
        current["businessDate"].as_str().unwrap(),
        &format!("FEEDBACK-{tag}"),
    )
    .build()
    .unwrap();
    invoice.items[0].hs_code = CODE.into();
    invoice.items[0].style_name_cn = name.clone();
    let mut body = serde_json::to_value(invoice).unwrap();
    body["pendingHsFeedback"] = json!([feedback, feedback, feedback]);
    let mut saved =
        request(service, admin, CREATE_INVOICE, 0, &[], Some(body.clone())).unwrap()["invoice"]
            .clone();
    let id = saved["id"].as_i64().unwrap();
    assert_eq!(saved["pendingHsFeedback"], json!([]));
    let examples = || {
        request(
            service,
            admin,
            LIST_HS_CODE_KNOWLEDGE_EXAMPLES,
            0,
            &[("keyword", name.clone())],
            None,
        )
        .unwrap()
    };
    assert_eq!(
        examples()["totalCount"],
        0,
        "duplicate entries in one save count once"
    );
    assert_eq!(
        request(service, admin, CREATE_INVOICE, 0, &[], Some(body))
            .unwrap_err()
            .status,
        Some(409)
    );
    let mut invalid = saved.clone();
    invalid["pendingHsFeedback"] =
        json!([feedback,{"queryText":name,"candidateCode":"INVALID","accepted":true}]);
    assert_eq!(
        request(service, admin, UPDATE_INVOICE, id, &[], Some(invalid))
            .unwrap_err()
            .status,
        Some(400)
    );
    assert_eq!(
        request(service, admin, GET_INVOICE, id, &[], None).unwrap()["rowVersion"],
        saved["rowVersion"]
    );
    for expected in [0, 1] {
        let mut update = saved.clone();
        update["pendingHsFeedback"] = json!([feedback]);
        saved = request(
            service,
            admin,
            UPDATE_INVOICE,
            id,
            &[],
            Some(update.clone()),
        )
        .unwrap()["invoice"]
            .clone();
        assert_eq!(
            examples()["totalCount"],
            expected,
            "failed/replayed transactions must not publish feedback"
        );
        assert_eq!(
            request(service, admin, UPDATE_INVOICE, id, &[], Some(update))
                .unwrap_err()
                .status,
            Some(409)
        );
    }
    assert_eq!(
        request(service, admin, GET_INVOICE, id, &[], None).unwrap()["pendingHsFeedback"],
        json!([])
    );
}
