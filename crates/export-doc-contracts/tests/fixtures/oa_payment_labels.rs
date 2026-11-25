//! Reviewed OA business labels; stable resources, actions and scopes stay frozen.
use serde_json::{Value, json};

pub(crate) fn apply(baseline: &mut Value) {
    for catalogue in ["modules", "resources"] {
        let entry = baseline["x-exportdoc-permissions"][catalogue]
            .as_array_mut()
            .unwrap()
            .iter_mut()
            .find(|entry| entry["key"] == "office.expenses")
            .unwrap();
        assert_eq!(entry["name"], "费用报销");
        entry["name"] = json!("付款报销");
    }
    let finance = baseline["x-exportdoc-permissions"]["roles"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|role| role["code"] == "Finance")
        .unwrap();
    assert_eq!(
        finance["description"],
        "查看本公司费用报销并登记财务接收，维护本人付款和申请；审批决定另行授权。"
    );
    finance["description"] =
        json!("查看本公司付款报销并登记财务接收，维护本人付款和申请；审批决定另行授权。");
}
