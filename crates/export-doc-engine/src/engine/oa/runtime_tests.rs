use super::*;
#[path = "../../../tests/support/approval_fixture.rs"]
mod workflow;
use crate::{
    generated_api::*,
    paths::{RuntimePaths, nonce},
    test_support::ScratchDirectory,
};

fn login(service: &NativeService) -> Value {
    workflow::request(
        service,
        "",
        LOGIN,
        Some(json!({"username":"admin","password":""})),
    )
}
fn command(service: &NativeService, token: &str, op: Operation, row: &Value) -> Result<Value> {
    let id = row["id"].as_i64().unwrap();
    workflow::call(
        service,
        token,
        op,
        id,
        workflow::decision_body(
            service,
            token,
            op,
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"定时执行验收"})),
        ),
    )
}
#[test]
fn future_personnel_changes_and_reminders_survive_restart_without_duplicates() {
    let root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/workflow-runtime-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let _cleanup = ScratchDirectory(root.clone());
    let paths = RuntimePaths::server(&root, &root.join("Data")).unwrap();
    let service = NativeService::open(paths.clone()).unwrap();
    let session = login(&service);
    let admin = session["accessToken"].as_str().unwrap();
    let today = crate::clock::BusinessClock::default()
        .at(chrono::Utc::now())
        .unwrap()
        .today;
    let future = today.checked_add_days(chrono::Days::new(3)).unwrap();
    let (reviewer_id, reviewer) = workflow::reviewer(&service, admin);
    let mut settings = workflow::request(&service, admin, GET_OA_APPROVAL_SETTINGS, None);
    for rule in settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .filter(|r| matches!(r["key"].as_str(), Some("personnel:Transfer" | "general")))
    {
        rule["stages"] = json!([workflow::node(
            "scheduled-review",
            &[],
            "Named",
            json!([reviewer_id])
        )]);
    }
    settings["expectedVersion"] = settings["versionNumber"].clone();
    settings = workflow::request(&service, admin, SAVE_OA_APPROVAL_SETTINGS, Some(settings));
    for key in ["personnel:Transfer", "general"] {
        settings = workflow::request(
            &service,
            admin,
            PUBLISH_OA_DEFINITION,
            Some(json!({"expectedVersion":settings["versionNumber"],"definitionKey":key})),
        );
    }
    let mut people = vec![];
    let mut requests = vec![];
    for name in ["按期生效", "冲突保留"] {
        let person = workflow::request(
            &service,
            admin,
            CREATE_PERSONNEL,
            Some(
                json!({"requestKey":nonce().unwrap(),"employeeNumber":nonce().unwrap(),"departmentId":"GENERAL","jobTitle":"旧岗位","hireDate":today.to_string(),"employmentType":"FullTime","profile":{"fullName":name}}),
            ),
        );
        let draft = workflow::request(
            &service,
            admin,
            CREATE_PERSONNEL_CHANGE_REQUEST,
            Some(
                json!({"requestKey":nonce().unwrap(),"employeeId":person["employee"]["id"],"title":name,"reason":"未来岗位调整","personnelChange":{"action":"Transfer","effectiveDate":future.to_string(),"expectedPersonnelVersion":person["versionNumber"],"departmentId":"GENERAL","jobTitle":"新岗位"}}),
            ),
        );
        let pending = command(&service, admin, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft).unwrap();
        let approved = command(
            &service,
            &reviewer,
            APPROVE_PERSONNEL_CHANGE_REQUEST,
            &pending,
        )
        .unwrap();
        assert_eq!(approved["status"], "Approved");
        assert_eq!(
            workflow::call(
                &service,
                admin,
                GET_PERSONNEL,
                person["employee"]["id"].as_i64().unwrap(),
                None
            )
            .unwrap()["employee"]["jobTitle"],
            "旧岗位"
        );
        assert_eq!(
            command(
                &service,
                &reviewer,
                EXECUTE_APPROVED_PERSONNEL_CHANGE,
                &approved
            )
            .unwrap_err()
            .status,
            Some(409)
        );
        people.push(person);
        requests.push(approved);
    }
    workflow::call(&service,admin,UPDATE_PERSONNEL,people[1]["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":people[1]["versionNumber"],"employmentType":"FullTime","profile":{"fullName":"期间新事实"}}))).unwrap();
    service.close().unwrap();
    drop(service);
    let service = NativeService::open(paths.clone()).unwrap();
    service.workflow_scheduler.stop().unwrap();
    let session = login(&service);
    let admin = session["accessToken"].as_str().unwrap();
    let due = chrono::DateTime::parse_from_rfc3339(&format!("{future}T23:00:00+08:00"))
        .unwrap()
        .with_timezone(&chrono::Utc);
    tick(&service, due).unwrap();
    let applied = workflow::call(
        &service,
        admin,
        GET_PERSONNEL_CHANGE_REQUEST,
        requests[0]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    let blocked = workflow::call(
        &service,
        admin,
        GET_PERSONNEL_CHANGE_REQUEST,
        requests[1]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert_eq!(applied["status"], "Completed");
    assert_eq!(applied["personnelExecution"]["state"], "Applied");
    assert_eq!(blocked["status"], "Approved");
    assert_eq!(blocked["personnelExecution"]["state"], "Blocked");
    let changed = workflow::call(
        &service,
        admin,
        GET_PERSONNEL,
        people[0]["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert_eq!(changed["employee"]["jobTitle"], "新岗位");
    let unchanged = workflow::call(
        &service,
        admin,
        GET_PERSONNEL,
        people[1]["employee"]["id"].as_i64().unwrap(),
        None,
    )
    .unwrap();
    assert_eq!(unchanged["employee"]["jobTitle"], "旧岗位");
    assert_eq!(unchanged["profile"]["fullName"], "期间新事实");
    let cancel_body = json!({"commandKey":nonce().unwrap(),"expectedVersion":blocked["versionNumber"],"note":"人员事实已经更新，取消原定变动"});
    let cancelled = workflow::call(
        &service,
        admin,
        CANCEL_APPROVED_PERSONNEL_CHANGE,
        requests[1]["id"].as_i64().unwrap(),
        Some(cancel_body.clone()),
    )
    .unwrap();
    assert_eq!(cancelled["status"], "Cancelled");
    assert_eq!(cancelled["personnelExecution"]["state"], "Cancelled");
    assert_eq!(
        cancelled["approvalPlan"]["steps"], blocked["approvalPlan"]["steps"],
        "cancellation preserves actual signatures"
    );
    let retried = workflow::call(
        &service,
        admin,
        CANCEL_APPROVED_PERSONNEL_CHANGE,
        requests[1]["id"].as_i64().unwrap(),
        Some(cancel_body),
    )
    .unwrap();
    assert_eq!(retried["versionNumber"], cancelled["versionNumber"]);
    tick(&service, due).unwrap();
    assert_eq!(
        workflow::call(
            &service,
            admin,
            GET_PERSONNEL,
            people[0]["employee"]["id"].as_i64().unwrap(),
            None
        )
        .unwrap()["versionNumber"],
        changed["versionNumber"]
    );
    let pending = workflow::request(
        &service,
        admin,
        CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeId":people[0]["employee"]["id"],"title":"重启提醒","reason":"提醒不应代签","category":"Other"}),
        ),
    );
    let pending = command(&service, admin, SUBMIT_GENERAL_REQUEST, &pending).unwrap();
    let id = pending["id"].as_i64().unwrap();
    let deadline = chrono::DateTime::parse_from_rfc3339(
        pending["approvalPlan"]["nodes"][0]["dueAt"]
            .as_str()
            .unwrap(),
    )
    .unwrap()
    .with_timezone(&chrono::Utc);
    let count = |service: &NativeService| {
        service
            .store
            .read(|tx| {
                Ok(store::matching(
                    tx,
                    "oa-event",
                    &[("requestId", id.to_string()), ("action", "remind".into())],
                    &[],
                )?
                .len())
            })
            .unwrap()
    };
    tick(&service, deadline).unwrap();
    assert_eq!(count(&service), 1);
    tick(&service, deadline).unwrap();
    assert_eq!(count(&service), 1);
    service.close().unwrap();
    drop(service);
    let service = NativeService::open(paths).unwrap();
    service.workflow_scheduler.stop().unwrap();
    tick(&service, deadline + chrono::Duration::hours(24)).unwrap();
    assert_eq!(count(&service), 2);
    tick(&service, deadline + chrono::Duration::hours(48)).unwrap();
    assert_eq!(count(&service), 2);
    let session = login(&service);
    let admin = session["accessToken"].as_str().unwrap();
    assert_eq!(
        workflow::call(&service, admin, GET_GENERAL_REQUEST, id, None).unwrap()["status"],
        "Pending"
    );
    service.close().unwrap();
}
