use super::memory;
use crate::{Adapter, WorkflowProgress};
use serde_json::json;

#[test]
fn unchanged_workflow_saves_skip_assignments_and_a_signature_changes_one_task() {
    let sqlite = memory();
    {
        let connection = sqlite.connection.borrow();
        for ddl in [
            include_str!("../../sqlite.sql"),
            include_str!("../../permission_versions_sqlite.sql"),
            include_str!("../../identity_runtime_sqlite.sql"),
            include_str!("../../personnel_foundation_sqlite.sql"),
            include_str!("../../access_scope_sqlite.sql"),
            include_str!("../../workflow_runtime_sqlite.sql"),
        ] {
            connection.execute_batch(ddl).unwrap();
        }
        for (id, kind) in [
            (1, "oa-general"),
            (2, "users"),
            (3, "people"),
            (4, "oa-submission"),
            (5, "users"),
        ] {
            connection.execute("INSERT INTO records(id,kind,version,owner_id,company,department,search_text,body) VALUES(?1,?2,1,5,'TEST','D','',?3)",rusqlite::params![id,kind,if id==1 {r#"{"id":1,"submissionNumber":1,"ownerUserId":5,"kind":"general","status":"Pending"}"#}else{"{}"}]).unwrap();
        }
        connection.execute("INSERT INTO persons(id,full_name,employee_number,status,employment_type,hire_date) VALUES(3,'匿名签署人员','003','Active','FullTime','2026-01-01')",[]).unwrap();
    }
    let snapshot =
        json!({"id":4,"requestId":1,"submissionNumber":1,"request":{"id":1,"ownerUserId":5}});
    sqlite.freeze_request_revision(4, &snapshot).unwrap();
    let mut plan = json!({"definitionKey":"general","policyVersion":1,"generation":1,"subflows":[],"calendars":{},"notifyUserIds":[],"nodes":[{"key":"review","name":"审批","decision":"All","dependsOn":[],"skipped":false,"independentReview":false,"startedAt":"2026-11-01T00:00:00Z","dueAt":"2026-11-03T00:00:00Z"}],"steps":[{"taskKey":"review:2","stage":0,"decision":"All","approverUserId":2,"approverPersonId":3,"approverName":"匿名签署人员","approverContext":"","status":"Waiting","actedByUserId":null,"actedByPersonId":null,"actedByName":"","actedByContext":"","actedAt":"","note":"","delegationKey":"","verificationValues":{},"confirmedReviewPoints":[],"sourceGeneration":null}]});
    let active = vec!["review:2".to_owned()];
    let pending = WorkflowProgress {
        approval: "Pending",
        handling: None,
    };
    sqlite
        .save_workflow_state(1, 1, &pending, &plan, &active)
        .unwrap();
    let changes = || {
        sqlite
            .query_json("SELECT CAST(total_changes() AS TEXT)", &[])
            .unwrap()
            .as_u64()
            .unwrap()
    };
    let before = changes();
    sqlite
        .save_workflow_state(1, 1, &pending, &plan, &active)
        .unwrap();
    assert_eq!(
        changes() - before,
        1,
        "only the instance control row changes on an unchanged save"
    );
    let step = &mut plan["steps"][0];
    step["status"] = json!("Approved");
    step["actedByUserId"] = json!(2);
    step["actedByPersonId"] = json!(3);
    step["actedByName"] = json!("匿名签署人员");
    step["actedAt"] = json!("2026-11-01T00:00:01Z");
    step["note"] = json!("实际同意");
    let approved = WorkflowProgress {
        approval: "Approved",
        handling: None,
    };
    let before = changes();
    sqlite
        .save_workflow_state(1, 1, &approved, &plan, &[])
        .unwrap();
    assert_eq!(
        changes() - before,
        2,
        "one signature writes the instance and the affected assignment"
    );
    let before = changes();
    sqlite
        .save_workflow_state(1, 1, &approved, &plan, &[])
        .unwrap();
    assert_eq!(changes() - before, 1);
    plan["steps"][0]["note"] = json!("不得覆盖原签署");
    assert!(
        sqlite
            .save_workflow_state(1, 1, &approved, &plan, &[])
            .is_err()
    );
    let note = sqlite
        .connection
        .borrow()
        .query_row(
            "SELECT note FROM workflow_tasks WHERE request_id=1",
            [],
            |row| row.get::<_, String>(0),
        )
        .unwrap();
    assert_eq!(note, "实际同意");
}
