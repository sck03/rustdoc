//! Real signed workflows exercise revisions, task authority and durable command retries.
#[path = "approval_fixture.rs"]
mod fixture;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

fn op(action: &str) -> Operation {
    super::oa_contract::operation("general", action)
}
fn execute(
    service: &NativeService,
    token: &str,
    row: &Value,
    command: &str,
    target: &str,
    users: &[i64],
) -> Result<Value, export_doc_engine::api::ApiError> {
    fixture::call(
        service,
        token,
        op("workflow-command"),
        row["id"].as_i64().unwrap(),
        Some(json!({
            "commandKey":nonce().unwrap(),"expectedVersion":row["versionNumber"],"command":command,
            "targetNodeKey":target,"userIds":users,"note":"受控流程调整验收"
        })),
    )
}
fn sign(service: &NativeService, token: &str, row: &Value, verification: bool) -> Value {
    let id = row["id"].as_i64().unwrap();
    fixture::call(
        service,
        token,
        APPROVE_GENERAL_REQUEST,
        id,
        fixture::decision_body(
            service,
            token,
            APPROVE_GENERAL_REQUEST,
            id,
            Some(json!({
                "expectedVersion":row["versionNumber"],"note":"核验后同意",
                "verificationValues":if verification {json!({"checked":true})}else{json!({})},
                "confirmedReviewPoints":if verification{json!([0])}else{json!([])}
            })),
        ),
    )
    .unwrap()
}
pub fn exercise(service: &NativeService, admin: &str) {
    let (first_id, first) = fixture::reviewer(service, admin);
    let (parallel_id, parallel) = fixture::reviewer(service, admin);
    let (last_id, last) = fixture::reviewer(service, admin);
    let (added_id, added) = fixture::reviewer(service, admin);
    let person = fixture::request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(json!({
            "requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":"GENERAL",
            "jobTitle":"申请人","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":"流程申请人"}
        })),
    );
    let mut first_node = fixture::node("first", &[], "Named", json!([first_id]));
    first_node["revisionPolicy"] = json!({"allowAddSigners":true,"allowRemoveSigners":true,"allowReturnToNode":true,"minimumSigners":1,"protectedUserIds":[first_id]});
    first_node["reviewPoints"] = json!(["已核对申请原件"]);
    first_node["materialPolicy"] = json!({"requireReviewPoints":true,"verificationFields":[{"key":"checked","label":"原件核对","kind":"Boolean","required":true}]});
    let mut parallel_node = fixture::node("parallel", &[], "Named", json!([parallel_id]));
    parallel_node["revisionPolicy"] = json!({"allowAddSigners":true});
    let mut settings = fixture::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap()["stages"] = json!([
        first_node,
        parallel_node,
        fixture::node("join", &["first", "parallel"], "Named", json!([last_id]))
    ]);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let settings = fixture::request(service, admin, SAVE_OA_APPROVAL_SETTINGS, Some(settings));
    fixture::request(
        service,
        admin,
        PUBLISH_OA_DEFINITION,
        Some(json!({"expectedVersion":settings["versionNumber"],"definitionKey":"general"})),
    );
    let create = || {
        let draft = fixture::request(
            service,
            admin,
            CREATE_GENERAL_REQUEST,
            Some(json!({
                "requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"并行审批与异常操作","reason":"真实签署、幂等和历史验收","category":"Other"
            })),
        );
        fixture::call(service, admin, SUBMIT_GENERAL_REQUEST, draft["id"].as_i64().unwrap(), Some(json!({
            "commandKey":nonce().unwrap(),"expectedVersion":draft["versionNumber"],"note":"正式提交"
        }))).unwrap()
    };
    let mut row = create();
    let candidates = fixture::call(
        service,
        &first,
        op("workflow-candidates"),
        row["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert!(
        candidates["users"]
            .as_array()
            .unwrap()
            .iter()
            .any(|u| u["id"] == added_id)
    );
    assert!(
        !candidates["users"]
            .as_array()
            .unwrap()
            .iter()
            .any(|u| u["id"] == row["ownerUserId"])
    );
    assert_eq!(
        fixture::call(
            service,
            &last,
            op("workflow-candidates"),
            row["id"].as_i64().unwrap(),
            None
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    // An approver cannot alter the other parallel branch merely because it is active.
    assert_eq!(
        execute(
            service,
            &first,
            &row,
            "AddParallel",
            "parallel",
            &[added_id]
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        execute(service, &first, &row, "Suspend", "", &[])
            .unwrap_err()
            .status,
        Some(403)
    );
    row = execute(service, &first, &row, "AddParallel", "first", &[added_id]).unwrap();
    let generation = row["approvalPlan"]["generation"].as_u64().unwrap();
    assert_eq!(generation, 2);
    assert_eq!(
        execute(service, &first, &row, "RemoveSigner", "first", &[first_id])
            .unwrap_err()
            .status,
        Some(400)
    );
    row = execute(service, &first, &row, "RemoveSigner", "first", &[added_id]).unwrap();
    assert_eq!(row["status"], "Pending");
    let id = row["id"].as_i64().unwrap();
    let suspend = json!({"commandKey":nonce().unwrap(),"expectedVersion":row["versionNumber"],"command":"Suspend","note":"等待补充核验"});
    row = fixture::call(
        service,
        admin,
        op("workflow-command"),
        id,
        Some(suspend.clone()),
    )
    .unwrap();
    assert_eq!(row["status"], "Suspended");
    let repeated = fixture::call(
        service,
        admin,
        op("workflow-command"),
        id,
        Some(suspend.clone()),
    )
    .unwrap();
    assert_eq!(repeated["versionNumber"], row["versionNumber"]);
    let mut changed = suspend.clone();
    changed["note"] = json!("不能借重试修改命令");
    assert_eq!(
        fixture::call(service, admin, op("workflow-command"), id, Some(changed))
            .unwrap_err()
            .status,
        Some(409)
    );
    let receipt: Value = serde_json::from_slice(
        &service
            .dispatch(
                op("command-result"),
                &[
                    ("id", id.to_string()),
                    ("commandKey", suspend["commandKey"].as_str().unwrap().into()),
                ],
                &[],
                None,
                admin,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(receipt["committedVersion"], row["versionNumber"]);
    let detail = fixture::call(service, &first, GET_GENERAL_REQUEST, id, None).unwrap();
    assert_eq!(detail["canReview"], false);
    row = execute(service, admin, &row, "Resume", "", &[]).unwrap();
    row = sign(service, &first, &row, true);
    let signed = row["approvalPlan"]["steps"][0].clone();
    row = sign(service, &parallel, &row, false);
    row = execute(service, &last, &row, "ReturnToNode", "first", &[]).unwrap();
    let new_step = &row["approvalPlan"]["steps"][0];
    assert_eq!(new_step["status"], "Waiting");
    assert!(new_step["actedByPersonId"].is_null());
    assert_eq!(new_step["verificationValues"], json!({}));
    assert_eq!(new_step["confirmedReviewPoints"], json!([]));
    assert_ne!(new_step["taskKey"], signed["taskKey"]);
    assert_eq!(row["approvalPlan"]["steps"][1]["status"], "Approved");
    assert!(row["approvalPlan"]["steps"][1]["sourceGeneration"].is_number());
    let history = fixture::call(service, admin, op("history"), id, None).unwrap();
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|event| event["approvalPlan"]["steps"][0]["actedAt"] == signed["actedAt"])
    );
    row = sign(service, &first, &row, true);
    row = sign(service, &last, &row, false);
    assert_eq!(row["status"], "Approved");
    // Before and after additions preserve the separate branch and final join.
    for command in ["AddBefore", "AddAfter"] {
        let mut row = create();
        row = execute(service, &first, &row, command, "first", &[added_id]).unwrap();
        row = sign(service, &parallel, &row, false);
        if command == "AddBefore" {
            let detail = fixture::call(
                service,
                &first,
                GET_GENERAL_REQUEST,
                row["id"].as_i64().unwrap(),
                None,
            )
            .unwrap();
            assert_eq!(detail["canReview"], false);
            row = sign(service, &added, &row, true);
            row = sign(service, &first, &row, true);
        } else {
            let detail = fixture::call(
                service,
                &added,
                GET_GENERAL_REQUEST,
                row["id"].as_i64().unwrap(),
                None,
            )
            .unwrap();
            assert_eq!(detail["canReview"], false);
            row = sign(service, &first, &row, true);
            row = sign(service, &added, &row, true);
        }
        assert_eq!(row["status"], "Pending");
        row = sign(service, &last, &row, false);
        assert_eq!(row["status"], "Approved");
    }
    let pending = create();
    let terminated = execute(service, admin, &pending, "Terminate", "", &[]).unwrap();
    assert_eq!(terminated["status"], "Terminated");
    assert!(
        terminated["approvalPlan"]["steps"]
            .as_array()
            .unwrap()
            .iter()
            .all(|s| s["status"] == "Cancelled")
    );
    subflow_versions(
        service,
        admin,
        &person,
        (first_id, &first),
        (parallel_id, &parallel),
        (last_id, &last),
    );
}

fn subflow_versions(
    service: &NativeService,
    admin: &str,
    person: &Value,
    first: (i64, &str),
    newer: (i64, &str),
    last: (i64, &str),
) {
    let publish = |nodes: Value| {
        let mut settings = fixture::request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
        settings["rules"]
            .as_array_mut()
            .unwrap()
            .iter_mut()
            .find(|r| r["key"] == "general")
            .unwrap()["stages"] = nodes;
        settings["expectedVersion"] = settings["versionNumber"].clone();
        let saved = fixture::request(service, admin, SAVE_OA_APPROVAL_SETTINGS, Some(settings));
        let published = fixture::request(
            service,
            admin,
            PUBLISH_OA_DEFINITION,
            Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"general"})),
        );
        published["publications"]
            .as_array()
            .unwrap()
            .iter()
            .find(|p| p["key"] == "general")
            .unwrap()["version"]
            .as_i64()
            .unwrap()
    };
    let child_version = publish(json!([fixture::node(
        "child-review",
        &[],
        "Named",
        json!([first.0])
    )]));
    let mut wrapper = fixture::node("sub-review", &[], "Named", json!([first.0]));
    wrapper["subflow"] = json!({"definitionKey":"general","version":child_version});
    let parent_version = publish(json!([
        wrapper,
        fixture::node("parent-join", &["sub-review"], "Named", json!([last.0]))
    ]));
    let create = || {
        let draft = fixture::request(
            service,
            admin,
            CREATE_GENERAL_REQUEST,
            Some(
                json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":"固定版本子流程","reason":"版本变化不能改写在途实例","category":"Other"}),
            ),
        );
        fixture::call(service,admin,SUBMIT_GENERAL_REQUEST,draft["id"].as_i64().unwrap(),Some(json!({"commandKey":nonce().unwrap(),"expectedVersion":draft["versionNumber"],"note":"提交固定版本流程"}))).unwrap()
    };
    let mut pending = create();
    assert_eq!(pending["approvalPlan"]["policyVersion"], parent_version);
    assert_eq!(
        pending["approvalPlan"]["subflows"][0]["version"],
        child_version
    );
    assert_eq!(
        pending["approvalPlan"]["subflows"][0]["nodeKeys"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
    publish(json!([fixture::node(
        "new-review",
        &[],
        "Named",
        json!([newer.0])
    )]));
    let old = fixture::call(
        service,
        last.1,
        GET_GENERAL_REQUEST,
        pending["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert_eq!(old["canReview"], false, "parent must wait for child leaves");
    pending = sign(service, first.1, &pending, false);
    assert_eq!(pending["status"], "Pending");
    pending = sign(service, last.1, &pending, false);
    assert_eq!(pending["status"], "Approved");
    let next = create();
    assert!(
        next["approvalPlan"]["subflows"]
            .as_array()
            .unwrap()
            .is_empty()
    );
    assert_eq!(sign(service, newer.1, &next, false)["status"], "Approved");
}
