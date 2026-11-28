use super::*;
use serde_json::json;
fn validate_graph(
    people: Vec<Value>,
    proposed: &Value,
    previous: &Value,
    today: chrono::NaiveDate,
) -> Result<()> {
    let mut available = HashMap::new();
    for person in people
        .iter()
        .filter(|person| person["id"] != proposed["id"])
    {
        add_appointments(&mut available, person, today, false)?;
    }
    add_appointments(&mut available, proposed, today, true)?;
    super::validate_graph(available, proposed, previous)
}
#[test]
fn optional_expiry_does_not_block_unrelated_units_but_changes_and_cycles_remain_strict() {
    let person = |id, unit, department| json!({"id":id,"status":"Active","companyScope":unit,"departmentId":department,"jobTitle":"普通员工","hireDate":"2026-01-01","organization":{"primaryAssignmentId":format!("primary-{id}"),"managerEmployeeId":null,"functionalManagerEmployeeIds":[],"additionalAssignments":[]}});
    let mut manager = person(1, "A", "A1");
    manager["organization"]["additionalAssignments"] = json!([{"id":"temporary-b","companyCode":"B","departmentId":"B1","jobTitle":"临时兼任","startsOn":"2026-01-01","endsOn":"2026-10-29","managerEmployeeId":null,"functionalManagerEmployeeIds":[]}]);
    let mut reporter = person(2, "B", "B2");
    reporter["organization"]["managerEmployeeId"] = json!(1);
    reporter["organization"]["managerAssignmentId"] = json!("temporary-b");
    let unrelated = person(3, "C", "C1");
    let rows = vec![manager.clone(), reporter.clone(), unrelated.clone()];
    let mut changed = unrelated.clone();
    changed["jobTitle"] = json!("其他单位的岗位维护");
    let today = "2026-10-30".parse().unwrap();
    validate_graph(rows.clone(), &changed, &unrelated, today).unwrap();
    let mut changed_reporter = reporter.clone();
    changed_reporter["jobTitle"] = json!("调整汇报岗位");
    assert_eq!(
        validate_graph(rows.clone(), &changed_reporter, &reporter, today)
            .unwrap_err()
            .status,
        Some(400)
    );
    validate_graph(
        rows.clone(),
        &changed_reporter,
        &reporter,
        "2026-10-29".parse().unwrap(),
    )
    .unwrap();
    let mut previous_manager = manager.clone();
    previous_manager["organization"]["additionalAssignments"][0]["endsOn"] = json!("");
    assert_eq!(
        validate_graph(rows.clone(), &manager, &previous_manager, today)
            .unwrap_err()
            .status,
        Some(400)
    );
    let mut fixed = reporter.clone();
    fixed["organization"]["managerEmployeeId"] = Value::Null;
    fixed["organization"]["managerAssignmentId"] = Value::Null;
    validate_graph(rows, &fixed, &reporter, today).unwrap();
    let mut first = person(4, "A", "A2");
    let second = person(5, "A", "A3");
    first["organization"]["managerEmployeeId"] = json!(5);
    first["organization"]["managerAssignmentId"] = json!("primary-5");
    let mut looped = second.clone();
    looped["organization"]["managerEmployeeId"] = json!(4);
    looped["organization"]["managerAssignmentId"] = json!("primary-4");
    assert_eq!(
        validate_graph(vec![first, second.clone()], &looped, &second, today)
            .unwrap_err()
            .status,
        Some(400)
    );
}
