use chrono::{TimeZone, Utc};
use export_doc_domain::approval::{ApprovalPlan, ApprovalStep, RevisionKind};
use serde_json::{Value, json};

fn graph() -> ApprovalPlan {
    let mut plan:ApprovalPlan=serde_json::from_value(json!({"definitionKey":"purchase","policyVersion":1,"nodes":[
        {"key":"review","name":"部门复核","reviewPoints":[],"dependsOn":[],"decision":"All","dueHours":48,"skipped":false,"startedAt":"","dueAt":"","independentReview":false,"revisionPolicy":{"allowAddSigners":true,"allowRemoveSigners":true,"allowReturnToNode":true,"minimumSigners":1,"protectedUserIds":[2]}},
        {"key":"audit","name":"并行复核","reviewPoints":[],"dependsOn":[],"decision":"All","dueHours":48,"skipped":false,"startedAt":"","dueAt":"","independentReview":false},
        {"key":"join","name":"公司复核","reviewPoints":[],"dependsOn":["review","audit"],"decision":"All","dueHours":48,"skipped":false,"startedAt":"","dueAt":"","independentReview":false}],
        "steps":[step("review:2",0,2),step("review:3",0,3),step("audit:4",1,4),step("join:5",2,5)],"notifyUserIds":[]})).unwrap();
    plan.activate(Utc.with_ymd_and_hms(2026, 10, 30, 0, 0, 0).unwrap())
        .unwrap();
    plan.validate(1).unwrap();
    plan
}
fn step(key: &str, stage: usize, user: i64) -> Value {
    json!({"taskKey":key,"stage":stage,"decision":"All","approverUserId":user,"approverName":format!("匿名审批人 {user}"),"approverContext":"","status":"Waiting","actedByUserId":null,"actedByName":"","actedByContext":"","actedAt":"","note":"","delegationKey":""})
}
fn candidate(user: i64) -> ApprovalStep {
    serde_json::from_value(step("new", 0, user)).unwrap()
}
fn sign(plan: &mut ApprovalPlan, key: &str) {
    let index = plan
        .current_indices()
        .into_iter()
        .find(|i| plan.steps[*i].task_key == key)
        .unwrap();
    let step = &mut plan.steps[index];
    step.status = "Approved".into();
    step.acted_by_user_id = Some(step.approver_user_id);
    step.acted_by_name = step.approver_name.clone();
    step.acted_at = "2026-10-30T01:00:00Z".into();
    step.note = "已核验".into();
    plan.settle(index);
}
#[test]
fn adding_before_and_after_preserves_parallel_and_join_dependencies() {
    let mut before = graph();
    before
        .revise(RevisionKind::AddBefore, "review", &[candidate(6)], &[], 1)
        .unwrap();
    let active = before
        .current_indices()
        .iter()
        .map(|i| before.steps[*i].approver_user_id)
        .collect::<Vec<_>>();
    assert!(active.contains(&6) && active.contains(&4));
    assert!(!active.contains(&2) && !active.contains(&5));
    let mut after = graph();
    sign(&mut after, "review:2");
    let original = after.steps[0].clone();
    after
        .revise(RevisionKind::AddAfter, "review", &[candidate(6)], &[], 1)
        .unwrap();
    assert_eq!(after.steps[0].acted_at, original.acted_at);
    assert_eq!(after.steps[0].source_generation, Some(1));
    assert!(
        !after
            .current_indices()
            .iter()
            .any(|i| after.steps[*i].approver_user_id == 6)
    );
    sign(&mut after, "review:3");
    sign(&mut after, "audit:4");
    assert!(
        !after
            .current_indices()
            .iter()
            .any(|i| after.steps[*i].stage == 2)
    );
    assert!(
        after
            .current_indices()
            .iter()
            .any(|i| after.steps[*i].approver_user_id == 6)
    );
}
#[test]
fn returning_one_parallel_branch_keeps_other_approval_as_an_explicit_reference() {
    let mut plan = graph();
    sign(&mut plan, "review:2");
    sign(&mut plan, "review:3");
    sign(&mut plan, "audit:4");
    let previous = serde_json::to_value(&plan).unwrap();
    plan.revise(RevisionKind::ReturnToNode, "review", &[], &[], 1)
        .unwrap();
    assert_eq!(plan.generation, 2);
    assert_eq!(plan.steps[2].source_generation, Some(1));
    assert_eq!(plan.steps[2].acted_at, previous["steps"][2]["actedAt"]);
    assert_eq!(plan.steps[0].status, "Waiting");
    assert_eq!(plan.steps[0].acted_by_user_id, None);
    assert!(plan.steps[0].task_key.contains("g2"));
    assert!(
        !plan
            .current_indices()
            .iter()
            .any(|i| plan.steps[*i].stage == 2)
    );
    assert_eq!(previous["steps"][0]["status"], "Approved");
}
#[test]
fn mandatory_people_and_automatic_pass_are_protected_and_failed_revisions_are_atomic() {
    let mut plan = graph();
    let original = serde_json::to_value(&plan).unwrap();
    assert!(
        plan.revise(RevisionKind::RemoveSigner, "review", &[], &[2], 1)
            .is_err()
    );
    assert_eq!(serde_json::to_value(&plan).unwrap(), original);
    sign(&mut plan, "review:2");
    let signed = serde_json::to_value(&plan).unwrap();
    assert!(
        plan.revise(RevisionKind::RemoveSigner, "review", &[], &[3], 1)
            .is_err()
    );
    assert_eq!(serde_json::to_value(&plan).unwrap(), signed);
    assert!(
        plan.revise(RevisionKind::AddParallel, "review", &[candidate(2)], &[], 1)
            .is_err()
    );
    assert_eq!(serde_json::to_value(&plan).unwrap(), signed);
}
