//! Material access must hold across every public representation, on either database.
#[path = "approval_fixture.rs"]
mod workflow;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, admin: &str) {
    let key = nonce().unwrap();
    let secret = format!("protected-material-{key}");
    let user = workflow::request(service, admin, CREATE_USER_ACCOUNT, Some(json!({
        "username":format!("material-{key}"),"fullName":"材料审查员","role":"OfficeEmployee",
        "companyScope":"DEFAULT","departmentId":"GENERAL","isActive":true,"resetPassword":"Materials-Test-2026",
        "permissionGrants":[
            {"resourceKey":"office.general","action":"view","dataScope":"company"},
            {"resourceKey":"office.general","action":"approve","dataScope":"company"},
            {"resourceKey":"office.notifications","action":"view","dataScope":"own"}
        ]
    })))["user"].clone();
    let person = |name: &str| {
        workflow::request(
            service,
            admin,
            CREATE_PERSONNEL,
            Some(json!({
                "requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":"GENERAL",
                "jobTitle":"普通员工","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":name}
            })),
        )
    };
    let reviewer = person("材料审查员");
    workflow::call(service, admin, LINK_PERSONNEL_ACCOUNT, reviewer["employee"]["id"].as_i64().unwrap(), Some(json!({
        "expectedVersion":reviewer["versionNumber"],"userId":user["id"],"expectedAccountVersion":user["versionNumber"]
    }))).unwrap();
    let login = workflow::request(
        service,
        "",
        LOGIN,
        Some(json!({"username":user["username"],"password":"Materials-Test-2026"})),
    );
    let token = login["accessToken"].as_str().unwrap();
    let applicant = person("申请材料所有者");
    let mut settings = workflow::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    let mut node = workflow::node("material-review", &[], "Named", json!([user["id"]]));
    node["materialPolicy"] =
        json!({"hiddenFields":["title","reason"],"allowRestrictedAttachments":false});
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap()["stages"] = json!([node]);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let saved = workflow::save_settings(service, admin, settings).unwrap();
    workflow::request(
        service,
        admin,
        PUBLISH_OA_DEFINITION,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"general"})),
    );
    let mut request = workflow::request(
        service,
        admin,
        CREATE_GENERAL_REQUEST,
        Some(json!({
            "requestKey":nonce().unwrap(),"employeeId":applicant["employee"]["id"],"title":secret,"reason":secret,"category":"Other"
        })),
    );
    let id = request["id"].as_i64().unwrap();
    let parameters = [("id", id.to_string())];
    request = service.upload(super::oa_contract::operation("general","upload"), &parameters,
        json!({"expectedVersion":request["versionNumber"],"purpose":"Supporting","classification":"Restricted"}),
        &format!("{secret}.xml"), b"<?xml version=\"1.0\"?><Private>restricted</Private>", admin).unwrap();
    let attachment = request["attachments"][0]["id"].clone();
    let act = |token: &str, operation: Operation, row: &Value, note: &str| {
        workflow::call(
            service,
            token,
            operation,
            id,
            workflow::decision_body(
                service,
                token,
                operation,
                id,
                Some(json!({"expectedVersion":row["versionNumber"],"note":note})),
            ),
        )
        .unwrap()
    };
    request = act(admin, SUBMIT_GENERAL_REQUEST, &request, "提交独立材料审核");
    let read =
        |operation: Operation, parameters: &[(&str, String)], query: &[(&str, String)]| -> Value {
            let value = serde_json::from_slice(
                &service
                    .dispatch(operation, parameters, query, None, token)
                    .unwrap(),
            )
            .unwrap();
            export_doc_contracts::validation::response(operation.id, &value).unwrap();
            value
        };
    let check = |value: &Value| {
        assert!(
            !value.to_string().contains(&secret),
            "protected content escaped a projection"
        )
    };
    let detail = read(GET_GENERAL_REQUEST, &parameters, &[]);
    check(&detail);
    assert!(detail.get("title").is_none() && detail.get("reason").is_none());
    assert_eq!(detail["materialAccess"]["isPartial"], true);
    assert_eq!(detail["attachments"], json!([]));
    check(&read(
        super::oa_contract::operation("general", "list"),
        &[],
        &[("mineOnly", "false".into())],
    ));
    check(&read(GET_OA_APPROVAL_HUB, &[], &[]));
    let notifications = ALL_OPERATIONS
        .iter()
        .find(|op| op.id == "ListNotifications")
        .unwrap();
    check(&read(*notifications, &[], &[]));
    let attachment_path = [
        ("id", id.to_string()),
        ("attachmentId", attachment.to_string()),
    ];
    assert_eq!(
        service
            .download_file(
                super::oa_contract::operation("general", "download"),
                &attachment_path,
                token
            )
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    request = act(token, APPROVE_GENERAL_REQUEST, &request, &secret);
    check(&request);
    check(&read(
        super::oa_contract::operation("general", "history"),
        &parameters,
        &[],
    ));
    let submission_path = [
        ("id", id.to_string()),
        ("submissionId", request["submissionId"].to_string()),
    ];
    let archived = read(
        super::oa_contract::operation("general", "submission"),
        &submission_path,
        &[],
    );
    check(&archived);
    assert_eq!(archived["materialAccess"]["isPartial"], true);
    assert_eq!(
        service
            .download_file(
                super::oa_contract::operation("general", "bundle-download"),
                &submission_path,
                token
            )
            .err()
            .unwrap()
            .status,
        Some(403)
    );
    assert_eq!(
        workflow::call(service, admin, GET_GENERAL_REQUEST, id, None).unwrap()["reason"],
        secret
    );
    activated_materials(
        service,
        admin,
        user["id"].as_i64().unwrap(),
        token,
        &applicant,
    );
}

fn activated_materials(
    service: &NativeService,
    admin: &str,
    reviewer: i64,
    token: &str,
    applicant: &Value,
) {
    let (future_id, future) = workflow::reviewer(service, admin);
    for (same_person, past_full) in [(true, false), (false, false), (true, true)] {
        let secret = format!("node-material-{}", nonce().unwrap());
        let mut first = workflow::node("first", &[], "Named", json!([reviewer]));
        let mut second = workflow::node(
            "second",
            &["first"],
            "Named",
            json!([if same_person { reviewer } else { future_id }]),
        );
        if past_full {
            second["materialPolicy"] = json!({"hiddenFields":["reason"]});
        } else {
            first["materialPolicy"] = json!({"hiddenFields":["reason"]});
        }
        let mut settings = workflow::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
        settings["rules"]
            .as_array_mut()
            .unwrap()
            .iter_mut()
            .find(|r| r["key"] == "general")
            .unwrap()["stages"] = json!([first, second]);
        settings["expectedVersion"] = settings["versionNumber"].clone();
        let saved = workflow::save_settings(service, admin, settings).unwrap();
        workflow::request(
            service,
            admin,
            PUBLISH_OA_DEFINITION,
            Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"general"})),
        );
        let draft = workflow::request(
            service,
            admin,
            CREATE_GENERAL_REQUEST,
            Some(
                json!({"requestKey":nonce().unwrap(),"employeeId":applicant["employee"]["id"],"title":"按激活节点检查材料","reason":secret,"category":"Other"}),
            ),
        );
        let id = draft["id"].as_i64().unwrap();
        let mut row = workflow::call(
            service,
            admin,
            SUBMIT_GENERAL_REQUEST,
            id,
            workflow::decision_body(
                service,
                admin,
                SUBMIT_GENERAL_REQUEST,
                id,
                Some(json!({"expectedVersion":draft["versionNumber"],"note":"提交节点材料回归"})),
            ),
        )
        .unwrap();
        let before = workflow::call(service, token, GET_GENERAL_REQUEST, id, None).unwrap();
        assert_eq!(
            before.get("reason").is_some(),
            past_full,
            "a future node never widens the current policy"
        );
        if !same_person {
            let queued = workflow::call(service, &future, GET_GENERAL_REQUEST, id, None).unwrap();
            assert_eq!(queued["canReview"], false);
            assert!(
                queued.get("reason").is_none(),
                "future-only assignment has no business materials"
            );
        }
        let sign = |token: &str, row: &Value| {
            workflow::call(
                service,
                token,
                APPROVE_GENERAL_REQUEST,
                id,
                workflow::decision_body(
                    service,
                    token,
                    APPROVE_GENERAL_REQUEST,
                    id,
                    Some(json!({"expectedVersion":row["versionNumber"],"note":secret})),
                ),
            )
            .unwrap()
        };
        row = sign(token, &row);
        let next = if same_person { token } else { future.as_str() };
        let current = workflow::call(service, next, GET_GENERAL_REQUEST, id, None).unwrap();
        assert_eq!(
            current.get("reason").is_some(),
            !past_full,
            "current restrictions override an earlier broad node"
        );
        if past_full {
            let history = workflow::call(
                service,
                token,
                super::oa_contract::operation("general", "history"),
                id,
                None,
            )
            .unwrap();
            assert!(!history.to_string().contains(&secret));
        }
        assert_eq!(sign(next, &row)["status"], "Approved");
    }
}
