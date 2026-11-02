use super::memory;
use crate::{Adapter, WorkflowProgress};
use serde_json::json;

fn fixture() -> crate::sqlite::Sqlite {
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
    sqlite
}

#[test]
fn unchanged_workflow_saves_skip_assignments_and_a_signature_changes_one_task() {
    let sqlite = fixture();
    let snapshot =
        json!({"id":4,"requestId":1,"submissionNumber":1,"request":{"id":1,"ownerUserId":5}});
    sqlite.freeze_request_revision(4, &snapshot).unwrap();
    let mut plan = json!({"definitionKey":"general","policyVersion":1,"generation":1,"subflows":[],"calendars":{},"notifyUserIds":[],"nodes":[{"key":"review","name":"审批","decision":"All","dependsOn":[],"skipped":false,"independentReview":false,"startedAt":"2026-11-01T00:00:00Z","dueAt":"2026-11-03T00:00:00Z"}],"steps":[{"taskKey":"review:2","stage":0,"decision":"All","approverUserId":2,"approverPersonId":3,"approverName":"匿名签署人员","approverContext":"","status":"Waiting","actedByUserId":null,"actedByPersonId":null,"actedByName":"","actedByContext":"","actedAt":"","note":"","delegationKey":"","verificationValues":{},"confirmedReviewPoints":[],"sourceGeneration":null}]});
    let active = vec!["review:2".to_owned()];
    plan["nodes"][0]["kind"] = json!("Approval");
    plan["nodes"][0]["phase"] = json!("Decision");
    plan["nodes"][0]["state"] = json!("Active");
    plan["nodes"][0]["execution"] = serde_json::Value::Null;
    plan["nodes"][0]["errorCode"] = json!("");
    plan["nodes"][0]["escalationCursor"] = json!(0);
    plan["nodes"][0]["reminderLevel"] = json!(0);
    plan["nodes"][0]["nextEscalationAt"] = json!("");
    plan["steps"][0]["origin"] = json!({});
    plan["steps"][0]["mandatory"] = json!(false);
    let binding = json!({"userId":2,"personId":3,"context":"","administration":false,"origin":{"source":"Named"}});
    plan["nodes"][0]["actors"] = json!([binding]);
    let mut supervisor = binding.clone();
    supervisor["origin"]["source"] = json!("Responsibility");
    plan["nodes"][0]["escalationTargets"] = json!([[supervisor], [binding]]);
    let pending = WorkflowProgress {
        approval: "Pending",
        handling: None,
    };
    sqlite
        .save_workflow_state(1, 1, &pending, &plan, &active)
        .unwrap();
    let assignments = sqlite.query_json("SELECT json_group_array(json(value)) FROM (SELECT json_object('level',activation_level,'source',origin->>'source') AS value FROM workflow_node_actors ORDER BY activation_level)", &[]).unwrap();
    assert_eq!(
        assignments,
        json!([
            {"level":0,"source":"Named"},
            {"level":1,"source":"Responsibility"},
            {"level":2,"source":"Named"}
        ]),
        "the same identity retains each frozen escalation origin independently"
    );
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
    plan["nodes"][0]["state"] = json!("Completed");
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
        3,
        "one signature writes the instance, the completed node and the affected assignment"
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

#[test]
fn expiring_responsibility_revokes_list_count_and_approval_inbox_together() {
    let sqlite = fixture();
    sqlite.connection.borrow().execute_batch(
        "INSERT INTO records(id,kind,version,owner_id,company,department,search_text,body) VALUES
         (6,'companies',1,5,'TEST','D','','{}'),(7,'departments',1,5,'TEST','D','','{}');
         INSERT INTO organization_units VALUES(6,'TEST',NULL,'Company');
         INSERT INTO organization_departments VALUES(7,6,'D',NULL);
         INSERT INTO organization_labels VALUES(6,'测试单位',1),(7,'测试部门',1);
         INSERT INTO personnel_memberships(person_id,company,department,job_title,starts_on,ends_on,is_primary,id,unit_id,department_id)
         VALUES(3,'TEST','D','复核员','2026-01-01','',1,'review-appointment',6,7);
         INSERT INTO personnel_accounts VALUES(3,2);
         INSERT INTO responsibilities(id,unit_id,code,name,workflow_kinds,is_active,version)
         VALUES(1,6,'REVIEW','复核职责','[\"general\"]',1,1);
         INSERT INTO responsibility_assignments(responsibility_id,unit_id,appointment_id,starts_on,ends_on)
         VALUES(1,6,'review-appointment','2026-01-01','2026-11-02');"
    ).unwrap();
    let origin = json!({"source":"Responsibility","sourceId":"1","unitCode":"TEST","appointmentId":"review-appointment"});
    let plan = json!({
        "definitionKey":"general","policyVersion":1,"generation":1,"subflows":[],"calendars":{},"notifyUserIds":[],
        "nodes":[{"key":"review","name":"职责复核","kind":"Approval","phase":"Decision","state":"Active",
        "decision":"All","dependsOn":[],"skipped":false,"independentReview":false,
        "startedAt":"2026-11-01T00:00:00Z","dueAt":"2026-11-03T00:00:00Z","execution":null,"errorCode":"",
        "escalationCursor":0,"reminderLevel":0,"nextEscalationAt":"",
        "actors":[{"userId":2,"personId":3,"context":"","administration":false,"origin":origin}]}],
        "steps":[{"taskKey":"review:2","stage":0,"decision":"All","approverUserId":2,"approverPersonId":3,
        "approverName":"匿名签署人员","approverContext":"","origin":origin,"mandatory":false,"status":"Waiting",
        "actedByUserId":null,"actedByPersonId":null,"actedByName":"","actedByContext":"","actedAt":"",
        "note":"","delegationKey":"","verificationValues":{},"confirmedReviewPoints":[],"sourceGeneration":null}]
    });
    sqlite
        .freeze_request_revision(
            4,
            &json!({"id":4,"requestId":1,"submissionNumber":1,"request":{"id":1,"ownerUserId":5}}),
        )
        .unwrap();
    sqlite
        .save_workflow_state(
            1,
            1,
            &WorkflowProgress {
                approval: "Pending",
                handling: None,
            },
            &plan,
            &["review:2".into()],
        )
        .unwrap();
    let principals = [crate::RecordApprover {
        user_id: 2,
        context: String::new(),
        restrictions: vec![],
    }];
    let mut query = crate::RecordQuery {
        kind: "oa-general",
        company: "TEST",
        owner: Some(2),
        visible_user: Some(2),
        visible_context: Some(""),
        include_participants: true,
        workflow_date: Some(chrono::NaiveDate::from_ymd_opt(2026, 11, 1).unwrap()),
        limit: 20,
        ..Default::default()
    };
    assert_eq!(sqlite.query_records(&query).unwrap().0, 1);
    query.approvers = Some(&principals);
    query.approval_actor = Some(2);
    assert_eq!(sqlite.query_records(&query).unwrap().0, 1);
    query.workflow_date = Some(chrono::NaiveDate::from_ymd_opt(2026, 11, 2).unwrap());
    assert_eq!(
        sqlite.query_records(&query).unwrap().0,
        0,
        "the effective responsibility period applies before counting the inbox"
    );
    query.approvers = None;
    query.approval_actor = None;
    assert_eq!(
        sqlite.query_records(&query).unwrap().0,
        0,
        "expired task and node bindings cannot supply ordinary participation"
    );
    query.workflow_date = None;
    assert_eq!(
        sqlite.query_records(&query).unwrap().0,
        1,
        "internal handover audits retain expired responsibility facts"
    );
}
