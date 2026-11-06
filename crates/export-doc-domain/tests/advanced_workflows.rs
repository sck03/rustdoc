use export_doc_domain::{
    approval::ApprovalPlan,
    approval_graph::{NodeState, OutputAggregation, Propagation},
    approval_policy::{ApprovalRule, ConditionContext},
};
use serde_json::{Value, json};
fn node(key: &str, kind: &str, phase: &str, depends: &[&str]) -> Value {
    json!({"key":key,"name":key,"kind":kind,"phase":phase,"dependsOn":depends,"decision":"All","dueHours":1,"skipped":false,"startedAt":"","dueAt":"","independentReview":false,"materialPolicy":{"verificationFields":[{"key":"value","label":"核验值","kind":"Decimal","required":false}]}})
}
fn plan(nodes: Vec<Value>) -> ApprovalPlan {
    let steps=nodes.iter().enumerate().filter(|(_,n)|n["kind"]=="Approval").map(|(i,_)|json!({"taskKey":format!("task-{i}"),"stage":i,"decision":"All","approverUserId":(i+2) as i64,"approverPersonId":(i+20) as i64,"approverName":format!("人员{i}"),"approverContext":"","status":"Waiting","actedByUserId":null,"actedByName":"","actedByContext":"","actedAt":"","note":"","delegationKey":""})).collect::<Vec<_>>();
    serde_json::from_value(json!({"definitionKey":"general","policyVersion":1,"nodes":nodes,"steps":steps,"notifyUserIds":[]})).unwrap()
}
fn sign(p: &mut ApprovalPlan, key: &str, status: &str, amount: Option<&str>) {
    let index = p
        .current_indices()
        .into_iter()
        .find(|i| p.nodes[p.steps[*i].stage].key == key)
        .expect("actionable task");
    let step = &mut p.steps[index];
    step.status = status.into();
    step.acted_by_user_id = Some(step.approver_user_id);
    step.acted_by_person_id = step.approver_person_id;
    step.acted_by_name = step.approver_name.clone();
    step.acted_at = chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Nanos, true);
    if let Some(amount) = amount {
        step.verification_values
            .insert("value".into(), json!(amount));
    }
    p.settle(index);
    p.refresh_subflows().unwrap();
    p.activate(chrono::Utc::now()).unwrap();
    p.validate(1).unwrap();
}
fn child(policy: Propagation, required: bool) -> ApprovalPlan {
    let mut p = plan(vec![
        node("pre", "Approval", "Decision", &[]),
        node("child", "Approval", "Decision", &["pre"]),
        node("exit", "Subflow", "Decision", &["child"]),
        node("tail", "Approval", "Decision", &["exit"]),
    ]);
    p.subflows=serde_json::from_value(json!([{"key":"sub","definitionKey":"general","version":1,"definitionId":99,"nodeKeys":["child"],"boundaryKey":"exit","phase":"Decision","policy":{"rejected":policy,"returned":policy,"cancelled":policy,"exception":"Block","compensation":"ManualReview"},"outputs":[{"nodeKey":"child","verificationKey":"value","target":"amount","aggregation":"AllEqual","required":required}]}])).unwrap();
    p.activate(chrono::Utc::now()).unwrap();
    p
}
#[test]
fn skipped_only_dependency_still_waits_for_its_real_ancestor() {
    let mut p = plan(vec![
        node("first", "Approval", "Decision", &[]),
        node("skip", "Handling", "Fulfillment", &["first"]),
        node("last", "Approval", "Fulfillment", &["skip"]),
    ]);
    p.nodes[1].skipped = true;
    p.activate(chrono::Utc::now()).unwrap();
    assert_eq!(p.current_indices().len(), 1);
    assert_eq!(p.nodes[p.steps[p.current().unwrap()].stage].key, "first");
    sign(&mut p, "first", "Approved", None);
    assert_eq!(p.nodes[p.steps[p.current().unwrap()].stage].key, "last");
}
#[test]
fn fulfillment_rejection_keeps_the_approval_decision_and_physical_progress_separate() {
    let mut p = plan(vec![
        node("decision", "Approval", "Decision", &[]),
        node("receipt", "Approval", "Fulfillment", &["decision"]),
    ]);
    p.activate(chrono::Utc::now()).unwrap();
    assert!(p.nodes[1].started_at.is_empty());
    sign(&mut p, "decision", "Approved", None);
    assert_eq!(p.outcome(), "Approved");
    sign(&mut p, "receipt", "Rejected", None);
    assert_eq!(p.outcome(), "Approved");
    assert_eq!(p.nodes[1].state, NodeState::Blocked);
    assert!(p.current_indices().is_empty());
}
#[test]
fn blocked_child_retains_the_signed_refusal_and_retries_in_a_new_generation() {
    let mut p = child(Propagation::Block, false);
    sign(&mut p, "pre", "Approved", None);
    sign(&mut p, "child", "Rejected", None);
    assert_eq!(p.outcome(), "Pending");
    assert_eq!(p.subflow_progress["sub"].state, NodeState::Blocked);
    assert!(p.current_indices().is_empty());
    assert_eq!(p.steps[1].status, "Rejected");
    p.retry_child("sub", 1, chrono::Utc::now()).unwrap();
    assert_eq!(p.generation, 2);
    assert_eq!(p.steps[0].source_generation, Some(1));
    sign(&mut p, "child", "Approved", Some("12.5"));
    assert_eq!(p.subflow_progress["sub"].outputs["amount"], "12.5");
    sign(&mut p, "tail", "Approved", None);
    assert_eq!(p.outcome(), "Approved");
}
#[test]
fn ignored_child_does_not_fabricate_consent_and_its_parent_can_continue() {
    let mut p = child(Propagation::Ignore, false);
    sign(&mut p, "pre", "Approved", None);
    sign(&mut p, "child", "Rejected", None);
    assert_eq!(p.subflow_progress["sub"].outcome, "Ignored");
    assert_eq!(p.steps[1].status, "Rejected");
    sign(&mut p, "tail", "Approved", None);
    assert_eq!(p.outcome(), "Approved");
    assert!(p.steps[1].acted_by_user_id.is_some());
}
#[test]
fn required_child_outputs_block_the_exit_instead_of_silently_disappearing() {
    let mut p = child(Propagation::Block, true);
    sign(&mut p, "pre", "Approved", None);
    sign(&mut p, "child", "Approved", None);
    assert_eq!(p.subflow_progress["sub"].error_code, "SubflowOutputMissing");
    assert_eq!(p.nodes[2].state, NodeState::Blocked);
    assert_eq!(p.steps[1].status, "Approved");
    assert_eq!(p.outcome(), "Pending");
}
#[test]
fn cancellation_and_ignored_children_cannot_bypass_unfinished_parent_preconditions() {
    let mut p = child(Propagation::Ignore, false);
    p.cancel_child("sub").unwrap();
    assert_eq!(p.outcome(), "Pending");
    assert_eq!(p.current_indices().len(), 1);
    assert_eq!(p.nodes[p.steps[p.current().unwrap()].stage].key, "pre");
    sign(&mut p, "pre", "Approved", None);
    sign(&mut p, "tail", "Approved", None);
    assert_eq!(p.outcome(), "Approved");
}
#[test]
fn child_sum_is_exact_and_a_conflicting_equal_contract_is_an_exception() {
    let mut p = child(Propagation::Block, true);
    let mut extra = p.steps[1].clone();
    extra.task_key = "second-child".into();
    extra.approver_user_id = 9;
    extra.approver_person_id = Some(29);
    p.steps.push(extra);
    sign(&mut p, "pre", "Approved", None);
    sign(&mut p, "child", "Approved", Some("0.1"));
    sign(&mut p, "child", "Approved", Some("0.2"));
    assert_eq!(
        p.subflow_progress["sub"].error_code,
        "SubflowOutputConflict"
    );
    p.subflows[0].outputs[0].aggregation = OutputAggregation::Sum;
    p.refresh_subflows().unwrap();
    assert_eq!(p.subflow_progress["sub"].outputs["amount"], "0.3");
}
#[test]
fn replacing_accounts_does_not_allow_the_same_natural_person_to_vote_twice() {
    let mut p = plan(vec![node("review", "Approval", "Decision", &[])]);
    let mut extra = p.steps[0].clone();
    extra.task_key = "other-account".into();
    extra.approver_user_id = 8;
    extra.approver_person_id = Some(28);
    p.steps.push(extra);
    p.activate(chrono::Utc::now()).unwrap();
    sign(&mut p, "review", "Approved", None);
    assert!(p.signer_conflict(0, 8, p.steps[0].acted_by_person_id));
    p.steps[1].status = "Approved".into();
    p.steps[1].acted_by_user_id = Some(8);
    p.steps[1].acted_by_person_id = p.steps[0].acted_by_person_id;
    p.steps[1].acted_at = chrono::Utc::now().to_rfc3339();
    assert!(p.validate(1).is_err());
}
fn branch_rule() -> ApprovalRule {
    serde_json::from_value(json!({"key":"expense","kind":"expense","variant":"","name":"费用","notifyUserIds":[],"stages":[
        {"key":"choice","name":"金额分支","kind":"Exclusive","dependsOn":[],"source":"Named","approverUserIds":[],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":1,"branches":[{"target":"large","condition":{"conditions":[{"field":"Currency","operator":"Equal","value":"CNY"},{"field":"Amount","operator":"Greater","value":"100"}]}},{"target":"normal","isDefault":true}]},
        {"key":"large","name":"大额","dependsOn":["choice"],"source":"Named","approverUserIds":[2],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":1},
        {"key":"normal","name":"默认","dependsOn":["choice"],"source":"Named","approverUserIds":[3],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":1}
    ]})).unwrap()
}
#[test]
fn exclusive_money_branches_have_an_explicit_currency_boundary_and_default_path() {
    let r = branch_rule();
    r.validate().unwrap();
    let mut c = ConditionContext {
        amount: Some("100.0000".parse().unwrap()),
        currency: "CNY".into(),
        ..Default::default()
    };
    assert!(
        r.routed_exclusions_in(&c, &std::collections::BTreeMap::new())
            .unwrap()
            .contains("large")
    );
    c.amount = Some("100.0001".parse().unwrap());
    assert!(
        r.routed_exclusions_in(&c, &std::collections::BTreeMap::new())
            .unwrap()
            .contains("normal")
    );
    c.currency = "USD".into();
    assert!(
        r.routed_exclusions_in(&c, &std::collections::BTreeMap::new())
            .unwrap()
            .contains("large")
    );
}
#[test]
fn overlapping_exclusive_paths_are_rejected_by_publication_and_runtime() {
    let mut r = branch_rule();
    let mut duplicate = r.stages[0].branches[0].clone();
    duplicate.target = "normal".into();
    r.stages[0].branches.insert(1, duplicate);
    assert!(r.validate().is_err());
    let c = ConditionContext {
        amount: Some("101".parse().unwrap()),
        currency: "CNY".into(),
        ..Default::default()
    };
    assert!(
        r.routed_exclusions_in(&c, &std::collections::BTreeMap::new())
            .is_err()
    );
}
#[test]
fn partial_handling_history_keeps_every_actual_person_out_of_independent_acceptance() {
    let mut p = plan(vec![
        node("approval", "Approval", "Decision", &[]),
        node("issue", "Handling", "Fulfillment", &["approval"]),
        node("return", "Handling", "Fulfillment", &["issue"]),
        node("accept", "Approval", "Fulfillment", &["return"]),
    ]);
    p.nodes[3].independent_review = true;
    p.record_participant(1, 8, Some(28), None, "").unwrap();
    p.record_participant(2, 9, Some(29), None, "").unwrap();
    p.record_participant(2, 10, Some(30), None, "").unwrap();
    assert!(p.signer_conflict(3, 9, Some(29)));
    assert!(p.signer_conflict(3, 8, Some(28)));
    assert!(!p.signer_conflict(3, 11, Some(31)));
    p.record_participant(1, 8, Some(28), Some(41), "first-principal")
        .unwrap();
    p.record_participant(1, 8, Some(28), Some(42), "second-principal")
        .unwrap();
    assert!(p.signer_conflict(3, 21, Some(41)));
    assert!(p.signer_conflict(3, 22, Some(42)));
}

#[test]
fn nested_failure_obeys_each_parent_policy_and_recovery_preserves_prior_signatures() {
    for refusal in ["Rejected", "Returned"] {
        let mut p = plan(vec![
            node("pre", "Approval", "Decision", &[]),
            node("inner-review", "Approval", "Decision", &["pre"]),
            node("inner-result", "Subflow", "Decision", &["inner-review"]),
            node("outer-review", "Approval", "Decision", &["inner-result"]),
            node("outer-result", "Subflow", "Decision", &["outer-review"]),
            node("tail", "Approval", "Decision", &["outer-result"]),
        ]);
        p.subflows=serde_json::from_value(json!([
            {"key":"outer","definitionKey":"general","version":1,"definitionId":98,"nodeKeys":["inner-review","inner-result","outer-review"],"boundaryKey":"outer-result","phase":"Decision","policy":{"rejected":"Block","returned":"Block","cancelled":"Block","exception":"Block"}},
            {"key":"outer:inner","parentKey":"outer","definitionKey":"general","version":1,"definitionId":99,"nodeKeys":["inner-review"],"boundaryKey":"inner-result","phase":"Decision","policy":{"rejected":"Propagate","returned":"Propagate","cancelled":"Propagate","exception":"Block"}}
        ])).unwrap();
        p.activate(chrono::Utc::now()).unwrap();
        sign(&mut p, "pre", "Approved", None);
        sign(&mut p, "inner-review", refusal, None);
        assert_eq!(p.outcome(), "Pending");
        assert_eq!(p.subflow_progress["outer:inner"].outcome, refusal);
        assert_eq!(p.subflow_progress["outer"].state, NodeState::Blocked);
        assert!(p.current_indices().is_empty());
        p.retry_child("outer", 1, chrono::Utc::now()).unwrap();
        assert_eq!(p.steps[0].source_generation, Some(1));
        assert_eq!(p.steps[0].status, "Approved");
        sign(&mut p, "inner-review", "Approved", None);
        sign(&mut p, "outer-review", "Approved", None);
        sign(&mut p, "tail", "Approved", None);
        assert_eq!(p.outcome(), "Approved");
    }
}

#[test]
fn child_with_physical_progress_requires_compensation_before_a_new_generation() {
    let mut p = plan(vec![
        node("pre", "Approval", "Decision", &[]),
        node("handover", "Handling", "Fulfillment", &["pre"]),
        node("acceptance", "Approval", "Fulfillment", &["handover"]),
        node("result", "Subflow", "Fulfillment", &["acceptance"]),
        node("tail", "Approval", "Fulfillment", &["result"]),
    ]);
    p.nodes[1].actors = serde_json::from_value(
        json!([{"userId":4,"personId":24,"name":"实际办理人","context":""}]),
    )
    .unwrap();
    p.subflows=serde_json::from_value(json!([{"key":"delivery","definitionKey":"general","version":1,"definitionId":99,"nodeKeys":["handover","acceptance"],"boundaryKey":"result","phase":"Fulfillment","policy":{"rejected":"Block","compensation":"ManualReview"}}])).unwrap();
    p.activate(chrono::Utc::now()).unwrap();
    sign(&mut p, "pre", "Approved", None);
    p.nodes[1].execution=Some(serde_json::from_value(json!({"userId":4,"personId":24,"context":"","occurredAt":chrono::Utc::now().to_rfc3339(),"note":"已实际交付"})).unwrap());
    p.nodes[1].state = NodeState::Completed;
    p.record_participant(1, 4, Some(24), None, "").unwrap();
    p.activate(chrono::Utc::now()).unwrap();
    sign(&mut p, "acceptance", "Rejected", None);
    assert_eq!(p.outcome(), "Approved");
    assert!(p.retry_child("delivery", 1, chrono::Utc::now()).is_err());
    assert!(p.nodes[1].execution.is_some());
    p.mark_child_compensated("delivery").unwrap();
    p.retry_child("delivery", 1, chrono::Utc::now()).unwrap();
    assert_eq!(p.generation, 2);
    assert!(p.nodes[1].execution.is_none());
    assert!(p.participants.iter().any(|person| person.user_id == 4));
    assert_eq!(p.steps[0].source_generation, Some(1));
}
