//! Publish actual submitted material, instances, nodes and immutable decisions.
use export_doc_storage::{Connection, RecordWrite, Result, WorkflowProgress};
use serde_json::json;
pub struct Task {
    pub key: String,
    pub node: String,
    pub assignee: i64,
    pub context: String,
    pub acted_by: Option<i64>,
    pub acted_by_context: String,
    pub status: String,
    pub active: bool,
    pub independent: bool,
    pub due_at: String,
    pub started_at: String,
    pub acted_at: String,
    pub elapsed_seconds: Option<i64>,
}
pub trait WorkflowFixture {
    fn publish_workflow(&self, request: i64, revision: i64, tasks: &[Task]) -> Result<()>;
}
impl WorkflowFixture for Connection {
    fn publish_workflow(&self, request: i64, revision: i64, tasks: &[Task]) -> Result<()> {
        let row = self
            .get("oa-general", request)?
            .expect("fixture OA request");
        let nodes:Vec<_>=tasks.iter().enumerate().map(|(i,t)|{
            let start=match t.elapsed_seconds {
                Some(seconds)=>(chrono::DateTime::parse_from_rfc3339(&t.acted_at).expect("fixture timestamp")-chrono::Duration::seconds(seconds)).to_rfc3339(),
                None=>t.started_at.clone(),
            };
            json!({"key":format!("{}-{i}",t.node),"name":"复核","decision":"All","dependsOn":[],"approvalPercentage":100,"rejectionPolicy":"Immediate","skipped":false,"independentReview":t.independent,"dueHours":0,"reviewPoints":[],"startedAt":start,"dueAt":t.due_at})
        }).collect();
        let steps:Vec<_>=tasks.iter().enumerate().map(|(i,t)|json!({"taskKey":t.key,"stage":i,"decision":"All","approverUserId":t.assignee,"approverName":"匿名责任人","approverContext":t.context,"status":t.status,"actedByUserId":t.acted_by,"actedByName":if t.acted_by.is_some(){"匿名签署人"}else{""},"actedByContext":t.acted_by_context,"actedAt":t.acted_at,"note":"","delegationKey":"","sourceGeneration":null})).collect();
        let plan = json!({"definitionKey":"query-fixture","policyVersion":1,"generation":1,"notifyUserIds":[],"calendars":{},"subflows":[],"nodes":nodes,"steps":steps});
        let mut snapshot = json!({"versionNumber":1,"requestId":request,"submissionNumber":revision,"ownerUserId":row["ownerUserId"],"companyScope":row["companyScope"],"departmentId":row["departmentId"],"request":row,"approvalPlan":plan});
        let archive = self.insert(&RecordWrite {
            kind: "oa-submission",
            identity: None,
            body: &snapshot,
        })?;
        snapshot["id"] = json!(archive);
        self.freeze_request_revision(archive, &snapshot)?;
        let active = tasks
            .iter()
            .filter(|t| t.active)
            .map(|t| t.key.clone())
            .collect::<Vec<_>>();
        let state = if tasks.iter().any(|t| t.status == "Returned") {
            "Returned"
        } else {
            row["status"].as_str().unwrap_or("Pending")
        };
        self.save_workflow_state(
            request,
            revision,
            &WorkflowProgress {
                approval: state,
                handling: None,
            },
            &plan,
            &active,
        )
    }
}
pub fn exercise(tx: &Connection) {
    tx.begin().unwrap();
    let mut body = json!({"versionNumber":1,"ownerUserId":7,"companyScope":"WORKFLOW-FIXTURE","departmentId":"D","status":"Pending","submissionNumber":1,"title":"匿名申请"});
    let request = tx
        .insert(&RecordWrite {
            kind: "oa-general",
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(request);
    tx.set_body(request, &body).unwrap();
    let task = Task {
        key: "review".into(),
        node: "review".into(),
        assignee: request,
        context: String::new(),
        acted_by: None,
        acted_by_context: String::new(),
        status: "Waiting".into(),
        active: true,
        independent: false,
        due_at: "2026-10-30T00:00:00Z".into(),
        started_at: "2026-10-29T00:00:00Z".into(),
        acted_at: String::new(),
        elapsed_seconds: None,
    };
    tx.publish_workflow(request, 1, &[task]).unwrap();
    let principals = [export_doc_storage::RecordApprover {
        user_id: request,
        context: String::new(),
    }];
    for (submission, expected) in [(1, 1), (2, 0)] {
        body["submissionNumber"] = json!(submission);
        tx.set_body(request, &body).unwrap();
        for approval in [false, true] {
            let query = export_doc_storage::RecordQuery {
                kind: "oa-general",
                company: "WORKFLOW-FIXTURE",
                visible_user: (!approval).then_some(request),
                approvers: approval.then_some(principals.as_slice()),
                limit: 1,
                ..Default::default()
            };
            assert_eq!(
                tx.query_records(&query).unwrap().0,
                expected,
                "only the current submission grants participation"
            );
        }
    }
    assert!(
        tx.delete("oa-general", request, 1).is_err(),
        "submitted material protects its parent"
    );
    tx.rollback().unwrap();
}
