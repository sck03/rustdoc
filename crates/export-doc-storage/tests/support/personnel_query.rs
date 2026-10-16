use export_doc_storage::{Connection, DataScope, PersonnelQuery, RecordWrite};
use serde_json::json;

pub fn exercise(connection: &Connection) {
    connection.begin().unwrap();
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
            "employeeNumber":number,"status":status,"profile":{"fullName":"同名 E\u{301}cole","notes":"private-personnel".repeat(4096)}});
        let id = connection
            .insert(&RecordWrite {
                kind: "people",
                identity: Some(&number.to_lowercase()),
                body: &row,
            })
            .unwrap();
        row["id"] = json!(id);
        connection.set_body(id, &row).unwrap();
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
    connection.rollback().unwrap();
}
