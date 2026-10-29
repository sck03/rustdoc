use export_doc_domain::{
    approval::ApprovalPlan,
    approval_policy::{ApprovalRule, ConditionContext},
};
use serde_json::{Value, json};
fn node(key: &str, deps: Value) -> Value {
    json!({"key":key,"name":key,"dependsOn":deps,"decision":"All","dueHours":48,"skipped":false,"independentReview":false,"startedAt":"","dueAt":""})
}
fn task(stage: usize, user: i64) -> Value {
    json!({"taskKey":format!("{stage}:{user}"),"stage":stage,"decision":"All","approverUserId":user,"approverName":user.to_string(),"status":"Waiting","actedByUserId":null,"actedByName":"","actedAt":"","note":"","delegationKey":""})
}
fn graph() -> ApprovalPlan {
    serde_json::from_value(json!({"definitionKey":"leave","policyVersion":1,"notifyUserIds":[],"nodes":[node("a",json!([])),node("b",json!([])),node("join",json!(["a","b"]))],"steps":[task(0,2),task(1,3),task(2,2)]})).unwrap()
}
fn sign(p: &mut ApprovalPlan, index: usize) {
    p.steps[index].status = "Approved".into();
    p.steps[index].acted_by_user_id = Some(p.steps[index].approver_user_id);
    p.steps[index].acted_at = "2026-10-29T01:00:00Z".into();
    p.close_any_stage(index);
}
#[test]
fn parallel_join_does_not_advance_until_every_predecessor_finishes() {
    let mut p = graph();
    p.validate(1).unwrap();
    assert_eq!(p.current_indices(), vec![0, 1]);
    sign(&mut p, 0);
    assert_eq!(p.current_indices(), vec![1]);
    sign(&mut p, 1);
    assert_eq!(p.current_indices(), vec![2]);
    sign(&mut p, 2);
    assert!(p.current_indices().is_empty());
    p.validate(1).unwrap();
}
#[test]
fn skipped_middle_node_cannot_bypass_an_unfinished_ancestor() {
    let mut p = graph();
    p.nodes[1].depends_on = vec!["a".into()];
    p.nodes[1].skipped = true;
    p.steps.remove(1);
    p.validate(1).unwrap();
    assert_eq!(p.current_indices(), vec![0]);
    sign(&mut p, 0);
    assert_eq!(p.current_indices(), vec![1]);
}
#[test]
fn any_signing_closes_siblings_and_independent_review_cannot_be_delegated_to_prior_signer() {
    let mut p = graph();
    p.nodes[0].decision = export_doc_domain::approval::ApprovalDecision::Any;
    p.steps[0].decision = p.nodes[0].decision;
    let mut extra = p.steps[0].clone();
    extra.task_key = "extra".into();
    extra.approver_user_id = 4;
    p.steps.push(extra);
    sign(&mut p, 0);
    assert_eq!(p.steps[3].status, "Skipped");
    p.validate(1).unwrap();
    sign(&mut p, 1);
    sign(&mut p, 2);
    p.nodes[2].independent_review = true;
    assert!(p.validate(1).is_err());
}
#[test]
fn cycles_missing_dependencies_and_self_signatures_fail_closed() {
    let mut p = graph();
    p.nodes[0].depends_on.push("join".into());
    assert!(p.validate(1).is_err());
    let mut p = graph();
    p.nodes[0].depends_on.push("missing".into());
    assert!(p.validate(1).is_err());
    let mut p = graph();
    p.steps[0].approver_user_id = 1;
    assert!(p.validate(1).is_err());
}
#[test]
fn due_dates_start_once_on_activation_and_survive_round_trip() {
    let mut p = graph();
    let now = chrono::DateTime::parse_from_rfc3339("2026-10-29T01:00:00Z")
        .unwrap()
        .to_utc();
    p.activate(now);
    assert!(p.nodes[2].due_at.is_empty());
    let first = p.nodes[0].due_at.clone();
    p.activate(now + chrono::Duration::hours(1));
    assert_eq!(p.nodes[0].due_at, first);
    sign(&mut p, 0);
    sign(&mut p, 1);
    p.activate(now + chrono::Duration::hours(2));
    let restored: ApprovalPlan = serde_json::from_value(serde_json::to_value(&p).unwrap()).unwrap();
    restored.validate(1).unwrap();
    assert_ne!(restored.nodes[2].due_at, first);
}
#[test]
fn publication_checks_typed_business_fields_currency_and_no_implicit_approver() {
    let mut value = json!({"key":"expense","kind":"expense","variant":"","name":"报销","notifyUserIds":[],"stages":[{
        "key":"review","name":"审核","dependsOn":[],"source":"Named","approverUserIds":[],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","dueHours":48,
        "conditions":[{"field":"Amount","operator":"GreaterOrEqual","value":"100.00"}]}]});
    let rule = |v: &Value| serde_json::from_value::<ApprovalRule>(v.clone()).unwrap();
    assert!(rule(&value).validate().is_err());
    value["stages"][0]["approverUserIds"] = json!([2]);
    assert!(rule(&value).validate().is_err());
    value["stages"][0]["conditions"]
        .as_array_mut()
        .unwrap()
        .push(json!({"field":"Currency","operator":"Equal","value":"CNY"}));
    let r = rule(&value);
    r.validate().unwrap();
    let mut c = ConditionContext {
        amount: Some("100.00".parse().unwrap()),
        currency: "CNY".into(),
        duration: None,
        department: "D".into(),
        category: "".into(),
        personnel_action: "".into(),
    };
    assert!(r.includes(0, &c).unwrap());
    c.currency = "USD".into();
    assert!(!r.includes(0, &c).unwrap());
    value["stages"][0]["conditions"] =
        json!([{"field":"Duration","operator":"Greater","value":"3"}]);
    assert!(rule(&value).validate().is_err());
}
