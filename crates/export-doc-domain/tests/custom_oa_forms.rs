use export_doc_domain::{
    approval_graph::{ConditionGroup, overlap},
    approval_policy::{ApprovalCondition, ConditionContext},
    oa_form::FormDefinition,
};
use serde_json::json;
fn form() -> FormDefinition {
    serde_json::from_value(json!({"category":"业务支持","fields":[
        {"key":"customer","label":"客户","kind":"Text","required":true},
        {"key":"freight","label":"运费","kind":"Money","currency":"CNY","required":true},
        {"key":"samples","label":"样品明细","kind":"Table","required":true,"columns":[{"key":"name","label":"样品名称","kind":"Text","required":true},{"key":"quantity","label":"数量","kind":"Decimal","required":true}]},
        {"key":"note","label":"特殊说明","kind":"Text","required":true,"visibleWhen":{"field":"customer","equals":"特殊客户"}}
    ]})).unwrap()
}
#[test]
fn typed_forms_validate_drafts_precision_rows_visibility_and_unknown_fields() {
    let form = form();
    form.validate().unwrap();
    let mut values=serde_json::from_value(json!({"customer":"示例客户","freight":"100.01","samples":[{"name":"测试样品","quantity":"2.125"}]})).unwrap();
    form.validate_values(&values, true).unwrap();
    values.insert("freight".into(), json!("100.001"));
    assert!(form.validate_values(&values, true).is_err());
    values.insert("freight".into(), json!(""));
    form.validate_values(&values, false).unwrap();
    assert!(form.validate_values(&values, true).is_err());
    values.insert("freight".into(), json!("100.01"));
    values.insert("note".into(), json!("hidden payload"));
    assert!(form.validate_values(&values, true).is_err());
    values.remove("note");
    values.insert("unknown".into(), json!("injected"));
    assert!(form.validate_values(&values, false).is_err());
    values.remove("unknown");
    values.insert("samples".into(), json!([{"quantity":"1"}]));
    assert!(form.validate_values(&values, true).is_err());
}
#[test]
fn custom_conditions_are_exact_and_independent_fields_do_not_alias() {
    let condition: ApprovalCondition = serde_json::from_value(
        json!({"field":"FormNumber","fieldKey":"freight","operator":"Greater","value":"100.00"}),
    )
    .unwrap();
    let mut context = ConditionContext::default();
    assert!(!condition.matches(&context).unwrap());
    context
        .form_values
        .insert("freight".into(), json!("100.01"));
    assert!(condition.matches(&context).unwrap());
    let group = |key: &str, op: &str| {
        serde_json::from_value::<ConditionGroup>(json!({"conditions":[{"field":"FormNumber","fieldKey":key,"operator":op,"value":"100"}]})).unwrap()
    };
    assert!(
        !overlap(
            &group("freight", "Greater"),
            &group("freight", "LessOrEqual")
        )
        .unwrap()
    );
    assert!(overlap(&group("freight", "Greater"), &group("other", "LessOrEqual")).unwrap());
}
#[test]
fn department_projection_preserves_nested_or_and_unknown_request_facts() {
    let group:ConditionGroup=serde_json::from_value(json!({"logic":"Any","groups":[
        {"conditions":[{"field":"Department","operator":"Equal","value":"A"},{"field":"Amount","operator":"Greater","value":"100"}]},
        {"conditions":[{"field":"Department","operator":"Equal","value":"B"}]}
    ]})).unwrap();
    assert!(group.may_match_department("A").unwrap());
    assert!(group.may_match_department("B").unwrap());
    assert!(!group.may_match_department("C").unwrap());
    let empty: ConditionGroup = serde_json::from_value(json!({"logic":"Any"})).unwrap();
    assert!(!empty.may_match_department("A").unwrap());
}
