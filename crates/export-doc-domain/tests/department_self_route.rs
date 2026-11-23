use export_doc_domain::approval_policy::{
    ApprovalStage, ApproverSource, RepeatedApprover, SelfApproval,
};
use serde_json::json;

#[test]
fn only_effective_department_head_can_skip_an_unprotected_department_node() {
    let mut stage: ApprovalStage = serde_json::from_value(json!({"key":"department","name":"部门审批","dependsOn":[],"source":"DepartmentManager","approverUserIds":[],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":48})).unwrap();
    assert!(stage.skips_department_self(7, &[7]));
    assert!(!stage.skips_department_self(7, &[]));
    assert!(!stage.skips_department_self(7, &[8]));
    assert!(!stage.skips_department_self(7, &[7, 8]));
    assert!(!stage.skips_department_self(0, &[0]));
    stage.self_approval = SelfApproval::Reject;
    assert!(!stage.skips_department_self(7, &[7]));
    stage.self_approval = SelfApproval::Skip;
    stage.repeated_approver = RepeatedApprover::Reject;
    assert!(!stage.skips_department_self(7, &[7]));
    stage.repeated_approver = RepeatedApprover::Allow;
    stage.revision_policy.protected_user_ids = vec![7];
    assert!(!stage.skips_department_self(7, &[7]));
    stage.revision_policy.protected_user_ids.clear();
    for source in [
        ApproverSource::Named,
        ApproverSource::CompanyExecutive,
        ApproverSource::DepartmentLeaders,
    ] {
        stage.source = source;
        assert!(!stage.skips_department_self(7, &[7]));
    }
}
