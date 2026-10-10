use export_doc_domain::designer::{ConditionMatch, ConditionalRule, ReportConditionalBlock};
use serde_json::{Value, json};

#[test]
fn condition_fixtures_use_exact_types_and_reject_invalid_rules_and_data() {
    let cases: Vec<Value> = serde_json::from_str(include_str!(
        "../../../tests/ReportTemplateFixtures/conditional-rules.json"
    ))
    .unwrap();
    for case in cases {
        let parsed = serde_json::from_value::<ConditionalRule>(case["rule"].clone());
        if case["invalidRule"] == true {
            assert!(
                parsed.is_err() || parsed.unwrap().validate().is_err(),
                "{}",
                case["name"]
            );
            continue;
        }
        let rule = parsed.unwrap();
        rule.validate().unwrap();
        let raw = &case["actual"];
        let displayed = match (&case["displayed"], raw) {
            (Value::String(text), _) | (_, Value::String(text)) => text.clone(),
            (_, Value::Null) => String::new(),
            _ => raw.to_string(),
        };
        let actual = rule.matches(raw, &displayed);
        if case["invalidData"] == true {
            assert!(actual.is_err(), "{}", case["name"]);
        } else {
            assert_eq!(
                actual.unwrap(),
                case["expected"].as_bool().unwrap(),
                "{}",
                case["name"]
            );
        }
    }
}

#[test]
fn combinations_preserve_old_templates_bound_rule_count_and_report_all_errors() {
    let source = json!({"id":"c", "condition":{"fieldPath":"Invoice.Currency","operator":"Equals","value":"USD"},
        "content":{"kind":"Text","text":"conditional"},"style":{}});
    let mut block: ReportConditionalBlock = serde_json::from_value(source.clone()).unwrap();
    assert_eq!(block.match_mode, ConditionMatch::All);
    assert!(block.additional_conditions.is_empty());
    assert!(
        block
            .matches(|rule| rule.matches(&json!("USD"), "USD"))
            .unwrap()
    );
    block.additional_conditions.push(serde_json::from_value(json!({"fieldPath":"Invoice.TotalAmount","operator":"GreaterThan","value":"1000","comparisonType":"Number"})).unwrap());
    let evaluate = |rule: &ConditionalRule| {
        if rule.field_path.ends_with("Currency") {
            rule.matches(&json!("USD"), "USD")
        } else {
            rule.matches(&json!("999.99"), "999.99")
        }
    };
    assert!(!block.matches(evaluate).unwrap());
    block.match_mode = ConditionMatch::Any;
    assert!(block.matches(evaluate).unwrap());
    let mut seen = 0;
    assert!(
        block
            .matches(|_| {
                seen += 1;
                if seen == 1 {
                    Ok(true)
                } else {
                    Err("invalid data".into())
                }
            })
            .is_err()
    );
    let encoded = serde_json::to_value(&block).unwrap();
    assert_eq!(
        serde_json::from_value::<ReportConditionalBlock>(encoded).unwrap(),
        block
    );
    block.additional_conditions = vec![block.condition.clone(); 7];
    block.validate_conditions().unwrap();
    block.additional_conditions.push(block.condition.clone());
    assert!(block.validate_conditions().is_err());
    let mut unknown = source;
    unknown["matchMode"] = json!("Execute");
    assert!(serde_json::from_value::<ReportConditionalBlock>(unknown).is_err());
}
