#[path = "support/native_fixture.rs"]
mod native_fixture;
#[path = "support/approval_fixture.rs"]
mod workflow;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn exercise(service: &NativeService, admin: &str) {
    let (reviewer, reviewer_token) = workflow::reviewer(service, admin);
    let (_, token) = workflow::reviewer(service, admin);
    let current = workflow::request(service, &token, GET_CURRENT_USER, None);
    let person = workflow::request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":current["departmentId"],"jobTitle":"发布申请人","hireDate":"2026-01-01","employmentType":"FullTime","profile":{"fullName":"批量发布申请人"}}),
        ),
    );
    let directory = workflow::query(
        service,
        admin,
        LIST_ORGANIZATION_MANAGERS,
        &[],
        &[
            (
                "companyCode",
                current["companyScope"].as_str().unwrap().into(),
            ),
            ("pageSize", "100".into()),
        ],
        None,
    )
    .unwrap();
    let entry = directory["items"]
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["id"] == person["employee"]["id"])
        .unwrap();
    let person_id = entry["id"].clone();
    let appointment = entry["appointments"][0]["id"].clone();
    let preview_call = |body: Value| {
        workflow::dispatch(service, admin, PREVIEW_OA_DEPLOYMENT, &[], &[], Some(body)).unwrap()
    };
    let mut settings = workflow::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    for rule in settings["rules"].as_array_mut().unwrap() {
        rule["stages"] = json!([workflow::node("review", &[], "Named", json!([reviewer]))]);
    }
    settings["handlingServices"] = json!([
        {"key":"seal","name":"公章","category":"Seal","handlerUserIds":[reviewer],"isActive":true},
        {"key":"it","name":"IT支持","category":"IT","handlerUserIds":[reviewer],"isActive":true},
        {"key":"certificate","name":"证明开具","category":"Certificate","handlerUserIds":[reviewer],"isActive":true},
        {"key":"repair","name":"维修","category":"Repair","handlerUserIds":[reviewer],"isActive":true},
        {"key":"other","name":"其他事项","category":"Other","handlerUserIds":[reviewer],"isActive":true}
    ]);
    settings = workflow::save_settings(service, admin, settings).unwrap();
    let preview_input = json!({"action":"Publish","sample":{"employeeId":person_id,"appointmentId":appointment},"items":[{"definitionKey":"general"},{"definitionKey":"leave"}]});
    let preview = preview_call(preview_input.clone());
    assert!(
        preview["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|row| row["ready"] == true),
        "{preview}"
    );
    assert!(settings["publications"].as_array().unwrap().is_empty());
    let apply = |preview: &Value| json!({"action":preview["action"],"expectedVersion":preview["expectedVersion"],"catalogRevision":preview["catalogRevision"],"commandKey":nonce().unwrap(),"items":preview["items"].as_array().unwrap().iter().map(|item| json!({"definitionKey":item["definitionKey"],"expectedRevision":item["expectedRevision"],"samples":item["samples"]})).collect::<Vec<_>>()});
    let mut stale = apply(&preview);
    stale["items"][1]["expectedRevision"] = json!(999);
    assert_eq!(
        workflow::call(service, admin, APPLY_OA_DEPLOYMENT, 0, Some(stale))
            .unwrap_err()
            .status,
        Some(409)
    );
    let unchanged = workflow::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    assert!(unchanged["publications"].as_array().unwrap().is_empty());
    let body = apply(&preview);
    let saved = workflow::request(service, admin, APPLY_OA_DEPLOYMENT, Some(body.clone()));
    let retry = workflow::request(service, admin, APPLY_OA_DEPLOYMENT, Some(body));
    assert_eq!(saved["publications"], retry["publications"]);
    assert_eq!(saved["publications"].as_array().unwrap().len(), 2);
    let missing = workflow::request(
        service,
        &token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"title":"缺少事项","reason":"验证不回退","category":"Other"}),
        ),
    );
    let error = workflow::call(
        service,
        &token,
        SUBMIT_GENERAL_REQUEST,
        missing["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":missing["versionNumber"],"commandKey":nonce().unwrap()})),
    )
    .unwrap_err();
    assert_eq!(
        error.code,
        Some(export_doc_engine::api::ErrorCode::HandlingRequired)
    );
    let mut pending = vec![];
    for (category, key) in [
        ("Seal", "seal"),
        ("Certificate", "certificate"),
        ("IT", "it"),
        ("Repair", "repair"),
        ("Other", "other"),
    ] {
        let draft = workflow::request(
            service,
            &token,
            CREATE_GENERAL_REQUEST,
            Some(
                json!({"requestKey":nonce().unwrap(),"title":"类别办理","reason":"各类别具有独立办理事项","category":category,"serviceId":key}),
            ),
        );
        let row = workflow::call(
            service,
            &token,
            SUBMIT_GENERAL_REQUEST,
            draft["id"].as_i64().unwrap(),
            Some(json!({"expectedVersion":draft["versionNumber"],"commandKey":nonce().unwrap()})),
        )
        .unwrap();
        assert_eq!(row["status"], "Pending");
        assert!(
            row["approvalPlan"]["nodes"]
                .as_array()
                .unwrap()
                .iter()
                .any(|node| node["kind"] == "Handling" && node["skipped"] == false)
        );
        pending.push(row);
    }
    let disabled = preview_call(
        json!({"action":"Disable","items":[{"definitionKey":"general"},{"definitionKey":"leave"}]}),
    );
    let stopped = workflow::request(service, admin, APPLY_OA_DEPLOYMENT, Some(apply(&disabled)));
    assert!(
        stopped["publications"]
            .as_array()
            .unwrap()
            .iter()
            .all(|row| row["enabled"] == false)
    );
    for row in pending {
        let preserved = workflow::call(
            service,
            &token,
            GET_GENERAL_REQUEST,
            row["id"].as_i64().unwrap(),
            None,
        )
        .unwrap();
        assert_eq!(preserved["approvalPlan"], row["approvalPlan"]);
        assert_eq!(preserved["status"], "Pending");
        let id = row["id"].as_i64().unwrap();
        let body = workflow::decision_body(
            service,
            &reviewer_token,
            APPROVE_GENERAL_REQUEST,
            id,
            Some(json!({"expectedVersion":preserved["versionNumber"],"note":"停用后继续在途审批"})),
        );
        let approved =
            workflow::call(service, &reviewer_token, APPROVE_GENERAL_REQUEST, id, body).unwrap();
        assert_eq!(approved["status"], "Approved");
        let node = approved["approvalPlan"]["nodes"]
            .as_array()
            .unwrap()
            .iter()
            .find(|node| node["kind"] == "Handling" && node["state"] == "Active")
            .unwrap();
        let completed = workflow::call(service,&reviewer_token,EXECUTE_GENERAL_REQUEST_FULFILLMENT,id,Some(json!({"expectedVersion":approved["versionNumber"],"nodeKey":node["key"],"commandKey":nonce().unwrap(),"note":"真实办理完成"}))).unwrap();
        assert_eq!(completed["status"], "Completed");
    }
    // Independent service edits retain CAS and idempotency without a catalog-sized settings body.
    let original = workflow::services(service, admin)
        .unwrap()
        .into_iter()
        .find(|row| row["key"] == "seal")
        .unwrap();
    let mut edited = original.clone();
    edited["name"] = json!("公章办理新名称");
    let mutation = json!({"service":edited,"commandKey":nonce().unwrap()});
    workflow::request(
        service,
        admin,
        SAVE_HANDLING_SERVICE,
        Some(mutation.clone()),
    );
    workflow::request(service, admin, SAVE_HANDLING_SERVICE, Some(mutation));
    let conflict = workflow::call(
        service,
        admin,
        SAVE_HANDLING_SERVICE,
        0,
        Some(json!({"service":original,"commandKey":nonce().unwrap()})),
    )
    .unwrap_err();
    assert_eq!(conflict.status, Some(409));
    assert!(
        workflow::request(service, admin, GET_OA_APPROVAL_SETTINGS, None)
            .get("handlingServices")
            .is_none()
    );
    let selected = workflow::query(
        service,
        admin,
        LIST_GENERAL_HANDLING_SERVICES,
        &[],
        &[
            ("keyword", "不存在的搜索词".into()),
            ("selected", "seal".into()),
        ],
        None,
    )
    .unwrap();
    assert!(selected["items"].as_array().unwrap().is_empty());
    assert_eq!(selected["selected"]["name"], "公章办理新名称");
    // Approval-only is an explicit Other policy and never leaves an unexecutable handling task.
    let mut only = workflow::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    let rule = only["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap();
    rule["generalHandling"]["Other"] = json!("ApprovalOnly");
    workflow::save_settings(service, admin, only).unwrap();
    let checked = preview_call(
        json!({"action":"Publish","sample":{"employeeId":person_id,"appointmentId":appointment},"items":[{"definitionKey":"general"}]}),
    );
    workflow::request(service, admin, APPLY_OA_DEPLOYMENT, Some(apply(&checked)));
    let draft = workflow::request(
        service,
        &token,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"title":"仅审批事项","reason":"无需办理","category":"Other"}),
        ),
    );
    let id = draft["id"].as_i64().unwrap();
    let row = workflow::call(
        service,
        &token,
        SUBMIT_GENERAL_REQUEST,
        id,
        Some(json!({"expectedVersion":draft["versionNumber"],"commandKey":nonce().unwrap()})),
    )
    .unwrap();
    assert!(
        row["approvalPlan"]["nodes"]
            .as_array()
            .unwrap()
            .iter()
            .all(|node| node["kind"] != "Handling" || node["skipped"] == true)
    );
    let decision = workflow::decision_body(
        service,
        &reviewer_token,
        APPROVE_GENERAL_REQUEST,
        id,
        Some(json!({"expectedVersion":row["versionNumber"],"note":"仅审批结束"})),
    );
    let complete = workflow::call(
        service,
        &reviewer_token,
        APPROVE_GENERAL_REQUEST,
        id,
        decision,
    )
    .unwrap();
    assert_eq!(complete["status"], "Completed");
}

#[test]
fn sqlite_batch_deployment_is_atomic_repeatable_and_checks_handlers() {
    let mut fixture = native_fixture::Fixture::new();
    fixture.client.take();
    let service = NativeService::open(fixture.paths.clone()).unwrap();
    let login = workflow::request(
        &service,
        "",
        LOGIN,
        Some(json!({"username":"admin","password":""})),
    );
    exercise(&service, login["accessToken"].as_str().unwrap());
    service.close().unwrap();
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires an isolated PostgreSQL 18 database"]
fn postgres_batch_deployment_is_atomic_repeatable_and_checks_handlers() {
    let maintenance = std::env::var("EXPORTDOC_TEST_ENGINE_MAINTENANCE").unwrap();
    let app = std::env::var("EXPORTDOC_TEST_ENGINE_APP").unwrap();
    export_doc_storage::Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let mut fixture = native_fixture::Fixture::new();
    fixture.client.take();
    let root = fixture.root.join("postgres");
    std::fs::create_dir_all(&root).unwrap();
    let paths = export_doc_engine::paths::RuntimePaths::server(&root, &root.join("Data")).unwrap();
    let bootstrap = nonce().unwrap();
    let service =
        NativeService::open_postgres(paths, &app, bootstrap.clone(), Default::default()).unwrap();
    let bytes = service
        .dispatch_with_bootstrap(
            LOGIN,
            &[],
            &[],
            Some(json!({"username":"admin","password":"Deployment-Admin-2026"})),
            "",
            &bootstrap,
        )
        .unwrap();
    let login: Value = serde_json::from_slice(&bytes).unwrap();
    exercise(&service, login["accessToken"].as_str().unwrap());
    service.close().unwrap();
}
