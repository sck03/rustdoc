use export_doc_storage::{Connection, DataScope, PersonnelQuery, RecordWrite};
use serde_json::json;

pub fn exercise(connection: &Connection) {
    connection.begin().unwrap();
    super::organization_fixture::unit(connection, "A", &["OLD", "D", "X"]);
    super::organization_fixture::unit(connection, "B", &["D"]);
    let mut ids = Vec::new();
    for (index, (company, department, owner, status)) in [
        ("A", "OLD", 1, "Active"),
        ("A", "D", 2, "Probation"),
        ("A", "X", 2, "Active"),
        ("B", "D", 1, "Active"),
        ("A", "D", 2, "Departed"),
    ]
    .into_iter()
    .enumerate()
    {
        let number = format!("PERSON-{index}");
        let mut row = json!({"versionNumber":1,"ownerUserId":owner,"companyScope":company,"departmentId":department,
            "organization":{"primaryAssignmentId":format!("person-{index}-primary")},
            "employeeNumber":number,"status":status,"profile":{"fullName":"同名 E\u{301}cole"}});
        let id = connection
            .insert(&RecordWrite {
                kind: "people",
                identity: Some(&number.to_lowercase()),
                body: &row,
            })
            .unwrap();
        row["id"] = json!(id);
        connection.set_body(id, &row).unwrap();
        connection
            .set_personnel_private_profile(id, &json!({"notes":"private-personnel".repeat(4096)}))
            .unwrap();
        ids.push(id);
    }
    let scopes = [DataScope {
        company: "A",
        department: "D",
        user_id: 1,
        own: true,
        department_wide: true,
        ..Default::default()
    }];
    let mut query = PersonnelQuery {
        as_of: "2026-10-29",
        company: "A",
        department: "",
        status: "",
        keyword: "éCOLE",
        search_fields: &["profile.fullName", "employeeNumber"],
        scopes: &scopes,
        fields: &[
            ("id", "id"),
            ("name", "profile.fullName"),
            ("employeeNumber", "employeeNumber"),
        ],
        offset: 0,
        limit: 1,
    };
    let (total, rows) = connection.query_personnel(&query).unwrap();
    assert_eq!(
        total, 2,
        "own and department grants form a union before paging"
    );
    assert_eq!(rows[0]["id"], ids[0]);
    assert_eq!(
        rows[0].as_object().unwrap().len(),
        3,
        "private profiles are not transferred"
    );
    query.offset = 1;
    assert_eq!(
        connection.query_personnel(&query).unwrap().1[0]["id"],
        ids[1]
    );
    query.offset = 99;
    let (total, rows) = connection.query_personnel(&query).unwrap();
    assert_eq!(total, 2);
    assert!(rows.is_empty());
    query.offset = 0;
    query.keyword = "private-personnel";
    assert_eq!(
        connection.query_personnel(&query).unwrap().0,
        0,
        "private fields cannot be searched"
    );
    query.keyword = "' OR 1=1 --";
    assert_eq!(connection.query_personnel(&query).unwrap().0, 0);
    query.keyword = "";
    let restricted = [
        DataScope {
            company: "A",
            company_wide: true,
            ..Default::default()
        },
        DataScope {
            company: "A",
            user_id: 1,
            own: true,
            ..Default::default()
        },
    ];
    query.scopes = &restricted;
    assert_eq!(
        connection.query_personnel(&query).unwrap().0,
        1,
        "private visibility intersects directory visibility"
    );
    query.scopes = &[];
    assert_eq!(
        connection.query_personnel(&query).unwrap().0,
        3,
        "company isolation remains mandatory for unscoped manager searches"
    );
    query.status = "Departed";
    assert_eq!(
        connection.query_personnel(&query).unwrap().1[0]["id"],
        ids[4]
    );
    query.status = "";
    query.department = "D";
    assert_eq!(connection.query_personnel(&query).unwrap().0, 1);
    let appointment_scope = [DataScope {
        company: "A",
        department: "D",
        department_wide: true,
        personnel_memberships: true,
        personnel_date: Some("2026-10-29"),
        ..Default::default()
    }];
    query.department = "";
    query.scopes = &appointment_scope;
    assert_eq!(
        connection.query_personnel(&query).unwrap().0,
        1,
        "dated department membership uses only bound parameters referenced by SQL"
    );
    let denied = [DataScope {
        company: "A",
        personnel_memberships: true,
        personnel_date: Some("2026-10-29"),
        ..Default::default()
    }];
    query.scopes = &denied;
    assert_eq!(connection.query_personnel(&query).unwrap().0, 0);
    for company in ["A", "B"] {
        let request = connection.insert(&RecordWrite { kind:"oa-general",identity:None,body:&json!({"versionNumber":1,"companyScope":company,"departmentId":"D","ownerUserId":1,"status":"Pending","submissionNumber":1,"title":"private request"}) }).unwrap();
        connection
            .replace_workflow_tasks(
                request,
                1,
                &[export_doc_storage::WorkflowTask {
                    key: format!("task-{company}"),
                    node: "review".into(),
                    assignee: ids[0],
                    context: "".into(),
                    acted_by: None,
                    acted_by_context: "".into(),
                    status: "Waiting".into(),
                    active: true,
                    independent: false,
                    due_at: "2026-10-28T23:00:00+08:00".into(),
                    started_at: "2026-10-28T00:00:00Z".into(),
                    acted_at: String::new(),
                    elapsed_seconds: None,
                }],
            )
            .unwrap();
    }
    let companies = ["A".to_owned()];
    let mut summary = export_doc_storage::WorkflowSummaryQuery {
        companies: &companies,
        as_of: "2026-10-29T00:00:00Z",
        completed_since: "2026-09-29T00:00:00Z",
        offset: 0,
        limit: 1,
    };
    let (total, rows) = connection.query_workflow_summary(&summary).unwrap();
    assert_eq!(total, 1);
    assert_eq!(rows[0]["activeTaskCount"], 1);
    assert_eq!(rows[0]["overdueTaskCount"], 1);
    assert_eq!(rows[0]["companyCode"], "A");
    assert_eq!(rows[0]["completedTaskCount"], 0);
    assert!(rows[0]["averageProcessingSeconds"].is_null());
    assert!(rows[0].get("title").is_none());
    for company in ["A", "B"] {
        let request = connection.insert(&RecordWrite { kind:"oa-general",identity:None,body:&json!({"versionNumber":1,"companyScope":company,"departmentId":"D","ownerUserId":1,"status":"Completed","submissionNumber":2}) }).unwrap();
        for submission in [1, 2] {
            let tasks = [
                (
                    "signed",
                    if submission == 1 {
                        "Returned"
                    } else {
                        "Approved"
                    },
                    "2026-10-28T23:00:00+08:00",
                ),
                ("old", "Approved", "2026-09-28T00:00:00Z"),
                ("future", "Approved", "2026-10-30T00:00:00Z"),
                ("skipped", "Skipped", ""),
                ("cancelled", "Cancelled", ""),
            ]
            .into_iter()
            .map(|(key, status, acted)| export_doc_storage::WorkflowTask {
                key: key.into(),
                node: "review".into(),
                assignee: ids[0],
                context: "".into(),
                acted_by: (!acted.is_empty()).then_some(ids[0]),
                acted_by_context: "".into(),
                status: status.into(),
                active: false,
                independent: false,
                due_at: "".into(),
                started_at: "2026-09-01T00:00:00Z".into(),
                acted_at: acted.into(),
                elapsed_seconds: (!acted.is_empty()).then_some(if company == "A" {
                    submission * 120
                } else {
                    90_000
                }),
            })
            .collect::<Vec<_>>();
            connection
                .replace_workflow_tasks(request, submission, &tasks)
                .unwrap();
        }
    }
    let (_, rows) = connection.query_workflow_summary(&summary).unwrap();
    assert_eq!(
        rows[0]["requestCount"], 1,
        "completed requests do not inflate pending count"
    );
    assert_eq!(
        rows[0]["completedTaskCount"], 2,
        "signed tasks include previous submissions, excluding skipped, cancelled and out-of-window tasks"
    );
    assert_eq!(
        rows[0]["averageProcessingSeconds"].as_f64(),
        Some(180.0),
        "other units must not affect duration statistics"
    );
    summary.offset = 1;
    let (total, rows) = connection.query_workflow_summary(&summary).unwrap();
    assert_eq!(total, 1);
    assert!(rows.is_empty());
    summary.companies = &[];
    assert_eq!(connection.query_workflow_summary(&summary).unwrap().0, 0);
    connection.rollback().unwrap();
}
