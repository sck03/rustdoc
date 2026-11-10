//! Custom definitions use the same public services on SQLite and PostgreSQL.
use super::oa_contract::{call, operation, workflow as w};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, admin: &str) {
    let (reviewer_id, reviewer) = w::reviewer(service, admin);
    let person = w::request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(json!({
            "requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":"GENERAL",
            "jobTitle":"样品业务","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":"自定义事项申请人"}
        })),
    );
    let key = format!("general:sample_{}", &nonce().unwrap()[..8]);
    let mut rule = w::rule(
        "general",
        key.strip_prefix("general:").unwrap(),
        json!([w::node("review", &[], "Named", json!([reviewer_id]))]),
    );
    rule["name"] = json!("样品寄送");
    rule["form"] = json!({"category":"外贸业务","departmentIds":["GENERAL"],"attachmentRequired":false,"fields":[
        {"key":"customer","label":"客户","kind":"Text","required":true},
        {"key":"freight","label":"运费","kind":"Money","currency":"CNY","required":true},
        {"key":"samples","label":"样品明细","kind":"Table","required":true,"columns":[
            {"key":"name","label":"品名","kind":"Text","required":true},
            {"key":"quantity","label":"数量","kind":"Decimal","required":true}
        ]}
    ]});
    let patch = |rule: &Value, revision: i64| json!({"section":"rules","definitionKey":rule["key"],"rule":rule,"expectedRevision":revision});
    let saved = w::request(
        service,
        admin,
        SAVE_OA_APPROVAL_SETTINGS,
        Some(patch(&rule, 0)),
    );
    let revision = saved["revisions"][format!("rule:{key}")].as_i64().unwrap();
    assert_eq!(
        w::call(
            service,
            admin,
            SAVE_OA_APPROVAL_SETTINGS,
            0,
            Some(patch(&rule, 0))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    // A separate definition can be edited using its own initial revision.
    let mut other = rule.clone();
    other["key"] = json!(format!("{key}_other"));
    other["variant"] = json!(format!("{}_other", rule["variant"].as_str().unwrap()));
    let saved = w::request(
        service,
        admin,
        SAVE_OA_APPROVAL_SETTINGS,
        Some(patch(&other, 0)),
    );
    assert_eq!(saved["revisions"][format!("rule:{key}")], revision);
    let other_revision =
        saved["revisions"][format!("rule:{}", other["key"].as_str().unwrap())].clone();
    w::request(
        service,
        admin,
        SAVE_OA_APPROVAL_SETTINGS,
        Some(
            json!({"section":"rules","definitionKey":other["key"],"remove":true,"expectedRevision":other_revision}),
        ),
    );
    assert_eq!(
        w::call(
            service,
            admin,
            SAVE_OA_APPROVAL_SETTINGS,
            0,
            Some(patch(&other, 0))
        )
        .unwrap_err()
        .status,
        Some(409),
        "deleted keys cannot reset optimistic concurrency"
    );
    let values = json!({"customer":"匿名外贸客户","freight":"125.50","samples":[{"name":"样品 A","quantity":"2"}]});
    let sample = json!({"employeeId":person["employee"]["id"],"appointmentId":person["organization"]["primaryAssignmentId"],"formValues":values,"category":"Other"});
    let mut simulation = sample.clone();
    simulation["definition"] = rule.clone();
    w::request(service, admin, SIMULATE_OA_DEFINITION, Some(simulation));
    let saved = w::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    let publication = json!({"expectedVersion":saved["versionNumber"],"expectedRevision":revision,"definitionKey":key,"samples":[sample],"commandKey":nonce().unwrap()});
    let saved = w::request(service, admin, PUBLISH_OA_DEFINITION, Some(publication));
    let revision = saved["revisions"][format!("rule:{key}")].as_i64().unwrap();
    let catalog = w::request(service, admin, LIST_OA_APPLICATIONS, None);
    let application = catalog["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|i| i["key"] == key)
        .unwrap();
    assert_eq!(application["name"], "样品寄送");
    let create = |values: Value| json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"展会样品寄送","reason":"客户确认样品","category":"Other","definitionKey":key,"definitionVersion":application["version"],"formValues":values});
    let incomplete = call(
        service,
        admin,
        operation("general", "create"),
        0,
        Some(create(json!({}))),
    )
    .unwrap();
    assert_eq!(
        call(
            service,
            admin,
            operation("general", "submit"),
            incomplete["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":incomplete["versionNumber"],"note":"缺少必填字段"}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let mut invalid = values.clone();
    invalid["freight"] = json!("125.501");
    assert_eq!(
        call(
            service,
            admin,
            operation("general", "create"),
            0,
            Some(create(invalid))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let draft = call(
        service,
        admin,
        operation("general", "create"),
        0,
        Some(create(values.clone())),
    )
    .unwrap();
    assert_eq!(draft["formDefinition"]["fields"][0]["label"], "客户");
    let id = draft["id"].as_i64().unwrap();
    let pending = call(
        service,
        admin,
        operation("general", "submit"),
        id,
        Some(json!({"expectedVersion":draft["versionNumber"],"note":"提交样品申请"})),
    )
    .unwrap();
    assert_eq!(pending["approvalPlan"]["definitionKey"], key);
    let page = w::query(
        service,
        admin,
        operation("general", "list"),
        &[],
        &[("definitionKey", key.clone())],
        None,
    )
    .unwrap();
    assert_eq!(page["totalCount"], 2);
    assert!(
        page["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|r| r["definitionKey"] == key)
    );
    // Changing the next draft never rewrites existing submitted forms or tasks.
    rule["form"]["fields"][0]["label"] = json!("客户名称");
    let changed = w::request(
        service,
        admin,
        SAVE_OA_APPROVAL_SETTINGS,
        Some(patch(&rule, revision)),
    );
    let stale = json!({"expectedVersion":saved["versionNumber"],"expectedRevision":revision,"definitionKey":key,"samples":[sample],"commandKey":nonce().unwrap()});
    assert_eq!(
        w::call(service, admin, PUBLISH_OA_DEFINITION, 0, Some(stale))
            .unwrap_err()
            .status,
        Some(409)
    );
    let approved = call(
        service,
        &reviewer,
        operation("general", "approve"),
        id,
        Some(json!({"expectedVersion":pending["versionNumber"],"note":"样品与运费核对通过"})),
    )
    .unwrap();
    assert_eq!(approved["status"], "Approved");
    assert_eq!(approved["formDefinition"]["fields"][0]["label"], "客户");
    assert_eq!(approved["formValues"], values);
    let parameters = [
        ("id", id.to_string()),
        ("submissionId", approved["submissionId"].to_string()),
    ];
    let snapshot = w::query(
        service,
        admin,
        operation("general", "submission"),
        &parameters,
        &[],
        None,
    )
    .unwrap();
    assert_eq!(snapshot["request"]["formValues"], values);
    w::request(
        service,
        admin,
        DISABLE_OA_DEFINITION,
        Some(
            json!({"definitionKey":key,"expectedVersion":changed["versionNumber"],"expectedRevision":changed["revisions"][format!("rule:{key}")]}),
        ),
    );
    assert!(
        !w::request(service, admin, LIST_OA_APPLICATIONS, None)["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|i| i["key"] == key)
    );
    assert_eq!(
        call(
            service,
            admin,
            operation("general", "create"),
            0,
            Some(create(values))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
}
