use export_doc_storage::{
    Connection, DataScope, ErrorKind, GenericQuery, JobQuery, JobRetention, RecordWrite,
};
use serde_json::{Value, json};
fn insert(c: &Connection, kind: &str, mut row: Value) -> i64 {
    let identity = row["jobId"].as_str();
    let id = c
        .insert(&RecordWrite {
            kind,
            identity,
            body: &row,
        })
        .unwrap();
    row["id"] = json!(id);
    c.set_body(id, &row).unwrap();
    id
}
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let amount: Value = serde_json::from_str("12345678901234.123456789").unwrap();
    for (owner, company, department) in
        [(1, "A", "OLD"), (2, "A", "D"), (2, "A", "X"), (1, "B", "D")]
    {
        insert(
            c,
            "query-contract",
            json!({"versionNumber":1,"ownerUserId":owner,"companyScope":company,"departmentId":department,"status":"Draft","label":"E\u{301}cole 中文","nested":{"enabled":true},"nullable":null,"amount":amount,"largeBody":"unused body".repeat(4096)}),
        );
    }
    let fields = [
        ("missing", "absent"),
        ("nullable", "nullable"),
        ("id", "id"),
        ("enabled", "nested.enabled"),
        ("amount", "amount"),
    ];
    let mut q = GenericQuery {
        kind: "query-contract",
        scope: DataScope {
            company: "A",
            department: "D",
            user_id: 1,
            own: true,
            department_wide: true,
            ..Default::default()
        },
        filters: &[],
        keyword: "",
        search_root: None,
        fields: &fields,
        offset: 0,
        limit: 1,
    };
    let (total, rows) = c.query_generic(&q).unwrap();
    assert_eq!(total, 2);
    assert_eq!(rows.len(), 1);
    assert_eq!(rows[0]["enabled"], true);
    assert!(rows[0].get("missing").is_none());
    assert_eq!(rows[0].get("nullable"), Some(&Value::Null));
    assert_eq!(rows[0]["amount"], amount);
    assert!(!rows[0].to_string().contains("unused body"));
    q.offset = 999;
    let (total, empty) = c.query_generic(&q).unwrap();
    assert_eq!(total, 2);
    assert!(empty.is_empty());
    q.offset = 0;
    q.keyword = "éCOLE";
    assert_eq!(c.query_generic(&q).unwrap().0, 2);
    q.keyword = "\"status\":\"draft\"";
    assert_eq!(c.query_generic(&q).unwrap().0, 2);
    q.keyword = "' OR 1=1 --";
    assert_eq!(c.query_generic(&q).unwrap().0, 0);
    q.keyword = "";
    let filters = [("departmentId", "D' OR 1=1 --".into())];
    q.filters = &filters;
    assert_eq!(c.query_generic(&q).unwrap().0, 0);
    q.filters = &[];
    let names: Vec<_> = (0..70).map(|n| format!("field{n}")).collect();
    let wide: Vec<_> = names.iter().map(|name| (name.as_str(), "id")).collect();
    q.fields = &wide;
    assert_eq!(
        c.query_generic(&q).unwrap().1[0].as_object().unwrap().len(),
        70
    );
    let invalid = [("invalid'", "id")];
    q.fields = &invalid;
    assert!(c.query_generic(&q).is_err());
    c.rollback().unwrap();
    jobs(c);
}
fn jobs(c: &Connection) {
    c.begin().unwrap();
    let base = chrono::DateTime::parse_from_rfc3339("2026-09-30T00:00:00Z").unwrap();
    for n in 0..230 {
        let owner = 1 + n % 3;
        let time = (base + chrono::Duration::seconds(n)).to_rfc3339();
        insert(
            c,
            "background-jobs",
            json!({"jobId":format!("job-{n}"),"versionNumber":1,"ownerUserId":owner,"requestedByUserId":owner,"companyScope":"A","departmentId":"D","status":"Succeeded","createdAt":time,"completedAt":time,"title":"中文文件任务","_retry":{"operation":"Export","body":"large input".repeat(2048)}}),
        );
    }
    for (owner, cancel) in [(1, false), (2, true)] {
        insert(
            c,
            "background-jobs",
            json!({"jobId":format!("running-{owner}"),"versionNumber":1,"ownerUserId":owner,"requestedByUserId":owner,"companyScope":"A","departmentId":"D","status":"Running","createdAt":base.to_rfc3339(),"cancelRequested":cancel}),
        );
    }
    let fields = [
        ("id", "id"),
        ("versionNumber", "versionNumber"),
        ("status", "status"),
        ("cancelRequested", "cancelRequested"),
        ("_retryOperation", "_retry.operation"),
    ];
    let mut q = JobQuery {
        owner: Some(2),
        active: None,
        status: "Canceling",
        keyword: "",
        retention: None,
        fields: &fields,
        offset: 0,
        limit: 200,
    };
    let (total, rows) = c.query_jobs(&q).unwrap();
    assert_eq!(total, 1);
    assert_eq!(rows[0]["cancelRequested"], true);
    q.owner = None;
    q.status = "Succeeded";
    q.keyword = "中文";
    let (total, rows) = c.query_jobs(&q).unwrap();
    assert_eq!(total, 230);
    assert_eq!(rows.len(), 200);
    assert!(!rows[0].to_string().contains("large input"));
    q.status = "";
    q.keyword = "";
    q.active = Some(false);
    let cutoff = (base + chrono::Duration::seconds(100)).to_rfc3339();
    q.retention = Some(JobRetention {
        cutoff: &cutoff,
        per_user: 2,
        global: 5,
    });
    let mut removed = 0;
    loop {
        let (_, rows) = c.query_jobs(&q).unwrap();
        if rows.is_empty() {
            break;
        }
        for row in rows {
            assert!(
                c.delete("background-jobs", row["id"].as_i64().unwrap(), 1)
                    .unwrap()
            );
            removed += 1;
        }
    }
    assert_eq!(removed, 225);
    let counts = c.job_counts().unwrap();
    assert_eq!(counts["Succeeded"], 5);
    assert_eq!(counts["Running"], 2);
    insert(
        c,
        "background-jobs",
        json!({"jobId":"corrupted-time","versionNumber":1,"ownerUserId":1,"requestedByUserId":1,"companyScope":"A","departmentId":"D","status":"Failed","createdAt":"invalid","completedAt":"invalid"}),
    );
    assert_eq!(c.query_jobs(&q).unwrap_err().kind, ErrorKind::Unavailable);
    c.rollback().unwrap();
}
