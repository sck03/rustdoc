use export_doc_domain::approval::{
    ApprovalDecision, ApprovalPlan, ApprovalVoting, RejectionPolicy, VoteOutcome,
};
use serde_json::json;

fn plan(policy: RejectionPolicy, percentage: u8) -> ApprovalPlan {
    serde_json::from_value(json!({"definitionKey":"purchase","policyVersion":1,"notifyUserIds":[],
        "nodes":[{"key":"review","name":"复核","dependsOn":[],"decision":"Ratio","approvalPercentage":percentage,"rejectionPolicy":policy,"dueHours":48,"skipped":false,"independentReview":false,"startedAt":"","dueAt":""}],
        "steps":(2..=4).map(|id| json!({"taskKey":format!("review:{id}"),"stage":0,"decision":"Ratio","approverUserId":id,"approverName":id.to_string(),"status":"Waiting","actedByUserId":null,"actedByName":"","actedAt":"","note":"","delegationKey":""})).collect::<Vec<_>>()
    })).unwrap()
}
fn sign(p: &mut ApprovalPlan, i: usize, approved: bool) {
    p.steps[i].status = if approved { "Approved" } else { "Rejected" }.into();
    p.steps[i].acted_by_user_id = Some(p.steps[i].approver_user_id);
    p.steps[i].acted_at = "2026-10-30T02:00:00Z".into();
    p.settle(i);
}
#[test]
fn percentages_round_up_without_floating_point_and_reject_empty_or_invalid_electorates() {
    let voting = ApprovalVoting {
        approval_percentage: 67,
        rejection_policy: RejectionPolicy::Collect,
    };
    assert_eq!(voting.required(ApprovalDecision::Ratio, 3).unwrap(), 3);
    assert_eq!(voting.required(ApprovalDecision::Ratio, 100).unwrap(), 67);
    assert!(voting.required(ApprovalDecision::Ratio, 0).is_err());
    assert!(voting.required(ApprovalDecision::Ratio, 201).is_err());
    assert!(
        ApprovalVoting {
            approval_percentage: 0,
            ..voting.clone()
        }
        .validate(ApprovalDecision::Ratio)
        .is_err()
    );
    assert!(voting.validate(ApprovalDecision::All).is_err());
}
#[test]
fn collecting_refusals_keeps_real_signatures_and_closes_only_unnecessary_tasks() {
    let mut p = plan(RejectionPolicy::Collect, 50);
    sign(&mut p, 0, false);
    assert_eq!(p.outcome(), "Pending");
    assert_eq!(p.current_indices(), vec![1, 2]);
    sign(&mut p, 1, true);
    assert_eq!(p.outcome(), "Pending");
    sign(&mut p, 2, true);
    assert_eq!(p.outcome(), "Approved");
    assert_eq!(p.steps[0].status, "Rejected");
    p.validate(1).unwrap();
    let restored: ApprovalPlan = serde_json::from_value(serde_json::to_value(&p).unwrap()).unwrap();
    assert_eq!(restored.outcome(), "Approved");
}
#[test]
fn immediate_veto_cancels_without_fabricating_consent_and_threshold_can_be_unreachable() {
    let mut p = plan(RejectionPolicy::Immediate, 50);
    sign(&mut p, 0, false);
    assert_eq!(p.outcome(), "Rejected");
    assert!(p.current_indices().is_empty());
    assert!(
        p.steps[1..]
            .iter()
            .all(|s| s.status == "Cancelled" && s.acted_by_user_id.is_none())
    );
    p.validate(1).unwrap();
    let voting = ApprovalVoting {
        approval_percentage: 67,
        rejection_policy: RejectionPolicy::Collect,
    };
    assert_eq!(
        voting.outcome(ApprovalDecision::Ratio, 3, 2, 1, 0).unwrap(),
        VoteOutcome::Rejected
    );
}
#[test]
fn enough_votes_skip_unsigned_people_and_one_actual_person_cannot_vote_twice_via_delegation() {
    let mut p = plan(RejectionPolicy::Collect, 50);
    sign(&mut p, 0, true);
    assert!(p.independent_signer_conflict(0, 2));
    sign(&mut p, 1, true);
    assert_eq!(p.outcome(), "Approved");
    assert_eq!(p.steps[2].status, "Skipped");
    assert_eq!(p.steps[2].acted_by_user_id, None);
    p.validate(1).unwrap();
    p.steps[1].acted_by_user_id = Some(2);
    assert!(p.validate(1).is_err());
}
